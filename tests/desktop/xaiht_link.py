"""Headed check: a message sent from the Tlamatini-RC page reaches Tlachihuani.log.

What runs, all on this computer and all visible:
  * The real Tlamatini-RC page and the real /api/rc routes from the web-app
    source, served by ``web-app/scripts/rc-local-server.ts`` on --port (8095) with
    a memory store and one fixed test user (no Cloud SQL, no Google sign-in).
  * The newly built XT (``Tlachihuani.Tray.exe --show``), with
    TLACHIHUANI_USER_DIR pointing at artifacts/xaiht-link/user, so the real
    Tlachihuani.log and xaiht-link.json of this Windows account stay untouched.
  * Headed Chrome drives the page; XT is driven through its own buttons
    (Services…, xaiht.org, Connect…, See log, Exit tray) by window messages.

Checks: the computer is offline before XT connects; Connect saves the key; the
page shows it online; SEND writes the exact JSON to Tlachihuani.log and the page
reports it; See log shows it; after XT exits the page shows it offline; a message
sent while offline waits and arrives when XT starts again.
Ports 8000, 8052, 12064 and 3000 are refused. Shoter takes every photograph.

  python tests\\desktop\\xaiht_link.py
"""

import argparse
import ctypes
import json
import os
import re
import secrets
import shutil
import socket
import subprocess
import sys
import time
import urllib.request
from ctypes import wintypes
from datetime import datetime
from pathlib import Path

import psutil
from playwright.sync_api import sync_playwright

REPO = Path(__file__).resolve().parents[2]
WEB_APP = Path(r"C:\Development\XAIHT\web-app")
TRAY = REPO / "src" / "Tlachihuani.Tray" / "bin" / "Debug" / "net10.0-windows" / "Tlachihuani.Tray.exe"
SHOTER_HARNESS = Path(r"C:\Development\XAIHT\Tlamatini\.claude\skills\tlamatini-daily-chat-test\harness")
REFUSED_PORTS = {8000, 8052, 12064, 3000}
WM_SETTEXT, WM_GETTEXT, WM_GETTEXTLENGTH, BM_CLICK = 0x000C, 0x000D, 0x000E, 0x00F5

u32, k32 = ctypes.windll.user32, ctypes.windll.kernel32
u32.SendMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPCWSTR]
u32.SendMessageW.restype = ctypes.c_ssize_t
u32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
u32.SendMessageTimeoutW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, ctypes.c_void_p,
                                    wintypes.UINT, wintypes.UINT, ctypes.POINTER(ctypes.c_size_t)]
ENUM = ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
results = []


class Stop(Exception):
    """A named reason this run cannot continue."""


def check(name, ok, detail=""):
    results.append({"check": name, "ok": bool(ok), "detail": detail})
    print(("PASS " if ok else "FAIL ") + name + (" -- " + detail if detail else ""), flush=True)
    return ok


def text_of(hwnd):
    buf = ctypes.create_unicode_buffer(4096)
    u32.GetWindowTextW(hwnd, buf, len(buf))
    return buf.value


def control_text(hwnd):
    """A control's text in another process (WM_GETTEXT; GetWindowText cannot read edit boxes there)."""
    length = ctypes.c_size_t()
    if not u32.SendMessageTimeoutW(hwnd, WM_GETTEXTLENGTH, 0, None, 2, 2000, ctypes.byref(length)):
        return ""
    buf = ctypes.create_unicode_buffer(length.value + 2)
    got = ctypes.c_size_t()
    u32.SendMessageTimeoutW(hwnd, WM_GETTEXT, len(buf), ctypes.cast(buf, ctypes.c_void_p), 2, 2000, ctypes.byref(got))
    return buf.value


def class_of(hwnd):
    buf = ctypes.create_unicode_buffer(256)
    u32.GetClassNameW(hwnd, buf, len(buf))
    return buf.value


def pid_of(hwnd):
    pid = wintypes.DWORD()
    u32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    return pid.value


def top_window(title, pid, timeout=20):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        found = []
        u32.EnumWindows(ENUM(lambda h, _: (found.append(h) if u32.IsWindowVisible(h) and pid_of(h) == pid
                                           and text_of(h) == title else None) or True), 0)
        if found:
            return found[0]
        time.sleep(0.2)
    raise Stop(f"XT did not show the window '{title}'.")


def children(hwnd):
    found = []
    u32.EnumChildWindows(hwnd, ENUM(lambda h, _: found.append(h) or True), 0)
    return found


def child(hwnd, text, timeout=10):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        for h in children(hwnd):
            if control_text(h) == text and u32.IsWindowVisible(h):
                return h
        time.sleep(0.2)
    raise Stop(f"Button or field '{text}' not found in '{text_of(hwnd)}'.")


