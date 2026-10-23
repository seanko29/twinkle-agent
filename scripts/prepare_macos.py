"""Assemble macOS resources from public inputs only; no Windows/private data."""
from pathlib import Path
import json
import plistlib
import shutil

ROOT = Path(__file__).resolve().parents[1]


def prepare():
    build = ROOT / 'build' / 'macos'
    assert build.resolve().is_relative_to((ROOT / 'build').resolve())
    if build.exists():
        shutil.rmtree(build)
    app = build / 'Twinkle Agent.app'
    contents = app / 'Contents'
    resources = contents / 'Resources'
    (contents / 'MacOS').mkdir(parents=True)
    resources.mkdir()
    shutil.copy2(ROOT / 'twinkle_macos/Info.plist', contents / 'Info.plist')
    with (contents / 'Info.plist').open('rb') as handle:
        assert plistlib.load(handle)['LSMinimumSystemVersion'] == '12.0'
    for folder in ['assets', 'library']:
        source = ROOT / 'twinkle_desktop' / folder
        for file in source.rglob('*'):
            if file.is_file() and file.suffix.lower() in {'.png', '.json'}:
                target = resources / folder / file.relative_to(source)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(file, target)
    shutil.copy2(ROOT / 'twinkle_desktop/walk_preview.gif', resources / 'walk_preview.gif')
    shutil.copy2(ROOT / 'ASSET_NOTICE.md', resources / 'ASSET_NOTICE.md')
    shutil.copy2(ROOT / 'twinkle_desktop/ASSET_SOURCES.json', resources / 'ASSET_SOURCES.json')
    gallery = resources / 'gallery'
    shutil.copytree(ROOT / 'twinkle_twinkle', gallery,
                    ignore=shutil.ignore_patterns('__pycache__', '*.pyc', '.DS_Store'))
    guide = resources / 'guide'
    guide.mkdir()
    source = (ROOT / 'index.html').read_text(encoding='utf-8')
    source = source.replace('twinkle_twinkle/index.html', '../gallery/index.html')
    source = source.replace('twinkle_desktop/walk_preview.gif', '../walk_preview.gif')
    (guide / 'index.html').write_text(source, encoding='utf-8')
    index = gallery / 'index.html'
    index.write_text(index.read_text(encoding='utf-8').replace('href="../index.html"', 'href="../guide/index.html"'), encoding='utf-8')
    for file in resources.rglob('*'):
        assert file.suffix.lower() not in {'.exe', '.dll', '.dat', '.pfx', '.p12'}
        assert file.name not in {'settings.json', 'native_host.json', 'google_desktop.json', 'accounts.dat'}
    # Verify the payload rather than trusting copy operations.
    animations = json.loads((resources / 'assets/animations.json').read_text(encoding='utf-8'))
    library = json.loads((resources / 'library/catalog.json').read_text(encoding='utf-8'))
    assert len(animations) == 8 and len(library) == 47
    for item in list(animations.values()) + library:
        assert len(item['files']) == len(item['durations_ms']) > 0
        for relative in item['files']:
            path = (resources / relative).resolve()
            assert path.is_relative_to(resources.resolve()) and path.is_file()
    print('Prepared clean macOS app resources: 8 activities, 47 animations, offline KOR/ENG guide and gallery.')
    return app


if __name__ == '__main__':
    prepare()
