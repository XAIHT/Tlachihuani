"""Install only verified frozen runtime files; preserve every existing database.

Run in the verified visible development console after both applications stop.
The independently installed runtimes retain their registry entries, data, agent
assets and carried Python. Every changed runtime file has a rollback copy.
"""
from pathlib import Path
import datetime
import hashlib
import json
import shutil
import sys
import uuid
import psutil
import re

try:  # Verbose script log (script-logs, copied into Tlachihuani.log by XT); see script_trace.py.
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import script_trace
    script_trace.start(__file__)
except ImportError:
    pass

workspace=Path(__file__).resolve().parents[1]
build_base=workspace/'artifacts/main-rc-freeze'
build_suffix=sys.argv[1] if len(sys.argv)>1 else '20261113-2335'
if not re.fullmatch(r'20\d{6}-\d{4}', build_suffix):
    raise SystemExit('Expected the exact verified build-pair timestamp')
selected_edition=sys.argv[2] if len(sys.argv)>2 else 'both'
if selected_edition not in ('english','spanish','both'):
    raise SystemExit('Expected english, spanish or both')
record_path=workspace/'artifacts/rc-local-diagnosis/main-installed.json'
previous_results=json.loads(record_path.read_text(encoding='utf-8-sig')) if selected_edition!='both' else []
if selected_edition!='both' and {item['edition'] for item in previous_results}!={'english','spanish'}:
    raise SystemExit('A single-edition update requires complete prior installation evidence')
backup_base=workspace/'artifacts/main-rc-installed-backup'/uuid.uuid4().hex
backup_base.mkdir(parents=True)

def digest(path):
    result=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):result.update(block)
    return result.hexdigest()

def safe_path(base,relative):
    path=(base/relative).resolve()
    if not path.is_relative_to(base.resolve()) or path==base.resolve():raise RuntimeError('Path outside expected installation')
    return path

results=[]
for edition, name in [('english','Tlamatini'),('spanish','Tlamatini-Spanish')]:
    if selected_edition!='both' and edition!=selected_edition:
        continue
    install=Path('C:/')/name
    exe=install/(name+'.exe')
    for process in psutil.process_iter(['pid','exe']):
        if process.info['exe'] and Path(process.info['exe']).resolve()==exe.resolve():
            raise RuntimeError('Stop the installed '+name+' normally before replacing runtime files')
    if not exe.is_file():raise RuntimeError('Expected registered application payload is absent')
    build=build_base/(edition+'-'+build_suffix)
    result=json.loads((build/'result.json').read_text())
    if result.get('passed') is not True:raise RuntimeError('Frozen build did not pass')
    frozen=build/'dist/manage'
    manifest=json.loads((build/'payload-sha256.json').read_text())
    for relative, expected in manifest.items():
        if Path(relative).name.startswith('db.sqlite3'):raise RuntimeError('Runtime overlay must never contain a database')
        if digest(safe_path(frozen,relative))!=expected:raise RuntimeError('Frozen payload changed: '+relative)
    protected={path:digest(path) for relative in ['config.json','_internal/db.sqlite3','_internal/db.sqlite3-wal','_internal/db.sqlite3-shm','db.sqlite3'] if (path:=safe_path(install,relative)).is_file()}
    backup=backup_base/name;backup.mkdir()
    changed=[];copied={}
    try:
        for index,(relative,expected) in enumerate(manifest.items(),1):
            destination=name+'.exe' if relative=='manage.exe' else relative
            source=safe_path(frozen,relative);target=safe_path(install,destination)
            if target.exists() and digest(target)==expected:continue
            old=safe_path(backup,destination)
            if target.exists():old.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(target,old)
            changed.append((target,old if old.exists() else None))
            target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
            if digest(target)!=expected:raise RuntimeError('Installed hash mismatch: '+destination)
            copied[destination]={'bytes':target.stat().st_size,'sha256':expected}
            if len(copied)%50==0:print(name, len(copied),'runtime files updated',flush=True)
        source_root=Path('C:/Development/XAIHT')/name
        snapshot=install/'TlamatiniSourceCode'
        source_records={}
        for relative in ['remote_controller_runtime.py','remote_controller_bridge.py','remote_controller_views.py','remote_mobile_views.py','urls.py','services/response_parser.py','rag/interface.py','test_prompt_shape_sentences.py','test_remote_controller_runtime.py','test_remote_controller_bridge.py','test_remote_mobile_views.py']:
            source=source_root/'Tlamatini/agent'/relative;destination='TlamatiniSourceCode/Tlamatini/agent/'+relative
            target=safe_path(install,destination);old=safe_path(backup,destination)
            if target.exists():old.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(target,old)
            changed.append((target,old if old.exists() else None));target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
            source_records[relative]=digest(target);copied[destination]={'bytes':target.stat().st_size,'sha256':source_records[relative]}
        manifest_path=install/'runtime-assets.json'
        if manifest_path.is_file():
            shutil.copy2(manifest_path,backup/'runtime-assets.json');changed.append((manifest_path,backup/'runtime-assets.json'))
            runtime=json.loads(manifest_path.read_text(encoding='utf-8'));runtime['files'].update(copied)
            manifest_path.write_text(json.dumps(runtime,indent=2,sort_keys=True)+'\n',encoding='utf-8')
        for path,expected in protected.items():
            if digest(path)!=expected:raise RuntimeError('Protected application data changed during runtime update')
        for relative,expected in manifest.items():
            destination=name+'.exe' if relative=='manage.exe' else relative
            if digest(safe_path(install,destination))!=expected:raise RuntimeError('Final installed runtime differs')
        results.append({'edition':edition,'installed':str(install),'build':str(build),'verified_files':len(manifest),
            'changed_files':len(copied),'protected_data_unchanged':True,'rollback':str(backup),'source_overlay':source_records,
            'installed_at':datetime.datetime.now(datetime.timezone.utc).isoformat()})
        print('PASS',name, len(manifest),'frozen runtime files verified; database and configuration unchanged',flush=True)
    except BaseException:
        for target,old in reversed(changed):
            if not target.resolve().is_relative_to(install.resolve()):raise RuntimeError('Unsafe rollback target')
            if old:shutil.copy2(old,target)
            elif target.is_file():target.unlink()
        raise
if selected_edition!='both':
    results += [item for item in previous_results if item['edition']!=selected_edition]
results.sort(key=lambda item:item['edition'])
record_path.write_text(json.dumps(results,indent=2),encoding='utf-8')
