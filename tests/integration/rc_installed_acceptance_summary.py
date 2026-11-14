"""Verify completed live evidence and final installed state; never dispatch work."""
import json
from pathlib import Path
import socket
import subprocess
import sys
from urllib.parse import urlparse
from urllib.request import urlopen
import datetime
import hashlib

root = Path('C:/Development/XAIHT/repo/Tlachihuani')
run = Path(sys.argv[1]).resolve()
assert run.parent == (root / 'artifacts/rc-final-acceptance').resolve()
def read(file):
    return json.loads(file.read_text(encoding='utf-8-sig'))

installation = read(run / 'installation.json')
current = read(root / 'artifacts/rc-local-diagnosis/installed-final.json')
assert installation == current and current['installed'] and current['uninstalled_first']
assert current['runtime_files_matched'] == 1475 and current['identities_preserved']
assert current['msi_exit'] == current['uninstall_exit'] == 0
main_installation = read(run / 'main-installation.json')
assert main_installation == read(root / 'artifacts/rc-local-diagnosis/main-installed.json')
assert {item['edition'] for item in main_installation} == {'english', 'spanish'}
assert all(item['protected_data_unchanged'] for item in main_installation)
for item in main_installation:
    built = Path(item['build']).resolve()
    assert built.parent == (root / 'artifacts/main-rc-freeze').resolve()
    frozen = read(built / 'result.json')
    assert frozen['passed'] and frozen['files'] == item['verified_files']
    assert frozen['edition'] == ('Tlamatini' if item['edition'] == 'english' else 'Tlamatini-Spanish')
origins = {edition: str(run) for edition in ('english', 'spanish')}
origin_file = run / 'english-evidence-origin.json'
if origin_file.is_file():
    origin = read(origin_file)
    source_run = Path(origin['source_run']).resolve()
    assert source_run.parent == (root / 'artifacts/rc-final-acceptance').resolve()
    assert read(source_run / 'installation.json') == installation
    source_main = next(item for item in read(source_run / 'main-installation.json') if item['edition'] == 'english')
    assert source_main == next(item for item in main_installation if item['edition'] == 'english')
    required = {'onboarding-results.json', 'prompt-results.json', 'mobile-results.json', 'lifecycle-restart.json', 'lifecycle-stop.json', 'application.json'}
    assert required <= set(origin['files'])
    for name, expected in origin['files'].items():
        assert Path(name).name == name
        for base in (source_run, run):
            assert hashlib.sha256((base / 'english-installed' / name).read_bytes()).hexdigest() == expected
    origins['english'] = str(source_run)
assert read(run / 'complete.json')['complete']
editions = {}
modes = {'multi_turn_enabled', 'exec_report_enabled', 'acpx_enabled', 'ask_execs_enabled', 'step_by_step_enabled', 'self_modify_enabled'}
for edition in ('english', 'spanish'):
    folder = run / (edition + '-installed')
    onboarding = read(folder / 'onboarding-results.json')
    assert onboarding['passed'] == 10 and onboarding['failed'] == 0
    prompts = read(folder / 'prompt-results.json')
    assert prompts['mode'] == 'installed real model'
    assert prompts['planned'] == prompts['completed'] == 50
    assert prompts['passed'] + prompts['failed'] == 50 and prompts['current'] is None
    assert len(prompts['results']) == len({x['id'] for x in prompts['results']}) == 50
    for case in prompts['results']:
        assert case['modesMatch'] and not case['unavailableModes']
        assert case['state'].endswith(' · succeeded') and not case['permission']
        assert modes == set(case['selected'])
        assert all(case['options'][key] == value for key, value in case['selected'].items())
    for mode in modes:
        assert {case['selected'][mode] for case in prompts['results']} == {False, True}
    mobile = read(folder / 'mobile-results.json')
    assert mobile['passed'] == 85 and mobile['failed'] == 0
    lifecycle = {}
    for action in ('restart', 'stop'):
        observed = read(folder / ('lifecycle-' + action + '.json'))
        assert not any(x.get('passed') is False for x in observed['results'])
        receipts = [x for x in observed['results'] if 'operation' in x]
        assert [x['operation'] for x in receipts] == (['stop', 'start'] if action == 'restart' else ['stop'])
        assert all(' · succeeded' in x['receipt'] for x in receipts)
        assert any(x.get('check') == 'actual processes and HTTP listener stopped' and x['passed'] for x in observed['results'])
        lifecycle[action] = observed
    restarted = next(x for x in lifecycle['restart']['results'] if x.get('check') == 'real installed application started via RC')
    assert restarted['passed'] and len(restarted['processes']) == 1
    assert all(restarted['processes'][0]['created_at'] > old['created_at'] for old in restarted['initial_processes'])
    processes = json.loads(subprocess.check_output([sys.executable, str(root / 'tests/integration/rc_installed_processes.py'), edition], text=True))
    assert not processes['processes']
    base = urlparse(read(folder / 'application.json')['base'])
    assert base.hostname == '127.0.0.1'
    with socket.socket() as client:
        # Windows may defer localhost refusal beyond two seconds; a timeout is
        # still inconclusive and must never count as a closed listener.
        client.settimeout(5)
        result = client.connect_ex((base.hostname, base.port))
        assert result in (10061, 111), 'Expected a refused connection after shutdown: ' + str(result)
    editions[edition] = {'real_prompts': 50, 'requests_succeeded': 50,
                         'answer_checks_passed': prompts['passed'], 'answer_checks_failed': prompts['failed'],
                         'answer_failures': [{'id': case['id'], 'expected': case['expected'], 'answer': case['answer']}
                                             for case in prompts['results'] if not case['passed']], 'mobile_checks': 85,
                         'all_six_modes_exercised': True, 'remote_restart': True, 'remote_final_stop': True,
                         'final_process_count': 0, 'http_listener_stopped': True}
with urlopen('http://127.0.0.1:12481/api/rc/v1/health', timeout=5) as response:
    health = json.load(response)
assert health['ready']
summary = {'complete': True, 'at': datetime.datetime.now(datetime.timezone.utc).isoformat(),
           'scope': 'installed local RC; real model prompts and mobile browser checks',
           'build': current['build'], 'uninstalled_first': True, 'runtime_files_matched': 1475,
           'main_runtime_builds': {item['edition']: item['build'] for item in main_installation},
           'evidence_origins': origins,
           'editions': editions, 'real_requests_succeeded': 100,
           'answer_checks_passed': sum(item['answer_checks_passed'] for item in editions.values()),
           'answer_checks_failed': sum(item['answer_checks_failed'] for item in editions.values()),
           'all_answer_checks_passed': all(item['answer_checks_failed'] == 0 for item in editions.values()),
           'mobile_checks_passed': 170, 'rc_ready': True,
           'deferred': ['LAN/firewall', 'physical phone', 'phone microphone capture', 'full external-tool workflows']}
(run / 'summary.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
print(json.dumps(summary, indent=2))
