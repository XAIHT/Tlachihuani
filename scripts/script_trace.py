"""Verbose log of a Tlachihuani Python script (the Python twin of Trace-Script.ps1).

    import script_trace; script_trace.start(__file__)

records every line the script executes, each call with its arguments and the value it
returns, and any exception, in
    %LOCALAPPDATA%\\XAIHT\\Tlachihuani\\script-logs\\<script>-<date>-<time>-p<pid>.log
XT copies each line into Tlachihuani.log, stamped and with secrets masked.
TLACHIHUANI_LOG_LEVEL=off records nothing; any level other than trace records only
the start and the end.
"""

import atexit
import linecache
import os
import reprlib
import sys
import threading
import time

_short = reprlib.Repr()
_short.maxstring = 240
_short.maxother = 240
_started = False


def start(script_file):
    global _started
    if _started:
        return
    level = (os.environ.get("TLACHIHUANI_LOG_LEVEL") or "trace").strip().lower()
    if level == "off":
        return
    custom = os.environ.get("TLACHIHUANI_USER_DIR")
    base = os.path.abspath(custom) if custom else os.path.join(
        os.environ.get("LOCALAPPDATA") or os.path.expanduser("~"), "XAIHT", "Tlachihuani")
    folder = os.path.join(base, "script-logs")
    target = os.path.abspath(script_file)
    stem = os.path.splitext(os.path.basename(target))[0]
    try:
        os.makedirs(folder, exist_ok=True)
        path = os.path.join(folder, f"{stem}-{time.strftime('%Y%m%d-%H%M%S')}-p{os.getpid()}.log")
        stream = open(path, "a", encoding="utf-8", buffering=1, newline="\r\n")
    except OSError:
        return
    _started = True
    lock = threading.Lock()
    name = os.path.basename(target)

    def write(text):
        with lock:
            try:
                stream.write(text.replace("\r", " ").replace("\n", " ") + "\n")
            except (OSError, ValueError):
                pass

    def show(value):
        try:
            return _short.repr(value)
        except Exception:
            return "<" + type(value).__name__ + ">"

    write(f"{name} started: python {sys.version.split()[0]} at {sys.executable}, arguments {sys.argv[1:]}, "
          f"working folder {os.getcwd()}, pid {os.getpid()}")
    # Nothing is printed: callers parse these scripts' output, and Windows PowerShell can
    # treat any stderr text of a redirected native command as a failure.

    def finished():
        write(f"{name} finished")
        sys.settrace(None)
        with lock:
            stream.close()

    atexit.register(finished)
    if level != "trace":
        return

    def local(frame, event, arg):
        code = frame.f_code
        where = f"{name}:{frame.f_lineno} {code.co_name}  "
        if event == "line":
            write(where + "· " + linecache.getline(code.co_filename, frame.f_lineno).strip())
        elif event == "return":
            write(where + f"← {code.co_qualname} returns {show(arg)}")
        elif event == "exception":
            write(where + f"exception {arg[0].__name__}: {arg[1]}")
        return local

    def call(frame, event, arg):
        if event != "call" or os.path.abspath(frame.f_code.co_filename) != target:
            return None
        code = frame.f_code
        count = code.co_argcount + code.co_kwonlyargcount
        values = frame.f_locals
        arguments = ", ".join(f"{n}={show(values.get(n))}" for n in code.co_varnames[:count])
        write(f"{name}:{frame.f_lineno} {code.co_name}  → {code.co_qualname}({arguments})")
        return local

    threading.settrace(call)
    sys.settrace(call)
    # The script's own top level is already running: trace it from the next line on.
    caller = sys._getframe(1)
    if os.path.abspath(caller.f_code.co_filename) == target:
        caller.f_trace = local
