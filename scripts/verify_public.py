"""Validate tracked publication files without printing candidate secret values."""
from pathlib import Path
import json,re,subprocess,sys
ROOT=Path(__file__).resolve().parents[1]
IGNORED={'.git','build','dist','work','__pycache__'}
SECRET_PATTERNS=[
    re.compile(rb'GOCSPX-[A-Za-z0-9_-]{15,}'),
    re.compile(rb'AIza[A-Za-z0-9_-]{30,}'),
    re.compile(rb'gh[pousr]_[A-Za-z0-9]{20,}'),
    re.compile(rb'github_pat_[A-Za-z0-9_]{20,}'),
    re.compile(rb'sk-[A-Za-z0-9_-]{24,}'),
    re.compile(rb'\d{8,}-[a-zA-Z0-9_-]{12,}\.apps\.googleusercontent\.com'),
    re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
    re.compile(rb'[A-Za-z]:[\\/]Users[\\/][^\s"\'<>]+'),
]
def candidates():
    result=subprocess.run(['git','ls-files','-z'],cwd=ROOT,capture_output=True)
    if result.returncode==0 and result.stdout:
        return [ROOT/p.decode('utf-8') for p in result.stdout.split(b'\0') if p]
    return [p for p in ROOT.rglob('*') if p.is_file() and not (set(p.relative_to(ROOT).parts)&IGNORED) and p.suffix.lower() not in {'.exe','.zip','.pyc','.pdb'} and p.name not in {'BUILD_INFO.json','native_host.json','settings.json','error.log'}]
def verify_files(files):
    failures=[]
    for path in files:
        rel=path.relative_to(ROOT)
        if {'user_mail','user_images'}&set(rel.parts) or path.name in {'accounts.dat','google_desktop.json','microsoft_client_id.txt','settings.json','native_host.json','error.log'}:
            failures.append(f'private runtime/config file: {rel}');continue
        data=path.read_bytes()
        for index,pattern in enumerate(SECRET_PATTERNS):
            if pattern.search(data):failures.append(f'potential credential/local path pattern {index+1}: {rel}')
        if 'third_party' not in rel.parts and path.suffix.lower() in {'.cs','.py','.ps1','.js','.json','.txt','.md','.html','.csv','.yml','.yaml','.xml','.config'}:
            emails=re.findall(rb'[A-Za-z0-9_.+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}',data)
            for email in emails:
                if not email.endswith((b'@example.test',b'@example.com',b'@example.invalid',b'@users.noreply.github.com')):
                    failures.append(f'unexpected email address: {rel}');break
    return failures
def local(path,base):
    resolved=(base/path).resolve()
    if not resolved.is_relative_to(ROOT):raise AssertionError('Reference escapes publication tree')
    if not resolved.exists():raise AssertionError('Missing local asset: '+str(resolved.relative_to(ROOT)))
def verify_assets():
    gallery=ROOT/'twinkle_twinkle';catalog=json.loads((gallery/'catalog.json').read_text(encoding='utf-8'))['assets']
    assert len(catalog)==47 and sum(asset['frames'] for asset in catalog)==555
    for asset in catalog:
        for key in ['original_file','preview_file','sprite_file','animation_file','frame_folder']:local(asset[key],gallery)
        assert asset['source_page'].startswith('https://') and 'source_status' in asset
    app=ROOT/'twinkle_desktop'
    clips=json.loads((app/'assets/animations.json').read_text(encoding='utf-8'))
    for clip in clips.values():
        assert len(clip['files'])==len(clip['durations_ms']) and all(value>=20 for value in clip['durations_ms'])
        for path in clip['files']:local(path,app)
    items=json.loads((app/'library/catalog.json').read_text(encoding='utf-8'));assert len(items)==47
    for item in items:
        local(item['Preview'],app)
        for path in item['files']:local(path,app)
    for html in [ROOT/'index.html',gallery/'index.html']:
        source=html.read_text(encoding='utf-8');assert 'KOR' in source and 'ENG' in source
        for target in re.findall(r'(?:src|href)="([^"]+)"',source):
            if target.startswith(('https:','http:','#')):continue
            local(target.split('?')[0],html.parent)
    assert 'enabled: true' not in (app/'browser_extension/background.js').read_text(encoding='utf-8')
if __name__=='__main__':
    files=candidates();failures=verify_files(files)
    if failures:
        print('\n'.join(failures));sys.exit(1)
    verify_assets()
    print(f'PASS: {len(files)} publication files checked; no detected credentials, personal email addresses, local home paths or private runtime files.')
    print('PASS: all 47 gallery assets, 555 frames, native animation references and bilingual-page links resolve; browser detection is opt-in.')
