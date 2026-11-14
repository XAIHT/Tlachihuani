"""Start the fixed installed edition with its real, independent main console."""
import argparse
import ctypes
from ctypes import wintypes
import json
import os
from pathlib import Path
import subprocess
import time
import psutil

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('edition',choices=['english','spanish'])
parser.add_argument('output',type=Path)
args=parser.parse_args()
root=Path(__file__).resolve().parents[2]
output=args.output.resolve();output.relative_to(root/'artifacts')
install=Path('C:/Tlamatini' if args.edition=='english' else 'C:/Tlamatini-Spanish')
image=install/('Tlamatini.exe' if args.edition=='english' else 'Tlamatini-Spanish.exe')
assert not [p for p in psutil.process_iter(['exe']) if p.info['exe'] and Path(p.info['exe'])==image]
config=json.loads((install/'config.json').read_text(encoding='utf-8-sig'))
port=int(config.get('django_port',8000));assert 1024<=port<=65535
env=dict(os.environ,**{key:str(install/'Temp') for key in ['TEMP','TMP','TMPDIR','TLAMATINI_TEMP']})
began=int(time.time()*1000)
child=subprocess.Popen([str(image)],cwd=install,env=env,creationflags=subprocess.CREATE_NEW_CONSOLE)
get_times=ctypes.WinDLL('kernel32',use_last_error=True).GetProcessTimes
get_times.argtypes=[wintypes.HANDLE]+[ctypes.POINTER(wintypes.FILETIME)]*4;get_times.restype=wintypes.BOOL
times=[wintypes.FILETIME() for _ in range(4)]
assert get_times(int(child._handle),*(ctypes.byref(t) for t in times))
created=(times[0].dwHighDateTime<<32)|times[0].dwLowDateTime
(output/'application.json').write_text(json.dumps({'edition':args.edition,'executable':str(image),'pid':child.pid,
    'startedAt':began,'creation_time':str(created),'base':'http://127.0.0.1:'+str(port),'dedicated_console':True},indent=2))
print('Started installed '+args.edition+' with a dedicated main console.',flush=True)
