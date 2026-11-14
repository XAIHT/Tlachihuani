"""Visible installed-runtime boot/cleanup checks; not RC lifecycle or prompt acceptance."""
import argparse
import ctypes
from ctypes import wintypes
import http.cookiejar
import json
import os
from pathlib import Path
import re
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('edition', choices=['english', 'spanish'])
    parser.add_argument('--stop-helper', required=True, type=Path)
    parser.add_argument('--serve-ready', type=Path, help='Keep this owned app alive for headed UI checks until stop.ready appears.')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    helper = args.stop_helper.resolve()
    helper.relative_to(root / 'artifacts' / 'release-builds')
    assert helper.name == 'Tlachihuani.Tray.exe' and helper.is_file()
    install = Path('C:/Tlamatini' if args.edition == 'english' else 'C:/Tlamatini-Spanish')
    image = install / ('Tlamatini.exe' if args.edition == 'english' else 'Tlamatini-Spanish.exe')
    config = json.loads((install / 'config.json').read_text(encoding='utf-8-sig'))
    base = 'http://127.0.0.1:' + str(int(config.get('django_port', 8000)))
    output = root / 'artifacts' / 'rc-local-diagnosis' / ('frozen-boot-' + args.edition + '-' + str(int(time.time())))
    output.mkdir(parents=True)
    import psutil
    existing = [p for p in psutil.process_iter(['exe']) if p.info['exe'] and Path(p.info['exe']) == image]
    assert not existing, 'Independent boot requires the installed edition to be stopped.'
    results = []

    def check(name, condition):
        results.append({'name': name, 'passed': bool(condition)})
        print(('PASS ' if condition else 'FAIL ') + args.edition + ' ' + name, flush=True)
        assert condition, name

    get_times = ctypes.WinDLL('kernel32', use_last_error=True).GetProcessTimes
    get_times.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(wintypes.FILETIME)] * 4
    get_times.restype = wintypes.BOOL
    env = dict(os.environ, TEMP=str(install / 'Temp'), TMP=str(install / 'Temp'), TLAMATINI_TEMP=str(install / 'Temp'))
    child = subprocess.Popen([str(image)], cwd=install, env=env, creationflags=subprocess.CREATE_NEW_CONSOLE)
    created, ended, kernel, user = (wintypes.FILETIME() for _ in range(4))
    assert get_times(int(child._handle), ctypes.byref(created), ctypes.byref(ended), ctypes.byref(kernel), ctypes.byref(user))
    creation = (created.dwHighDateTime << 32) | created.dwLowDateTime
    jar = http.cookiejar.CookieJar()
    client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    error = None
    try:
        print('Starting the installed frozen application; waiting for normal HTTP startup.', flush=True)
        deadline = time.monotonic() + 150
        observed = 0
        while time.monotonic() < deadline:
            assert child.poll() is None, 'Installed application exited before HTTP startup.'
            try:
                response = client.open(base + '/', timeout=3)
                login = response.read().decode('utf-8')
                if response.status == 200 and 'id_username' in login:
                    break
            except (OSError, urllib.error.URLError):
                pass
            if time.monotonic() - observed >= 10:
                print('Waiting for real installed login page...', flush=True)
                observed = time.monotonic()
            time.sleep(1)
        else:
            raise AssertionError('Installed application did not serve its normal login page.')
        check('fresh frozen runtime boots to normal login', True)
        csrf = re.search(r'name="csrfmiddlewaretoken" value="([^"]+)"', login).group(1)
        payload = urllib.parse.urlencode({'username': os.environ.get('TLAMATINI_USER', 'user'),
                                         'password': os.environ.get('TLAMATINI_PASSWORD', 'changeme'),
                                         'next': '/agent/agent/',
                                         'csrfmiddlewaretoken': csrf}).encode()
        request = urllib.request.Request(base + '/', payload, {'Referer': base + '/', 'Content-Type': 'application/x-www-form-urlencoded'})
        response = client.open(request, timeout=30)
        body = response.read().decode('utf-8')
        check('normal account login reaches real chat', 'id="chat-message-input"' in body and 'id_password' not in body)
        response = client.open(base + '/agent/remote-controller/session/', timeout=10)
        identity = json.load(response)
        check('frozen mobile identity endpoint matches the edition', identity.get('authenticated') is True and identity.get('edition') == args.edition)
        if identity.get('enabled'):
            response = client.open(base + '/agent/mobile/files/', timeout=10)
            listing = json.load(response)
            check('frozen mobile file endpoint lists permitted locations', isinstance(listing.get('entries'), list) and bool(listing.get('roots')))
        else:
            try:
                client.open(base + '/agent/mobile/files/', timeout=10)
            except urllib.error.HTTPError as denied:
                check('inactive RC profile refuses mobile file access', denied.code == 403)
            else:
                raise AssertionError('An inactive RC profile unexpectedly listed files.')
        if args.serve_ready:
            ready = args.serve_ready.resolve()
            ready.relative_to(root / 'artifacts')
            ready.mkdir(parents=True, exist_ok=True)
            (ready / 'ready.json').write_text(json.dumps({'base': base, 'identity': identity}), encoding='utf-8')
            limit = time.monotonic() + 1800
            while not (ready / 'stop.ready').exists() and time.monotonic() < limit:
                assert child.poll() is None, 'Owned installed app closed during mobile checks.'
                time.sleep(1)
    except Exception as exc:
        error = str(exc)
        print('BOOT CHECK FAILED: ' + error, flush=True)
    finally:
        print('Closing only the owned installed console through the verified cleanup helper.', flush=True)
        cleanup = subprocess.run([str(helper), '--rc-stop-console', args.edition, str(child.pid), str(creation)],
                                 creationflags=subprocess.CREATE_NO_WINDOW, timeout=45, check=False)
        results.append({'name': 'owned console cleanup helper', 'passed': cleanup.returncode == 0, 'exit': cleanup.returncode})
        if cleanup.returncode == 0:
            child.wait(timeout=10)
            left = [p for p in psutil.process_iter(['exe']) if p.info['exe'] and Path(p.info['exe']) == image]
            results.append({'name': 'all installed edition processes exited', 'passed': not left})
        record = {'edition': args.edition, 'kind': 'installed HTTP startup and direct owned cleanup; not RC lifecycle or model prompts',
                  'image': str(image), 'results': results, 'error': error}
        (output / 'results.json').write_text(json.dumps(record, indent=2), encoding='utf-8')
        print('Evidence: ' + str(output), flush=True)
    return 1 if error or any(not item['passed'] for item in results) else 0


if __name__ == '__main__':
    raise SystemExit(main())
