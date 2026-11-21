"""Freeze the edited main app without invoking its destructive release main().

Uses the checkout's exact PyInstaller command construction, pinned existing
build environment, version and hooks. All new output goes to a new evidence
folder. It does not remove databases, dist, installed files or install deps.
"""
from pathlib import Path
import ast
import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import time

try:  # Verbose script log (script-logs, copied into Tlachihuani.log by XT); see script_trace.py.
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import script_trace
    script_trace.start(__file__)
except ImportError:
    pass

repo = Path(sys.argv[1]).resolve()
out = Path(sys.argv[2]).resolve()
if repo not in (Path('C:/Development/XAIHT/Tlamatini'), Path('C:/Development/XAIHT/Tlamatini-Spanish')):
    raise SystemExit('Unknown edition checkout')
evidence_root = Path(__file__).resolve().parents[1] / 'artifacts'
if not out.is_relative_to(evidence_root) or out.exists():
    raise SystemExit('Use a fresh checkout-owned evidence folder')
if 'python-envs' not in str(Path(sys.executable)) or sys.prefix == sys.base_prefix:
    raise SystemExit('Use the existing sealed build environment')
out.mkdir(parents=True)
for name in ('TEMP','TMP','TMPDIR','PYINSTALLER_CONFIG_DIR'):
    target = out / name.lower();target.mkdir();os.environ[name]=str(target)
os.environ['PYTHONNOUSERSITE']='1'
os.environ['PYTHONDONTWRITEBYTECODE']='1'
os.environ['PYTHONUTF8']='1'
os.environ['PYTHONPATH']=os.pathsep.join((str(repo),str(repo/'Tlamatini')))
os.chdir(repo)
sys.path.insert(0,str(repo))
source = (repo/'build.py').read_text(encoding='utf-8')
spec = importlib.util.spec_from_file_location('rc_validation_build_definition',repo/'build.py')
build = importlib.util.module_from_spec(spec);spec.loader.exec_module(build)
# The destructive main() is NEVER called. Reuse only its reviewed command
# construction nodes, beginning at DLL collection and ending before execution.
tree = ast.parse(source)
main = next(node for node in tree.body if isinstance(node,ast.FunctionDef) and node.name=='main')
start = next(i for i,node in enumerate(main.body) if isinstance(node,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='dll_args' for t in node.targets))
end = next(i for i,node in enumerate(main.body[start:],start) if isinstance(node,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='result' for t in node.targets))
construction = ast.Module(body=main.body[start:end],type_ignores=[])
version_spec=importlib.util.spec_from_file_location('rc_validation_version',repo/'Tlamatini/agent/_version.py')
version=importlib.util.module_from_spec(version_spec);version_spec.loader.exec_module(version)
version_value=getattr(version,'__version__',None) or getattr(version,'VERSION',None)
if version_value!='1.93.5': raise SystemExit('Unexpected application version: '+str(version_value))
from versioning import render_versioninfo_for
executable='Tlamatini-Spanish.exe' if repo.name.endswith('Spanish') else 'Tlamatini.exe'
version_file=render_versioninfo_for('1.93.5',out/'version-info.txt',product_name=repo.name,original_filename=executable)
build.validate_migration_dependencies(repo)
build.validate_source_assets(repo)
build.verify_collected_static(repo)
namespace=dict(vars(build),repo_root=repo,self_modify=True,separator=';',version_file_path=version_file)
exec(compile(construction,str(repo/'build.py')+'[command-only]','exec'),namespace)
command=namespace['command']
# PyInstaller resolves --add-data against the spec directory. Isolated specs
# therefore require absolute source paths instead of the builder's relatives.
for index, value in enumerate(command):
    if value.startswith(('--add-data=', '--add-binary=')):
        option, pair = value.split('=', 1)
        origin, destination = pair.rsplit(';', 1)
        command[index] = option + '=' + str((repo / origin).resolve()) + ';' + destination
    elif value == 'Tlamatini/manage.py':
        command[index] = str(repo / value)
command += ['--hidden-import=agent.remote_mobile_views','--distpath='+str(out/'dist'),
            '--workpath='+str(out/'work'),'--specpath='+str(out/'spec')]
# pyinstaller_guard owns this exact hermetic directory; verify before its reset.
hermetic=repo/'build/pyinstaller-hermetic-profile'
if not hermetic.resolve().is_relative_to(repo/'build'): raise SystemExit('Unexpected hermetic path')
inputs={str(path.relative_to(repo)):hashlib.sha256(path.read_bytes()).hexdigest()
        for path in [*(repo/'Tlamatini/agent').glob('remote_*.py'), repo/'Tlamatini/agent/services/response_parser.py', repo/'Tlamatini/agent/rag/interface.py']}
(out/'inputs.json').write_text(json.dumps({'edition':repo.name,'version':'1.93.5','python':sys.executable,
    'command':command,'sources':inputs,'started_at':time.time()},indent=2),encoding='utf-8')
with (out/'freeze.log').open('w',encoding='utf-8') as log:
    process=subprocess.Popen(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',errors='replace')
    for line in process.stdout:
        print(line,end='',flush=True);log.write(line);log.flush()
    code=process.wait()
if code: raise SystemExit(code)
dist=out/'dist/manage'
build.verify_frozen_torch_absent(dist)
build.verify_frozen_pool_libraries_absent(dist)
build.verify_frozen_agent_modules(dist)
modules=build._read_pyz_module_names(dist)
for name in ('agent.remote_mobile_views','agent.remote_controller_runtime','agent.remote_controller_views','agent.remote_controller_bridge','agent.rag.interface'):
    if name not in modules: raise SystemExit('Missing frozen module: '+name)
manifest={str(path.relative_to(dist)):hashlib.sha256(path.read_bytes()).hexdigest() for path in dist.rglob('*') if path.is_file()}
(out/'payload-sha256.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
(out/'result.json').write_text(json.dumps({'passed':True,'edition':repo.name,'version':'1.93.5','files':len(manifest),
    'exe':str(dist/'manage.exe'),'finished_at':time.time()},indent=2),encoding='utf-8')
print('FROZEN RC VALIDATION BUILD COMPLETE',repo.name,len(manifest),flush=True)
