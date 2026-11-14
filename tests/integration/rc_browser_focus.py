# Tlachihuani — Created for Angela López Mendoza · XAIHT
"""Restore and verify one owned QA Chrome window on the real Windows desktop.

This harness never starts a browser, creates a console, executes page code, or
substitutes emulated focus for a real foreground window. The visible parent
passes its owned process identity; subsequent calls pin the process creation
time. Any ambiguity or failed foreground restoration stops the browser check.
"""
import argparse
import ctypes
from ctypes import wintypes
import json
import ntpath
import os
import sys
import time


CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
FILETIME_EPOCH = 116444736000000000


class FocusError(RuntimeError):
    def __init__(self, message, windows):
        super().__init__(message)
        # Only windows belonging to the already-verified QA Chrome process.
        self.windows = windows[:64]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", required=True, type=int)
    parser.add_argument("--started-after-ms", required=True, type=int)
    parser.add_argument("--creation-time", default="")
    parser.add_argument("--window", type=int, default=0)
    parser.add_argument("--title", default="")
    parser.add_argument("--verify-only", action="store_true")
    parser.add_argument("--visible-development", action="store_true")
    parser.add_argument("--content-x", type=float)
    parser.add_argument("--content-y", type=float)
    args = parser.parse_args()
    if os.name != "nt" or args.pid <= 0 or args.window < 0 or not args.visible_development:
        parser.error("Use an owned Chrome process from the verified visible development console")

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    if args.content_x is not None:
        user32.SetProcessDPIAware()
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    dwmapi = ctypes.WinDLL("dwmapi", use_last_error=True)
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user32.EnumWindows.argtypes = [callback_type, wintypes.LPARAM]
    user32.EnumWindows.restype = wintypes.BOOL
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetWindow.argtypes = [wintypes.HWND, wintypes.UINT]
    user32.GetWindow.restype = wintypes.HWND
    user32.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]
    user32.GetAncestor.restype = wintypes.HWND
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.IsIconic.argtypes = [wintypes.HWND]
    user32.IsIconic.restype = wintypes.BOOL
    user32.GetForegroundWindow.restype = wintypes.HWND
    user32.ShowWindowAsync.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.ShowWindowAsync.restype = wintypes.BOOL
    user32.SetForegroundWindow.argtypes = [wintypes.HWND]
    user32.SetForegroundWindow.restype = wintypes.BOOL
    user32.BringWindowToTop.argtypes = [wintypes.HWND]
    user32.BringWindowToTop.restype = wintypes.BOOL
    user32.SwitchToThisWindow.argtypes = [wintypes.HWND, wintypes.BOOL]
    user32.SwitchToThisWindow.restype = None
    user32.AttachThreadInput.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.BOOL]
    user32.AttachThreadInput.restype = wintypes.BOOL
    user32.keybd_event.argtypes = [wintypes.BYTE, wintypes.BYTE, wintypes.DWORD, ctypes.c_size_t]
    user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.GetWindowRect.restype = wintypes.BOOL
    class WindowPlacement(ctypes.Structure):
        _fields_ = [("length", wintypes.UINT), ("flags", wintypes.UINT), ("showCmd", wintypes.UINT),
                    ("ptMinPosition", wintypes.POINT), ("ptMaxPosition", wintypes.POINT),
                    ("rcNormalPosition", wintypes.RECT)]
    user32.GetWindowPlacement.argtypes = [wintypes.HWND, ctypes.POINTER(WindowPlacement)]
    user32.GetWindowPlacement.restype = wintypes.BOOL
    kernel32.GetCurrentThreadId.restype = wintypes.DWORD
    kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.OpenProcess.restype = wintypes.HANDLE
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
    kernel32.QueryFullProcessImageNameW.restype = wintypes.BOOL
    kernel32.GetProcessTimes.argtypes = [wintypes.HANDLE, *([ctypes.POINTER(wintypes.FILETIME)] * 4)]
    kernel32.GetProcessTimes.restype = wintypes.BOOL
    kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
    kernel32.GetExitCodeProcess.restype = wintypes.BOOL
    dwmapi.DwmGetWindowAttribute.argtypes = [wintypes.HWND, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD]

    handle = kernel32.OpenProcess(0x1000, False, args.pid)  # PROCESS_QUERY_LIMITED_INFORMATION
    if not handle:
        raise RuntimeError("Owned Chrome process is not available")
    try:
        executable = ctypes.create_unicode_buffer(32768)
        size = wintypes.DWORD(len(executable))
        if not kernel32.QueryFullProcessImageNameW(handle, 0, executable, ctypes.byref(size)):
            raise ctypes.WinError(ctypes.get_last_error())
        if ntpath.normcase(executable.value) != ntpath.normcase(CHROME):
            raise RuntimeError("Owned PID is not the fixed QA Chrome executable")
        times = [wintypes.FILETIME() for _ in range(4)]
        if not kernel32.GetProcessTimes(handle, *(ctypes.byref(value) for value in times)):
            raise ctypes.WinError(ctypes.get_last_error())
        created = (times[0].dwHighDateTime << 32) | times[0].dwLowDateTime
        created_ms = (created - FILETIME_EPOCH) // 10000
        if args.creation_time:
            if str(created) != args.creation_time:
                raise RuntimeError("Chrome process identity changed; refusing stale PID")
        elif not args.started_after_ms - 1000 <= created_ms <= int(time.time() * 1000) + 1000:
            raise RuntimeError("Chrome process predates this owned launch")

        def owned_windows():
            inventory = []
            @callback_type
            def visit(window, _state):
                process = wintypes.DWORD()
                user32.GetWindowThreadProcessId(window, ctypes.byref(process))
                if process.value != args.pid:
                    return True
                name, title = ctypes.create_unicode_buffer(256), ctypes.create_unicode_buffer(1024)
                user32.GetClassNameW(window, name, len(name))
                user32.GetWindowTextW(window, title, len(title))
                rectangle = wintypes.RECT()
                has_bounds = bool(user32.GetWindowRect(window, ctypes.byref(rectangle)))
                minimized = bool(user32.IsIconic(window))
                if minimized:
                    placement = WindowPlacement()
                    placement.length = ctypes.sizeof(placement)
                    if user32.GetWindowPlacement(window, ctypes.byref(placement)):
                        rectangle, has_bounds = placement.rcNormalPosition, True
                owner = user32.GetWindow(window, 4) or 0  # GW_OWNER excludes transient widgets/popups.
                root = user32.GetAncestor(window, 2) == window  # GA_ROOT
                visible = bool(user32.IsWindowVisible(window))
                width, height = rectangle.right - rectangle.left, rectangle.bottom - rectangle.top
                cloaked = wintypes.DWORD()
                cloak_result = dwmapi.DwmGetWindowAttribute(window, 14, ctypes.byref(cloaked), ctypes.sizeof(cloaked))
                reasons = []
                if name.value != "Chrome_WidgetWin_1": reasons.append("not_browser_class")
                if not visible: reasons.append("not_visible")
                if owner: reasons.append("owned_popup")
                if not root: reasons.append("not_root")
                if not has_bounds or width < 480 or height < 320: reasons.append("not_substantial")
                if cloak_result != 0 or cloaked.value: reasons.append("not_active_desktop")
                inventory.append({"window": str(window), "class": name.value,
                                  "title": title.value[:160], "owner": str(owner), "root": root,
                                  "visible": visible, "minimized": minimized,
                                  "bounds": [rectangle.left, rectangle.top, rectangle.right, rectangle.bottom],
                                  "width": width, "height": height, "excluded": reasons})
                return True
            user32.EnumWindows(visit, 0)
            return inventory

        def title_matches(value):
            expected = args.title.strip()
            return (not expected or value == expected or
                    any(value.startswith(expected + separator) for separator in (" - ", " — ", " – ")))

        # A navigation can update the native title shortly after CDP reports
        # completion. Wait briefly for the pinned real document/window, never
        # switch to a different substantial window when the pin is missing.
        inventory, matches = [], []
        for attempt in range(12):
            inventory = owned_windows()
            matches = [item for item in inventory if not item["excluded"]
                       and (not args.window or item["window"] == str(args.window))
                       and title_matches(item["title"])]
            if len(matches) == 1:
                break
            if attempt < 11:
                time.sleep(0.1)
        if len(matches) != 1:
            raise FocusError("Cannot identify the intended substantial, unowned QA Chrome root window"
                             + (" with pinned HWND " + str(args.window) if args.window else "")
                             + (" and CDP document title " + repr(args.title[:160]) if args.title else ""), inventory)
        window = int(matches[0]["window"])

        def identity_alive():
            code = wintypes.DWORD()
            if not kernel32.GetExitCodeProcess(handle, ctypes.byref(code)) or code.value != 259:
                raise RuntimeError("Owned Chrome process exited")
            owner = wintypes.DWORD()
            user32.GetWindowThreadProcessId(window, ctypes.byref(owner))
            if owner.value != args.pid:
                raise FocusError("Chrome window ownership changed", owned_windows())

        def restore(unlock=False):
            identity_alive()
            current_thread = kernel32.GetCurrentThreadId()
            owner = wintypes.DWORD()
            target_thread = user32.GetWindowThreadProcessId(window, ctypes.byref(owner))
            foreground_thread = user32.GetWindowThreadProcessId(user32.GetForegroundWindow(), ctypes.byref(owner))
            attached = []
            try:
                for thread in {target_thread, foreground_thread}:
                    if thread and thread != current_thread and user32.AttachThreadInput(current_thread, thread, True):
                        attached.append(thread)
                user32.ShowWindowAsync(window, 9)  # SW_RESTORE, real desktop window.
                if unlock and user32.GetForegroundWindow() != window:
                    # Same focus-unlock technique used by the authorized console helper.
                    user32.keybd_event(0x12, 0, 0, 0)
                    user32.keybd_event(0x12, 0, 2, 0)
                    user32.SwitchToThisWindow(window, True)
                user32.BringWindowToTop(window)
                user32.SetForegroundWindow(window)
            finally:
                for thread in reversed(attached):
                    user32.AttachThreadInput(current_thread, thread, False)

        if not args.verify_only:
            for attempt in range(5):
                restore(unlock=attempt > 0)
                time.sleep(0.1)
                if user32.GetForegroundWindow() == window and not user32.IsIconic(window):
                    break
        identity_alive()
        cloaked = wintypes.DWORD()
        if dwmapi.DwmGetWindowAttribute(window, 14, ctypes.byref(cloaked), ctypes.sizeof(cloaked)) != 0 or cloaked.value:
            raise FocusError("Owned Chrome window is not visible on the active desktop", owned_windows())
        rectangle = wintypes.RECT()
        if not user32.GetWindowRect(window, ctypes.byref(rectangle)):
            raise ctypes.WinError(ctypes.get_last_error())
        left, top = user32.GetSystemMetrics(76), user32.GetSystemMetrics(77)
        right = left + user32.GetSystemMetrics(78)
        bottom = top + user32.GetSystemMetrics(79)
        visible = (not user32.IsIconic(window) and user32.IsWindowVisible(window)
                   and min(right, rectangle.right) > max(left, rectangle.left)
                   and min(bottom, rectangle.bottom) > max(top, rectangle.top))
        foreground = user32.GetForegroundWindow() == window
        title = ctypes.create_unicode_buffer(1024)
        user32.GetWindowTextW(window, title, len(title))
        result = {"pid": args.pid, "creation_time": str(created), "window": str(window),
                  "foreground": bool(foreground), "visible": bool(visible), "title": title.value,
                  "bounds": [rectangle.left, rectangle.top, rectangle.right, rectangle.bottom],
                  "verified_at": time.time(), "restore": not args.verify_only}
        if not foreground or not visible:
            raise FocusError("Windows did not place the owned Chrome window visibly in foreground", owned_windows())
        if args.content_x is not None:
            if args.content_y is None or not (0 <= args.content_x <= 10000 and 0 <= args.content_y <= 10000):
                raise RuntimeError("Invalid observed inert content point")
            # CDP identifies an inert DOM background first. A real mouse click
            # in that renderer transfers focus from Chrome's native chrome.
            # This is never focus emulation and never clicks a page control.
            children = []
            @callback_type
            def renderer(child, _state):
                name = ctypes.create_unicode_buffer(256)
                user32.GetClassNameW(child, name, len(name))
                if name.value == "Chrome_RenderWidgetHostHWND" and user32.IsWindowVisible(child):
                    bounds = wintypes.RECT()
                    if user32.GetWindowRect(child, ctypes.byref(bounds)) and bounds.right-bounds.left > 100 and bounds.bottom-bounds.top > 100:
                        children.append(bounds)
                return True
            user32.EnumChildWindows(window, renderer, 0)
            if len(children) != 1:
                raise RuntimeError("Cannot identify one visible owned Chrome renderer")
            user32.GetDpiForWindow.argtypes = [wintypes.HWND]
            scale = user32.GetDpiForWindow(window) / 96
            bounds = children[0]
            x, y = round(bounds.left + args.content_x*scale), round(bounds.top + args.content_y*scale)
            if not (bounds.left <= x < bounds.right and bounds.top <= y < bounds.bottom):
                raise RuntimeError("Content point is outside the renderer")
            identity_alive()
            if user32.GetForegroundWindow() != window:
                raise RuntimeError("Chrome lost foreground before content focus")
            user32.SetCursorPos(x, y)
            user32.mouse_event(2, 0, 0, 0, 0)
            user32.mouse_event(4, 0, 0, 0, 0)
            result["content_focus_click"] = [args.content_x, args.content_y]
        print(json.dumps(result), flush=True)
    finally:
        kernel32.CloseHandle(handle)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        result = {"error": str(error)}
        if isinstance(error, FocusError):
            result["windows"] = error.windows
        print(json.dumps(result), flush=True)
        sys.exit(1)
