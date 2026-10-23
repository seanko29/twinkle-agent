#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
if [[ "$(uname -s)" != Darwin ]]; then
  echo "Build on macOS with Xcode Command Line Tools (or use the macOS GitHub Actions workflow)." >&2
  exit 1
fi
if [[ -n "${NOTARY_PROFILE:-}" && -z "${CODESIGN_IDENTITY:-}" ]]; then
  echo "NOTARY_PROFILE requires a Developer ID CODESIGN_IDENTITY." >&2
  exit 1
fi
python3 scripts/verify_public.py
python3 scripts/prepare_macos.py
BUILD="$ROOT/build/macos"
APP="$BUILD/Twinkle Agent.app"
SDK="$(xcrun --sdk macosx --show-sdk-path)"
for ARCH in arm64 x86_64; do
  xcrun swiftc -O -swift-version 5 -sdk "$SDK" -target "$ARCH-apple-macosx12.0" \
    -framework AppKit -framework ImageIO -framework UniformTypeIdentifiers \
    twinkle_macos/Sources/*.swift -o "$BUILD/TwinkleAgent-$ARCH"
done
lipo -create "$BUILD/TwinkleAgent-arm64" "$BUILD/TwinkleAgent-x86_64" \
  -output "$APP/Contents/MacOS/TwinkleAgent"
ICONSET="$BUILD/Twinkle.iconset"
mkdir -p "$ICONSET"
ICON_SOURCE="$ROOT/twinkle_desktop/assets/idle/000.png"
for SIZE in 16 32 128 256 512; do
  sips -z "$SIZE" "$SIZE" "$ICON_SOURCE" --out "$ICONSET/icon_${SIZE}x${SIZE}.png" >/dev/null
  DOUBLE=$((SIZE * 2))
  sips -z "$DOUBLE" "$DOUBLE" "$ICON_SOURCE" --out "$ICONSET/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/Twinkle.icns"
if [[ -n "${CODESIGN_IDENTITY:-}" ]]; then
  codesign --force --options runtime --timestamp --sign "$CODESIGN_IDENTITY" "$APP"
else
  codesign --force --sign - "$APP"
fi
codesign --verify --deep --strict "$APP"
lipo -verify_arch arm64 x86_64 "$APP/Contents/MacOS/TwinkleAgent"
"$APP/Contents/MacOS/TwinkleAgent" --self-test
"$APP/Contents/MacOS/TwinkleAgent" --smoke-test
if [[ -n "${NOTARY_PROFILE:-}" ]]; then
  ditto -c -k --sequesterRsrc --keepParent "$APP" "$BUILD/notarize.zip"
  xcrun notarytool submit "$BUILD/notarize.zip" --keychain-profile "$NOTARY_PROFILE" --wait
  xcrun stapler staple "$APP"
  xcrun stapler validate "$APP"
fi
DIST="$ROOT/dist/macos"
mkdir -p "$DIST" "$BUILD/dmg"
ditto "$APP" "$BUILD/dmg/Twinkle Agent.app"
ln -s /Applications "$BUILD/dmg/Applications"
cp twinkle_macos/INSTALL.txt "$BUILD/dmg/INSTALL.txt"
cp ASSET_NOTICE.md "$BUILD/dmg/ASSET_NOTICE.md"
hdiutil create -volname "Twinkle Agent" -srcfolder "$BUILD/dmg" -ov -format UDZO "$DIST/TwinkleAgent_macOS_universal.dmg"
if [[ -n "${CODESIGN_IDENTITY:-}" ]]; then
  codesign --force --timestamp --sign "$CODESIGN_IDENTITY" "$DIST/TwinkleAgent_macOS_universal.dmg"
fi
if [[ -n "${NOTARY_PROFILE:-}" ]]; then
  xcrun notarytool submit "$DIST/TwinkleAgent_macOS_universal.dmg" --keychain-profile "$NOTARY_PROFILE" --wait
  xcrun stapler staple "$DIST/TwinkleAgent_macOS_universal.dmg"
fi
ditto -c -k --sequesterRsrc --keepParent "$APP" "$DIST/TwinkleAgent_macOS_universal.zip"
cp "$BUILD/TwinkleAgent_macOS_preview.png" "$DIST/"
cp twinkle_macos/INSTALL.txt "$DIST/INSTALL.txt"
(cd "$DIST" && shasum -a 256 TwinkleAgent_macOS_universal.dmg TwinkleAgent_macOS_universal.zip > SHA256SUMS.txt)
echo "Built Apple Silicon + Intel macOS 12+ app, ZIP and DMG in dist/macos/."
if [[ -z "${CODESIGN_IDENTITY:-}" ]]; then
  echo "PREVIEW: ad-hoc signed; not Developer ID signed or notarized. See INSTALL.txt."
fi
