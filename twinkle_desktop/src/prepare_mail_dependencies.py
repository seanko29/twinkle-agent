from pathlib import Path
import urllib.request,zipfile,io,json,hashlib
root=Path(__file__).resolve().parent.parent
packages=[('MailKit','4.18.1'),('MimeKit','4.18.1'),('BouncyCastle.Cryptography','2.7.0'),('System.Formats.Asn1','10.0.0'),('System.Memory','4.6.3'),('System.Buffers','4.6.1'),('System.Numerics.Vectors','4.6.1'),('System.Runtime.CompilerServices.Unsafe','6.1.2'),('System.Threading.Tasks.Extensions','4.6.3')]
records=[];license_dir=root/'third_party';license_dir.mkdir(exist_ok=True)
for name,version in packages:
    url=f'https://api.nuget.org/v3-flatcontainer/{name.lower()}/{version}/{name.lower()}.{version}.nupkg'
    data=urllib.request.urlopen(url,timeout=60).read();z=zipfile.ZipFile(io.BytesIO(data))
    files=[]
    for framework in ['net48','net47','net472','net462','net461','net46','net45','netstandard2.0']:
        candidates=[n for n in z.namelist() if n.startswith('lib/'+framework+'/') and n.count('/')==2 and n.endswith('.dll')]
        if candidates:
            for n in candidates:(root/Path(n).name).write_bytes(z.read(n));files.append(Path(n).name)
            break
    assert files,name
    license_files=[n for n in z.namelist() if 'license' in n.lower() and n.lower().endswith(('.txt','.md'))]
    for i,n in enumerate(license_files):(license_dir/(name+'-'+str(i)+'-'+Path(n).name)).write_bytes(z.read(n))
    (license_dir/(name+'.nuspec')).write_bytes(z.read(next(n for n in z.namelist() if n.endswith('.nuspec'))))
    records.append({'package':name,'version':version,'url':url,'package_sha256':hashlib.sha256(data).hexdigest(),'assemblies':files})
    print(name,version,files)
(license_dir/'MAIL_DEPENDENCIES.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