def click(hwnd):
    # Posted, because several buttons open a modal window that would block a send.
    u32.PostMessageW(hwnd, BM_CLICK, 0, 0)


def bring_front(hwnd):
    for _ in range(40):
        if u32.GetForegroundWindow() == hwnd:
            return True
        u32.keybd_event(0x12, 0, 0, 0)
        u32.keybd_event(0x12, 0, 2, 0)
        u32.SetForegroundWindow(hwnd)
        time.sleep(0.1)
    return u32.GetForegroundWindow() == hwnd


def foreground_console(timeout=90):
    """This console must be the window in front (Windows Terminal: by its title marker)."""
    marker = "XT xaiht.org link test " + secrets.token_hex(6)
    k32.SetConsoleTitleW(marker)
    told = False
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        front, console = u32.GetForegroundWindow(), k32.GetConsoleWindow()
        if front and u32.IsWindowVisible(front) and not u32.IsIconic(front):
            if console and u32.IsWindowVisible(console) and u32.GetAncestor(console, 2) == front:
                return True
            if class_of(front) in ("CASCADIA_HOSTING_WINDOW_CLASS", "ConsoleWindowClass") and marker in text_of(front):
                return True
        if not told:
            print("Waiting for this console to be in front. Click inside it to continue.", flush=True)
            told = True
        time.sleep(0.15)
    return False


def chrome_window(page):
    title = page.title()
    found = []
    u32.EnumWindows(ENUM(lambda h, _: (found.append(h) if class_of(h) == "Chrome_WidgetWin_1" and u32.IsWindowVisible(h)
                                       and not u32.IsIconic(h) and text_of(h).startswith(title) else None) or True), 0)
    return found[0] if found else 0


def photo(photos, label, hwnd, what):
    """Shoter photographs the whole desktop, only while the named window is really in front."""
    sys.path.insert(0, str(SHOTER_HARNESS))
    from shoter_shot import take_shot
    if not hwnd or not bring_front(hwnd):
        raise Stop(f"Refusing to photograph {label}: {what} is not in front.")
    time.sleep(0.4)
    path = take_shot(str(photos), label + ".png", str(photos))
    if not path:
        raise Stop("Shoter could not capture " + label)
    check(f"photo {label} shows {what} in front", u32.GetForegroundWindow() == hwnd, path)


def page_photo(page, photos, label):
    page.bring_to_front()
    photo(photos, label, chrome_window(page), "Chrome with the Tlamatini-RC page")


def port_free(port):
    with socket.socket() as s:
        return s.connect_ex(("127.0.0.1", port)) != 0


def stop_tree(proc):
    """Stop a process this test started and the processes it started; never this console."""
    if proc is None or proc.poll() is not None:
        return
    mine = {os.getpid(), *(p.pid for p in psutil.Process().parents())}
    try:
        tree = psutil.Process(proc.pid).children(recursive=True)
    except psutil.NoSuchProcess:
        tree = []
    for p in tree + [psutil.Process(proc.pid)]:
        if p.pid not in mine:
            try:
                p.kill()
            except psutil.NoSuchProcess:
                pass


def start_server(port):
    print("=== Building the Tlamatini-RC page (npx vite build)", flush=True)
    build = subprocess.run("npx vite build", cwd=WEB_APP, shell=True)
    if build.returncode != 0:
        raise Stop("vite build failed.")
    print(f"=== Starting the local check server on port {port}", flush=True)
    proc = subprocess.Popen(f"npx tsx --tsconfig tsconfig.server.json scripts/rc-local-server.ts {port}", cwd=WEB_APP, shell=True)
    deadline = time.monotonic() + 40
    while time.monotonic() < deadline:
        try:
            with urllib.request.urlopen(f"http://127.0.0.1:{port}/api/trpc/ping", timeout=2) as r:
                if r.status == 200:
                    return proc
        except OSError:
            time.sleep(0.3)
    stop_tree(proc)
    raise Stop("The local check server did not start.")


def start_tray(user_dir, show=True):
    for p in psutil.process_iter(["name"]):
        if (p.info["name"] or "").lower() == "tlachihuani.tray.exe":
            raise Stop(f"Another XT is already running (PID {p.pid}); XT allows one per Windows user. Exit it and run again.")
    env = dict(os.environ, TLACHIHUANI_USER_DIR=str(user_dir))
    return subprocess.Popen([str(TRAY)] + (["--show"] if show else []), env=env)


