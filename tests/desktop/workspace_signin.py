"""Headed check: XT "Open workspace" always passes through the Tlamatini login page.

The Services panel's REST workspace button opens ``127.0.0.1:12064/?signin=1``.
The Jobs launcher page then sends the browser to that edition's login page
(``/?next=/agent/torrenter/workspace/``) instead of straight into the
workspace, so the queue shown belongs only to the account that just signed in,
even when this browser is already signed in with another account.

Isolation (nothing here touches the installed Tlamatini or Jobs):
  * Tlamatini runs in-process from a source tree on --port (default 8091) with
    a throwaway test database and two throwaway accounts.
  * Its Jobs calls are replaced by a recorder, so no test account ever reaches
    the installed Jobs service. The recorder proves which account every Jobs
    request was made for.
  * The newly built Jobs web facade (``Tlachihuani.Jobs.exe --web``) serves the
    real launcher page on --launcher-port (default 12091). The browser's
    ``/controllers`` answer is pointed at the test Tlamatini, so the installed
    editions are never probed.
Ports 8000, 8052 and 12064 are refused.

Run from a visible foreground console, for example:
  python tests\\desktop\\workspace_signin.py
  python tests\\desktop\\workspace_signin.py --tlamatini C:\\Development\\XAIHT\\Tlamatini-Spanish\\Tlamatini --port 8092
"""

import argparse
import ctypes
import json
import os
import secrets
import socket
import subprocess
import sys
import threading
import time
import urllib.request
from datetime import datetime
from pathlib import Path

from playwright.sync_api import sync_playwright

REPO = Path(__file__).resolve().parents[2]
SHOTER_HARNESS = Path(r"C:\Development\XAIHT\Tlamatini\.claude\skills\tlamatini-daily-chat-test\harness")
LIVE_PORTS = {8000, 8052, 12064}
WORKSPACE_PATH = "/agent/torrenter/workspace/"
LOGIN_PATH = "/?next=%2Fagent%2Ftorrenter%2Fworkspace%2F"

results = []


def check(name, ok, detail=""):
    results.append({"check": name, "ok": bool(ok), "detail": detail})
    print(("PASS " if ok else "FAIL ") + name + (" -- " + detail if detail else ""), flush=True)
    return ok


def port_free(port):
    with socket.socket() as s:
        return s.connect_ex(("127.0.0.1", port)) != 0


def window_text(hwnd):
    buf = ctypes.create_unicode_buffer(2048)
    ctypes.windll.user32.GetWindowTextW(hwnd, buf, len(buf))
    return buf.value


def window_class(hwnd):
    buf = ctypes.create_unicode_buffer(256)
    ctypes.windll.user32.GetClassNameW(hwnd, buf, len(buf))
    return buf.value


def foreground_console(timeout=90):
    """Same rule as scripts/Assert-Visible.ps1, including a Windows Terminal tab.

    ConPTY's GetConsoleWindow() is a hidden helper, so a Terminal in front only
    counts when its title carries the marker this console just wrote.
    """
    k32, u32 = ctypes.windll.kernel32, ctypes.windll.user32
    marker = "XT sign-in test " + secrets.token_hex(6)
    k32.SetConsoleTitleW(marker)
    told = False
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        front, console = u32.GetForegroundWindow(), k32.GetConsoleWindow()
        if front and u32.IsWindowVisible(front) and not u32.IsIconic(front):
            if console and u32.IsWindowVisible(console) and u32.GetAncestor(console, 2) == front:
                return True
            if window_class(front) in ("CASCADIA_HOSTING_WINDOW_CLASS", "ConsoleWindowClass") \
                    and marker in window_text(front):
                return True
        if not told:
            print("Waiting for this console to be in front. Click inside it to continue.", flush=True)
            told = True
        time.sleep(0.15)
    return False


