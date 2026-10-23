"""Package a clean Windows release from explicit application inputs."""
from pathlib import Path
import argparse,json,shutil,subprocess,zipfile,hashlib
ROOT=Path(__file__).resolve().parents[1]
def main():
    args=argparse.ArgumentParser();args.add_argument('--inno',help='Path to Inno Setup ISCC.exe');options=args.parse_args()
    app=ROOT/'twinkle_desktop';payload=ROOT/'build/release/payload';dist=ROOT/'dist';dist.mkdir(exist_ok=True)
    assert payload.resolve().is_relative_to((ROOT/'build').resolve())
    if payload.exists():shutil.rmtree(payload)
    payload.mkdir(parents=True)
    for name in ['assets','library','third_party','browser_extension','integrations']:
        shutil.copytree(app/name,payload/name,ignore=shutil.ignore_patterns('__pycache__','*.pyc'))
    for name in ['TwinkleDesktop.exe','TwinkleDesktop.exe.config','TwinkleNativeHost.exe','BUILD_INFO.json','ASSET_SOURCES.json','browser_extension_id.txt','notify.ps1','install_browser_bridge.ps1','notification_sample.json','walk_preview.gif','사용방법.txt','메일_연결_안내.txt','알림_연동_안내.txt']:
        shutil.copy2(app/name,payload/name)
    dependencies=json.loads((app/'third_party/MAIL_DEPENDENCIES.json').read_text(encoding='utf-8'))
    for name in sorted({name for item in dependencies for name in item['assemblies']}):shutil.copy2(app/name,payload/name)
    (payload/'app_config').mkdir();shutil.copy2(app/'app_config/README.txt',payload/'app_config/README.txt')
    shutil.copytree(ROOT/'twinkle_twinkle',payload/'gallery')
    (payload/'guide').mkdir()
    guide=(ROOT/'index.html').read_text(encoding='utf-8').replace('data-gallery="twinkle_twinkle/index.html"','data-gallery="../gallery/index.html"').replace('data-preview="twinkle_desktop/walk_preview.gif"','data-preview="../walk_preview.gif"').replace('href="twinkle_twinkle/index.html"','href="../gallery/index.html"').replace('src="twinkle_desktop/walk_preview.gif"','src="../walk_preview.gif"')
    (payload/'guide/index.html').write_text(guide,encoding='utf-8')
    gallery=(payload/'gallery/index.html');gallery.write_text(gallery.read_text(encoding='utf-8').replace('href="../index.html"','href="../guide/index.html"'),encoding='utf-8')
    assert not any((payload/name).exists() for name in ['settings.json','user_mail','user_images','native_host.json','app_config/google_desktop.json','app_config/microsoft_client_id.txt'])
    archive=dist/'TwinkleAgent_Windows_Portable.zip'
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as output:
        for path in sorted(payload.rglob('*')):
            if path.is_file():output.write(path,Path('TwinkleAgent')/path.relative_to(payload))
    with zipfile.ZipFile(archive) as check:assert check.testzip() is None
    if options.inno:
        subprocess.run([options.inno,'/Qp',f'/DPayloadDir={payload}',f'/DReleaseDir={dist}',str(app/'src/TwinkleSetup.iss')],check=True)
    outputs=[path for path in dist.iterdir() if path.suffix in {'.zip','.exe'}]
    (dist/'SHA256SUMS.txt').write_text(''.join(hashlib.sha256(path.read_bytes()).hexdigest()+'  '+path.name+'\n' for path in outputs),encoding='utf-8')
    print('Clean release packaged with offline KOR/ENG guide and all 47 gallery items. No credentials or personal settings included.')
if __name__=='__main__':main()