def exit_tray(tray):
    status = top_window("Tlachihuani", tray.pid)
    click(child(status, "Exit tray"))
    try:
        tray.wait(20)
    except subprocess.TimeoutExpired:
        raise Stop("XT did not exit after Exit tray.")


def log_entries(user_dir):
    """Every 'Message #n received' entry with its indented JSON block."""
    path = user_dir / "Tlachihuani.log"
    if not path.exists():
        return {}
    entries, current, block = {}, None, []
    for line in path.read_text(encoding="utf-8").splitlines() + [""]:
        if line.startswith("    ") and current is not None:
            block.append(line[4:])
            continue
        if current is not None:
            entries.setdefault(current, []).append("\n".join(block))
            current, block = None, []
        match = re.search(r"  INFO   xaiht\.org  Message #(\d+) received from xaiht\.org$", line)
        if match:
            current = int(match.group(1))
    return entries


def wait_status(page, message_id, status, timeout):
    page.wait_for_selector(f'[data-testid="rc-message-{message_id}"][data-status="{status}"]', timeout=timeout * 1000)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8095)
    args = parser.parse_args()
    if args.port in REFUSED_PORTS:
        print(f"Refusing port {args.port}: it belongs to a running Tlamatini, Jobs or the dev server.")
        return 2
    if not foreground_console():
        print("STOP: this console never came to the front, so the run would not be visible.")
        return 3
    if not TRAY.exists():
        print(f"STOP: build XT first ({TRAY}).")
        return 4
    if not port_free(args.port):
        print(f"STOP: port {args.port} is already in use.")
        return 5

    out = REPO / "artifacts" / "xaiht-link"
    photos, user_dir = out / "photos", out / "user"
    shutil.rmtree(out, ignore_errors=True)
    photos.mkdir(parents=True)
    user_dir.mkdir(parents=True)
    server = tray = None
    try:
        server = start_server(args.port)
        with sync_playwright() as pw:
            browser = pw.chromium.launch(channel="chrome", headless=False, args=["--start-maximized"])
            page = browser.new_context(no_viewport=True).new_page()
            page.goto(f"http://127.0.0.1:{args.port}/tlamatini-rc?lang=en")
            page.wait_for_selector('[data-testid="rc-page"]', timeout=20000)
            check("the signed-in menu has Tlamatini-RC", page.locator("nav a", has_text="Tlamatini-RC").first.is_visible())

            page.fill("#rc-new-name", "Angela laptop (local check)")
            page.click("text=Create key")
            page.wait_for_selector('[data-testid="rc-new-key-value"]', timeout=10000)
            key = page.inner_text('[data-testid="rc-new-key-value"]').strip()
            check("xaiht.org created a computer key", re.fullmatch(r"xrc_[A-Za-z0-9_-]{43}", key) is not None, key[:8] + "…")
            computer = page.locator('[data-testid^="rc-computer-"]').first
            check("before XT connects the computer is not online", computer.get_attribute("data-online") == "false",
                  computer.locator('[data-testid="rc-online-label"]').inner_text())
            page_photo(page, photos, "01-key-created")

            tray = start_tray(user_dir)
            status = top_window("Tlachihuani", tray.pid)
            click(child(status, "Services…"))
            services = top_window("Tlachihuani · Services", tray.pid)
            click(child(services, "xaiht.org"))
            time.sleep(0.5)
            click(child(services, "Connect…"))
            dialog = top_window("Tlachihuani · xaiht.org link", tray.pid)
            edits = [h for h in children(dialog) if "EDIT" in class_of(h).upper()]
            website = next(h for h in edits if control_text(h).startswith("http"))
            key_box = next(h for h in edits if h != website)
            u32.SendMessageW(website, WM_SETTEXT, 0, f"http://127.0.0.1:{args.port}")
            u32.SendMessageW(key_box, WM_SETTEXT, 0, key)
            photo(photos, "02-xt-connect-dialog", dialog, "XT's Connect to xaiht.org dialog")
            click(child(dialog, "Connect"))
            deadline = time.monotonic() + 10
            while (user_dir / "xaiht-link.json").exists() is False and time.monotonic() < deadline:
                time.sleep(0.2)
            saved = (user_dir / "xaiht-link.json").read_text(encoding="utf-8") if (user_dir / "xaiht-link.json").exists() else ""
            check("Connect saved the key protected (not in plain text)", saved != "" and key not in saved)

            page.wait_for_selector('[data-testid^="rc-computer-"][data-online="true"]', timeout=30000)
            check("xaiht.org shows the computer online once XT asks", True,
                  computer.locator('[data-testid="rc-online-label"]').inner_text())
            time.sleep(4.5)   # the Services panel repaints its xaiht.org status every 4 seconds
            check("XT's panel says Online", any(control_text(h).startswith("Online") for h in children(services)))
            photo(photos, "03-xt-panel-online", services, "XT's Services panel on the xaiht.org tab")
            page_photo(page, photos, "04-page-online")

            text = "Hola Tlachihuani — primer mensaje desde xaiht.org (ñ, ü, 😀)"
            page.select_option("#rc-target", "tlachihuani")
            page.select_option("#rc-type", "text")
            page.select_option("#rc-level", "warning")
            page.fill("#rc-subject", "First message")
            page.select_option("#rc-ttl", "60")
            page.fill("#rc-message", text)
            preview = json.loads(page.inner_text('[data-testid="rc-preview"]'))
            check("the preview is the JSON that will be sent", preview["message"] == text and preview["level"] == "warning")
            sent_at = time.monotonic()
            page.click('[data-testid="rc-send"]')
            page.wait_for_selector('[data-testid^="rc-message-"]', timeout=10000)
            first_id = int(page.locator('[data-testid^="rc-message-"]').first.get_attribute("data-testid").rsplit("-", 1)[1])
            wait_status(page, first_id, "acknowledged", 15)
            elapsed = time.monotonic() - sent_at
            check("SEND reached Tlachihuani.log and the page says so", True, f"{elapsed:.1f} s from SEND to receipt")

            entries = log_entries(user_dir)
            blocks = entries.get(first_id, [])
            logged = json.loads(blocks[0]) if blocks else {}
            check("Tlachihuani.log holds the exact JSON once, characters as typed", len(blocks) == 1 and text in blocks[0]
                  and logged.get("message") == text
                  and logged.get("subject") == "First message" and logged.get("level") == "warning"
                  and logged.get("target") == "tlachihuani" and logged.get("id") == first_id
                  and logged.get("computer", {}).get("name") == "Angela laptop (local check)"
                  and logged.get("schema") == "xaiht.tlamatini-rc.message/1", json.dumps(logged, ensure_ascii=False)[:160])
            page_photo(page, photos, "05-page-written-to-log")

            click(child(services, "See log"))
            viewer = top_window("Tlachihuani · Log", tray.pid)
            time.sleep(2)
            shown = "".join(control_text(h) for h in children(viewer))
            check("See log shows the received message", f"Message #{first_id} received" in shown and text in shown)
            photo(photos, "06-xt-see-log", viewer, "XT's Tlachihuani.log viewer")
            click(child(viewer, "Close"))
            time.sleep(0.5)
            click(child(services, "Close"))
            time.sleep(0.5)

            exit_tray(tray)
            tray = None
            went = time.monotonic()
            page.wait_for_selector('[data-testid^="rc-computer-"][data-online="false"]', timeout=90000)
            check("after XT exits the page shows the computer offline", True, f"{time.monotonic() - went:.0f} s after exit")
            offline_text = "Second message, sent while the computer was offline"
            page.fill("#rc-subject", "")
            page.select_option("#rc-level", "info")
            page.fill("#rc-message", offline_text)
            page.click('[data-testid="rc-send"]')
            time.sleep(2.5)
            second_id = int(page.locator('[data-testid^="rc-message-"]').first.get_attribute("data-testid").rsplit("-", 1)[1])
            wait_status(page, second_id, "queued", 10)
            check("a message sent while offline waits for the computer", True, f"message #{second_id}")
            page_photo(page, photos, "07-page-offline-waiting")

            tray = start_tray(user_dir)
            wait_status(page, second_id, "acknowledged", 40)
            entries = log_entries(user_dir)
            second = json.loads(entries[second_id][0]) if entries.get(second_id) else {}
            check("when XT starts again the waiting message is written to the log", second.get("message") == offline_text
                  and len(entries.get(second_id, [])) == 1 and len(entries.get(first_id, [])) == 1)
            page_photo(page, photos, "08-page-delivered-after-restart")
            exit_tray(tray)
            tray = None
            browser.close()
    except Stop as stop:
        check("run finished", False, str(stop))
    except Exception as error:   # noqa: BLE001 - every failure must be reported, with its kind
        check("run finished", False, f"{type(error).__name__}: {error}")
    finally:
        if tray is not None:
            try:
                exit_tray(tray)
            except Stop:
                stop_tree(tray)
        stop_tree(server)
    passed = sum(1 for r in results if r["ok"])
    (out / "results.json").write_text(json.dumps({"when": datetime.now().isoformat(timespec="seconds"), "results": results},
                                                 ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\nRESULT {passed}/{len(results)} passed. Photos and results: {out}", flush=True)
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    sys.exit(main())