def chrome_in_front(page):
    """The foreground window is visible Chrome showing this page's title."""
    u32 = ctypes.windll.user32
    title = page.title()

    def ours(hwnd):
        return (window_class(hwnd) == "Chrome_WidgetWin_1" and u32.IsWindowVisible(hwnd)
                and not u32.IsIconic(hwnd) and window_text(hwnd).startswith(title))

    for attempt in range(50):
        page.bring_to_front()
        hwnd = u32.GetForegroundWindow()
        if ours(hwnd):
            return hwnd
        if attempt % 10 == 9:
            # Windows only lets the active input owner switch windows; a lone
            # ALT press grants that right, then the verified Chrome window is raised.
            found = []
            enum = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
            u32.EnumWindows(enum(lambda h, _: (found.append(h) if ours(h) else None) or True), 0)
            if found:
                u32.keybd_event(0x12, 0, 0, 0)
                u32.keybd_event(0x12, 0, 2, 0)
                u32.SetForegroundWindow(found[0])
        time.sleep(0.1)
    return 0


def photo(page, photos, label):
    """Shoter photographs the whole desktop, only while this page is really in front."""
    sys.path.insert(0, str(SHOTER_HARNESS))
    from shoter_shot import take_shot
    hwnd = chrome_in_front(page)
    if not hwnd:
        raise RuntimeError("Refusing to photograph: Chrome with '" + page.title() + "' is not in front.")
    path = take_shot(str(photos), label + ".png", str(photos))
    if not path:
        raise RuntimeError("Shoter could not capture " + label)
    still = ctypes.windll.user32.GetForegroundWindow() == hwnd
    check("photo " + label + " shows the page in front", still, path)


