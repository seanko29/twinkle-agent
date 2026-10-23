"""Local completion notification; does not call model APIs or read provider credentials."""
from pathlib import Path
import json, os, subprocess, tempfile

def notify(source, title, message=''):
    executable = Path(__file__).resolve().parent.parent / 'TwinkleDesktop.exe'
    fd, path = tempfile.mkstemp(prefix='twinkle-notify-', suffix='.json')
    try:
        with os.fdopen(fd, 'w', encoding='utf-8') as output:
            json.dump({'source': source, 'title': title, 'message': message}, output, ensure_ascii=False)
        subprocess.run([str(executable), '--notify-json', path], check=True, creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    finally:
        Path(path).unlink(missing_ok=True)
