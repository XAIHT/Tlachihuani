"""Read-only installed process identity observations; never terminates a process."""
import json
from pathlib import Path
import sys
import psutil
edition=sys.argv[1]
name={'english':'Tlamatini','spanish':'Tlamatini-Spanish'}[edition]
expected=(Path('C:/')/name/(name+'.exe')).resolve()
found=[]
for process in psutil.process_iter(['pid','exe','create_time']):
    if process.info['exe'] and Path(process.info['exe']).resolve()==expected:
        found.append({'pid':process.pid,'path':str(expected),'created_at':process.info['create_time']})
print(json.dumps({'edition':edition,'processes':found}))