def start_launcher(exe, port):
    proc = subprocess.Popen([str(exe), "--web", str(port)], stdin=subprocess.PIPE,
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    proc.stdin.write(b"START\n")
    proc.stdin.flush()
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        try:
            with urllib.request.urlopen(f"http://127.0.0.1:{port}/open-workspace.js", timeout=2) as r:
                if b"signin" in r.read():
                    return proc
                raise RuntimeError("The launcher serves an open-workspace.js without the sign-in change.")
        except OSError:
            time.sleep(0.2)
    proc.kill()
    raise RuntimeError("The Jobs launcher did not start on port %d." % port)


def start_tlamatini(source, port, work):
    sys.path.insert(0, str(source))
    os.environ["DJANGO_SETTINGS_MODULE"] = "tlamatini.settings"
    os.environ["TLAMATINI_NO_AUDIO"] = "1"
    import django
    django.setup()
    from django.db import connections
    from django.test.runner import DiscoverRunner
    from django.test.testcases import LiveServerThread
    from django.contrib.staticfiles.handlers import StaticFilesHandler

    connections["default"].settings_dict["TEST"]["NAME"] = str(work / "signin_test.sqlite3")
    runner = DiscoverRunner(interactive=False, verbosity=0)
    print("Creating the throwaway test database (migrations take a moment)...", flush=True)
    old = runner.setup_databases()

    from agent import torrenter_views
    calls, lock = [], threading.Lock()

    def fake_session(profile):
        return "xt-signin-test:" + profile

    def fake_request(method, path, body=None, *, bearer=None, key=None, revision=None):
        with lock:
            calls.append({"t": time.time(), "method": method, "route": path,
                          "profile": (bearer or "").split(":", 1)[-1]})
        if path.startswith("torrents"):
            return 200, {"items": [], "next_cursor": None}
        return 200, {}

    torrenter_views.client.session = fake_session
    torrenter_views.client.request = fake_request

    # Tlamatini selects asyncio's selector loop on Windows; Playwright must start
    # Chrome as a subprocess, which only the default proactor loop supports.
    import asyncio
    asyncio.set_event_loop_policy(asyncio.WindowsProactorEventLoopPolicy())

    server = LiveServerThread("127.0.0.1", StaticFilesHandler, connections_override={}, port=port)
    server.daemon = True
    server.start()
    server.is_ready.wait()
    if server.error:
        raise server.error
    return runner, old, server, calls, lock


def make_user(name):
    from django.contrib.auth import get_user_model
    password = secrets.token_urlsafe(18)
    get_user_model().objects.create_user(username=name, password=password)
    return password


def profile_of(name):
    from django.contrib.auth import get_user_model
    from agent.torrenter_bridge import profile_for
    return profile_for(get_user_model().objects.get(username=name).pk)


def sign_in(page, username, password):
    page.locator("#id_username").fill(username)
    page.locator("#id_password").fill(password)
    page.locator("form button[type=submit]").click()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--tlamatini", type=Path, default=Path(r"C:\Development\XAIHT\Tlamatini\Tlamatini"))
    parser.add_argument("--jobs-exe", type=Path,
                        default=REPO / r"src\Tlachihuani.Jobs\bin\Debug\net10.0-windows\Tlachihuani.Jobs.exe")
    parser.add_argument("--port", type=int, default=8091)
    parser.add_argument("--launcher-port", type=int, default=12091)
    args = parser.parse_args()
    if not foreground_console():
        parser.error("Run from a visible, foreground development console on the interactive desktop.")
    for port in (args.port, args.launcher_port):
        if port in LIVE_PORTS:
            parser.error("Port %d belongs to the running Tlamatini/Jobs. Choose another port." % port)
        if not port_free(port):
            parser.error("Port %d is already in use." % port)
    edition = "Tlamatini-Spanish" if "Spanish" in str(args.tlamatini) else "Tlamatini"
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    work = REPO / "artifacts" / "workspace-signin" / (stamp + "-" + edition)
    work.mkdir(parents=True, exist_ok=True)
    base = f"http://127.0.0.1:{args.port}"
    launcher = f"http://127.0.0.1:{args.launcher_port}"
    print(f"Edition {edition}  source {args.tlamatini}", flush=True)
    print(f"Test Tlamatini {base}   test launcher {launcher}   photos {work}", flush=True)

    jobs = start_launcher(args.jobs_exe, args.launcher_port)
    check("Built Jobs launcher serves the sign-in launcher page", True, args.jobs_exe.name)
    runner, old_db, server, calls, lock = start_tlamatini(args.tlamatini, args.port, work)
    try:
        alice, bob = "xt-test-alice", "xt-test-bob"
        alice_pw, bob_pw = make_user(alice), make_user(bob)
        alice_profile, bob_profile = profile_of(alice), profile_of(bob)

        def route_controllers(page, apps):
            body = json.dumps(apps)
            page.route(launcher + "/controllers",
                       lambda route: route.fulfill(status=200, content_type="application/json", body=body))

        one_app = [{"name": edition, "url": base + WORKSPACE_PATH}]
        with sync_playwright() as pw:
            browser = pw.chromium.launch(channel="chrome", headless=False, args=["--start-maximized"])
            context = browser.new_context(no_viewport=True)

            # 1. Bob is already signed in to this browser and has his workspace open.
            bob_tab = context.new_page()
            bob_tab.goto(base + LOGIN_PATH)
            sign_in(bob_tab, bob, bob_pw)
            bob_tab.wait_for_url(base + WORKSPACE_PATH, timeout=20000)
            bob_tab.locator("#workspace-owner").filter(has_text=bob).wait_for(timeout=15000)
            check("1. Another account (bob) is signed in with his workspace open", True)
            photo(bob_tab, work, "01-bob-already-signed-in")

            # 2. XT "Open workspace" (?signin=1) must show the login page, not bob's queue.
            tab = context.new_page()
            route_controllers(tab, one_app)
            tab.goto(launcher + "/?signin=1")
            tab.wait_for_url(base + LOGIN_PATH, timeout=15000)
            tab.locator("#id_username").wait_for(timeout=10000)
            check("2. Open workspace lands on the login page even though bob is signed in",
                  tab.url == base + LOGIN_PATH, tab.url)
            still_bob = tab.request.get(base + WORKSPACE_PATH + "session/").json().get("username")
            check("2b. The login page is shown while the browser session is still bob's", still_bob == bob, str(still_bob))
            photo(tab, work, "02-forced-login-page")

            # 3. A wrong password keeps the login page; no workspace is shown.
            sign_in(tab, alice, alice_pw + "-wrong")
            tab.locator("#id_username").wait_for(timeout=10000)
            tab.wait_for_timeout(800)
            check("3. A wrong password stays on the login page",
                  tab.url.startswith(base + "/") and WORKSPACE_PATH not in tab.url.split("?")[0], tab.url)

            # 4. Alice signs in -> her workspace, and every Jobs request is hers.
            sign_in(tab, alice, alice_pw)
            tab.wait_for_url(base + WORKSPACE_PATH, timeout=20000)
            tab.locator("#workspace-owner").filter(has_text=alice).wait_for(timeout=15000)
            # Counted from here: any request bob's tab began before the sign-in has finished.
            signed_in_at = time.time()
            owner = tab.locator("#workspace-owner").inner_text()
            check("4. After sign-in the workspace belongs to alice", owner == alice, "owner=" + owner)
            tab.wait_for_timeout(5000)  # at least two of the workspace's 2-second refreshes
            with lock:
                after = [c for c in calls if c["t"] >= signed_in_at]
            check("4b. The workspace reached Jobs only for alice after her sign-in",
                  bool(after) and all(c["profile"] == alice_profile for c in after),
                  "%d Jobs requests, profiles=%s" % (len(after), sorted({c["profile"][:8] for c in after})))
            check("4c. No Jobs request was made for bob after alice signed in",
                  not any(c["profile"] == bob_profile for c in after))
            photo(tab, work, "03-alice-workspace")

            # 5. Bob's older tab must not keep showing (or mixing) his queue.
            bob_tab.bring_to_front()
            bob_tab.locator("#connection").filter(has_text="Session changed").wait_for(timeout=10000)
            hidden = bob_tab.locator("#workspace").is_hidden()
            check("5. Bob's old tab reports the account change and hides the queue", hidden,
                  bob_tab.locator("#connection").inner_text())
            photo(bob_tab, work, "04-bob-tab-reports-change")

            # 6. Forced even when the same account is already signed in.
            again = context.new_page()
            route_controllers(again, one_app)
            again.goto(launcher + "/?signin=1")
            again.wait_for_url(base + LOGIN_PATH, timeout=15000)
            check("6. Open workspace asks again even when alice is already signed in",
                  again.locator("#id_username").is_visible(), again.url)
            again.close()

            # 7. "Open downloads" (no ?signin) keeps the current login, as before.
            downloads = context.new_page()
            route_controllers(downloads, one_app)
            downloads.goto(launcher + "/")
            downloads.wait_for_url(base + WORKSPACE_PATH, timeout=15000)
            downloads.locator("#workspace-owner").filter(has_text=alice).wait_for(timeout=15000)
            check("7. Open downloads still goes straight to the current account's workspace", True, downloads.url)
            photo(downloads, work, "05-open-downloads-keeps-login")
            downloads.close()

            # 8. Two editions: the chooser's links are sign-in links too.
            two = one_app + [{"name": "Tlamatini-Spanish", "url": "http://127.0.0.1:8099" + WORKSPACE_PATH}]
            chooser = context.new_page()
            route_controllers(chooser, two)
            chooser.goto(launcher + "/?signin=1")
            chooser.locator("#apps a").nth(1).wait_for(timeout=10000)
            hrefs = chooser.locator("#apps a").evaluate_all("links => links.map(a => a.href)")
            expected = [base + LOGIN_PATH, "http://127.0.0.1:8099" + LOGIN_PATH]
            check("8. With two editions every choice opens its login page", hrefs == expected, json.dumps(hrefs))
            check("8b. The chooser stays on the launcher (no silent redirect)", chooser.url.startswith(launcher), chooser.url)
            photo(chooser, work, "06-chooser-sign-in-links")
            chooser.goto(launcher + "/")
            chooser.locator("#apps a").nth(1).wait_for(timeout=10000)
            plain = chooser.locator("#apps a").evaluate_all("links => links.map(a => a.href)")
            check("8c. Without ?signin the chooser keeps the direct workspace links",
                  plain == [a["url"] for a in two], json.dumps(plain))
            chooser.close()

            browser.close()
    finally:
        server.terminate()
        jobs.kill()
        try:
            from django.db import connections
            connections.close_all()
            runner.teardown_databases(old_db)
        except Exception as exc:  # the verdict must not be hidden by a locked test file
            print("WARNING: could not remove the throwaway test database: %s" % exc, flush=True)

    failed = [r for r in results if not r["ok"]]
    (work / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
    print("", flush=True)
    print("RESULT: %d passed, %d failed  (%s)" % (len(results) - len(failed), len(failed), edition), flush=True)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
