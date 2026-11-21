"""Verbose trace log of the Torrenter engine (torrent.log), the Python twin of TraceLog.

Every line of the engine's own files (worker, api, engine, storage, delivery) is logged as
it runs, with the call's arguments and the value it returns, plus every libtorrent alert
and every request and reply exchanged with Jobs. The format matches the C# trace lines,
so XT copies them into Tlachihuani.log next to the services' own.

The folder comes from TLACHIHUANI_LOG_DIR (set by Jobs), else <state>/logs.
TLACHIHUANI_LOG_LEVEL: trace (default), debug, info, warn, error or off.
Secrets (computer keys, bearer tokens, passwords, one-time codes) are masked.
"""

import atexit
import datetime
import linecache
import os
import queue
import re
import reprlib
import sqlite3
import sys
import threading
import time
import traceback

TRACE, DEBUG, INFO, WARN, ERROR, OFF = 0, 1, 2, 3, 4, 9
_LEVELS = {"trace": TRACE, "debug": DEBUG, "info": INFO, "warn": WARN, "warning": WARN, "error": ERROR, "off": OFF}
_NAMES = {TRACE: "TRACE", DEBUG: "DEBUG", INFO: "INFO ", WARN: "WARN ", ERROR: "ERROR"}
ROTATE_BYTES = 256 * 1024 * 1024
KEEP_FILES = 4

level = _LEVELS.get((os.environ.get("TLACHIHUANI_LOG_LEVEL") or "trace").strip().lower(), TRACE)
_component = "torrent"
_queue = None
_writer = None
path = None
_pid = os.getpid()
_root = os.path.dirname(os.path.abspath(__file__))
_self = os.path.abspath(__file__)
_ours = {}

_KEY = re.compile(r"xrc_[A-Za-z0-9_\-]{8,}")
_BEARER = re.compile(r"(?i)(bearer\s+)[A-Za-z0-9._~+/=\-]{8,}")
_SECRET_FIELDS = r"(?:access_|refresh_|id_)?token|secret|password|passwd|authorization|cookie|credential|api_?key|key|invitation|code_verifier|client_secret|nonce"
_JSON_FIELD = re.compile(r'(?i)"(' + _SECRET_FIELDS + r')"\s*:\s*"[^"]*"')
_PY_FIELD = re.compile(r"(?i)'(" + _SECRET_FIELDS + r")'\s*:\s*'[^']*'")
_ASSIGNMENT = re.compile(r"(?i)\b((?:access_|refresh_)?token|password|passwd|secret|api_?key|Key)\s*=\s*(?!\*\*\*)[^\s,;)}\]]+")
_QUERY = re.compile(r'(?i)([?&](?:ticket|invite|invitation|code|state|session|token|access_token|key|password|secret|nonce|sig|signature|otp)=)[^&\s#"]+')
_SECRET_NAME = re.compile(r"(?i)^(.*token.*|.*secret.*|.*passw.*|authorization|cookie|.*credential.*|api_?key|key|invitation|code_verifier|bearer|.*nonce.*)$")


def redact(text):
    if not text:
        return text
    text = _KEY.sub("xrc_***", text)
    text = _BEARER.sub(r"\1***", text)
    text = _JSON_FIELD.sub(r'"\1":"***"', text)
    text = _PY_FIELD.sub(r"'\1':'***'", text)
    text = _ASSIGNMENT.sub(r"\1=***", text)
    text = _QUERY.sub(r"\1***", text)
    return text


class _Short(reprlib.Repr):
    """Readable values without building a megabyte repr first."""

    def __init__(self):
        super().__init__()
        self.maxlevel = 3
        self.maxdict = 24
        self.maxlist = 24
        self.maxtuple = 24
        self.maxset = 24
        self.maxstring = 240
        self.maxother = 240
        self.maxlong = 60

    def repr_bytes(self, value, level):
        return f"bytes[{len(value)}]"

    def repr_bytearray(self, value, level):
        return f"bytearray[{len(value)}]"

    def repr_memoryview(self, value, level):
        return f"memoryview[{len(value)}]"

    def repr_Row(self, value, level):
        return self.repr1(dict(zip(value.keys(), tuple(value))), level)

    def repr_dict(self, value, level):
        masked = {k: ("***" if isinstance(k, str) and _SECRET_NAME.match(k) else v) for k, v in value.items()}
        return super().repr_dict(masked, level)


_short = _Short()


def show(value):
    try:
        if isinstance(value, sqlite3.Row):
            value = dict(zip(value.keys(), tuple(value)))
        text = _short.repr(value)
    except Exception:
        text = "<" + type(value).__name__ + ">"
    return redact(text)


def cut(text, limit=65536):
    return text if len(text) <= limit else text[:limit] + f"… ({len(text)} characters)"


_wall_base = time.time()
_perf_base = time.perf_counter()


def _now():
    # The Windows wall clock advances in ~15 ms steps; the performance counter gives
    # microsecond order between lines, anchored to the wall clock once.
    return _wall_base + (time.perf_counter() - _perf_base)


def _stamp(moment):
    now = datetime.datetime.fromtimestamp(moment).astimezone()
    zone = now.strftime("%z")
    return now.strftime("%Y-%m-%d %H:%M:%S.%f") + " " + zone[:3] + ":" + zone[3:]


def _clock(moment):
    return datetime.datetime.fromtimestamp(moment).strftime("%H:%M:%S")


def _format(lvl, message, file="", line=0, member="", moment=None, tid=None):
    where = f"{os.path.basename(file)}:{line} {member}  " if file else ""
    message = message.replace("\r", " ").replace("\n", " ")
    moment = _now() if moment is None else moment
    tid = threading.get_native_id() if tid is None else tid
    return f"{_stamp(moment)}  {_NAMES.get(lvl, 'ERROR')}  {_component}  [{_pid}:{tid}]  {where}{message}"


def _put(text):
    try:
        _queue.put(text)
    except Exception:
        pass


# ---- Passes: idle loops write nothing (Angela, 21 November 2026) --------------------
# The engine's tick (about three times a second) and each request from Jobs run inside a
# pass. A pass identical in shape to the previous pass of the same name (the lines and
# calls it went through, its messages with every number, id and time removed) is not
# written; a different one is written in full, after one line counting the identical passes
# left out. While nothing changes, one line every ten minutes says how many were left out.
# The C# side does the same (TraceLog.Pass).

_SHAPE = re.compile(r"[\w.\-]*\d[\w.\-]*")
MAX_PASS_ENTRIES = 200000
NOTE_EVERY = 600.0
_current = threading.local()
_history = {}
_history_lock = threading.Lock()
_quiet_codes = set()


def quiet(func):
    """A function whose own lines are not traced (an idle loop); what it calls still is."""
    _quiet_codes.add(func.__code__)
    return func


def _render(entry):
    moment, tid, lvl, message, file, line, member, block_text = entry
    if message is None:
        message = "· " + redact(linecache.getline(file, line).strip())
    lines = [_format(lvl, message, file, line, member, moment, tid)]
    if block_text is not None:
        lines.extend("    " + part for part in block_text.replace("\r\n", "\n").split("\n"))
    return lines


def _note(text):
    _put(_format(INFO, text))


class PassScope:
    def __init__(self, name):
        self.name = name
        self.entries = []
        self.shape = 0
        self.overflowed = False
        self.active = False
        self.significant = False

    def rename(self, name):
        self.name = name

    def __enter__(self):
        if _queue is not None and getattr(_current, "pass_", None) is None:
            self.active = True
            _current.pass_ = self
        return self

    def __exit__(self, *exc):
        if self.active:
            _current.pass_ = None
            self.active = False
            self._finish()
        return False

    def add(self, entry, kind, key, line, text):
        if self.overflowed:
            return False
        if len(self.entries) >= MAX_PASS_ENTRIES:
            # Too big to hold back: clearly busy. Write what it has and stream the rest.
            self.overflowed = True
            for held in self.entries:
                for item in _render(held):
                    _put(item)
            self.entries = []
            return False
        self.shape = hash((self.shape, kind, key, line, None if text is None else _SHAPE.sub("#", text)))
        if kind in ("M", "B") and entry[2] >= DEBUG:
            self.significant = True
        self.entries.append(entry)
        return True

    # Like TraceLog.PassHistory: a pass of code only is left out when it matches any of the
    # last 16 shapes (so the 30- and 60-second maintenance branches stay quiet); a pass that
    # writes a real message (Debug or higher) only when it matches the previous pass exactly.
    def _finish(self):
        now = _now()
        with _history_lock:
            known = _history.get(self.name)
            recent = list(known["recent"]) if known is not None else []
            if self.overflowed:
                _history[self.name] = dict(shape=None, recent=recent, repeats=0, since=now, last=now, note=now)
                return
            same = known is not None and (known["shape"] == self.shape if self.significant else self.shape in known["recent"])
            if same:
                known["shape"] = self.shape
                if self.shape in known["recent"]:
                    known["recent"].remove(self.shape)
                known["recent"].append(self.shape)
                del known["recent"][:-16]
                known["repeats"] += 1
                known["last"] = now
                if now - known["note"] >= NOTE_EVERY:
                    _note(f"(no change) {self.name}: {known['repeats']} identical passes since {_clock(known['since'])} were not written")
                    known.update(repeats=0, since=now, note=now)
                return
            if known is not None and known["repeats"] > 0:
                _note(f"(no change) {self.name}: {known['repeats']} identical passes between {_clock(known['since'])} "
                      f"and {_clock(known['last'])} were not written; the next pass differs")
            for entry in self.entries:
                for item in _render(entry):
                    _put(item)
            if self.shape in recent:
                recent.remove(self.shape)
            recent = (recent + [self.shape])[-16:]
            _history[self.name] = dict(shape=self.shape, recent=recent, repeats=0, since=now, last=now, note=now)


def pass_scope(name):
    """with tracing.pass_scope("engine tick"): ... — see the comment above."""
    return PassScope(name)


# kind: S line, E call, X return, M message, B message with a block.
def _record(lvl, kind, message, file, line, member, key, text, block_text=None):
    if _queue is None or lvl < level or level == OFF:
        return
    entry = (_now(), threading.get_native_id(), lvl, message, file, line, member, block_text)
    current = getattr(_current, "pass_", None)
    if current is not None and current.add(entry, kind, key, line, text):
        return
    for item in _render(entry):
        _put(item)


def _caller(depth):
    # depth 0 = the function that called _caller's caller
    frame = sys._getframe(depth + 2)
    return frame.f_code.co_filename, frame.f_lineno, frame.f_code.co_name


def log(lvl, message, _depth=0):
    if _queue is None or lvl < level or level == OFF:
        return
    file, line, member = _caller(_depth)
    text = redact(message)
    _record(lvl, "M", text, file, line, member, file, text)


def trace(message):
    log(TRACE, message, 1)


def debug(message):
    log(DEBUG, message, 1)


def info(message):
    log(INFO, message, 1)


def warn(message):
    log(WARN, message, 1)


def error(message):
    log(ERROR, message, 1)


def block(lvl, header, text):
    """A header line, then the block indented by four spaces (a traceback, a JSON body)."""
    if _queue is None or lvl < level or level == OFF:
        return
    file, line, member = _caller(0)
    head, body = redact(header), redact(text)
    _record(lvl, "B", head, file, line, member, file, head + body, body)


def _write_loop(target):
    stream = open(target, "ab")
    size = stream.tell()
    while True:
        item = _queue.get()
        batch = [item]
        while len(batch) < 20000:
            try:
                batch.append(_queue.get_nowait())
            except queue.Empty:
                break
        stop = None in batch
        data = "".join(line + "\r\n" for line in batch if line is not None).encode("utf-8", "replace")
        try:
            if size + len(data) > ROTATE_BYTES and size > 0:
                stream.close()
                _rotate(target)
                stream = open(target, "ab")
                size = 0
            stream.write(data)
            stream.flush()
            size += len(data)
        except OSError:
            pass
        if stop:
            stream.close()
            return


def _rotate(target):
    stem, ext = os.path.splitext(target)
    for n in range(KEEP_FILES - 1, 0, -1):
        older = f"{stem}.{n}{ext}"
        if os.path.exists(older):
            if n == KEEP_FILES - 1:
                os.remove(older)
            else:
                os.replace(older, f"{stem}.{n + 1}{ext}")
    os.replace(target, f"{stem}.1{ext}")


def _is_ours(filename):
    known = _ours.get(filename)
    if known is None:
        full = os.path.abspath(filename)
        known = os.path.dirname(full) == _root and full != _self
        _ours[filename] = known
    return known


def _arguments(frame):
    code = frame.f_code
    count = code.co_argcount + code.co_kwonlyargcount
    if code.co_flags & 0x04:
        count += 1
    if code.co_flags & 0x08:
        count += 1
    values = frame.f_locals
    parts = []
    for name in code.co_varnames[:count]:
        if name == "self":
            continue
        parts.append(f"{name}=***" if _SECRET_NAME.match(name) else f"{name}={show(values.get(name))}")
    return ", ".join(parts)


def _local(frame, event, arg):
    code = frame.f_code
    if event == "line":
        # The source text is read only if the line is written (see _render).
        _record(TRACE, "S", None, code.co_filename, frame.f_lineno, code.co_name, code.co_filename, None)
    elif event == "return":
        shown = "***" if _SECRET_NAME.match(code.co_name) else show(arg)
        _record(TRACE, "X", f"← {code.co_qualname} returns {shown}", code.co_filename, frame.f_lineno, code.co_name, code.co_qualname, None)
    elif event == "exception":
        kind, value, _ = arg
        text = f"exception {kind.__name__}: {redact(str(value))}"
        _record(TRACE, "M", text, code.co_filename, frame.f_lineno, code.co_name, code.co_filename, text)
    return _local


def _global(frame, event, arg):
    code = frame.f_code
    if event != "call" or code in _quiet_codes or not _is_ours(code.co_filename):
        return None
    _record(TRACE, "E", f"→ {code.co_qualname}({_arguments(frame)})", code.co_filename, frame.f_lineno, code.co_name, code.co_qualname, None)
    return _local


def setup(component, fallback_folder):
    """Opens torrent.log and starts tracing every line of the engine's own files."""
    global _component, _queue, _writer, path
    if _queue is not None or level == OFF:
        return
    _component = component
    folder = os.environ.get("TLACHIHUANI_LOG_DIR") or fallback_folder
    try:
        os.makedirs(folder, exist_ok=True)
        target = os.path.join(folder, component + ".log")
        open(target, "ab").close()
    except OSError:
        return
    path = target
    _queue = queue.Queue(maxsize=200000)
    _writer = threading.Thread(target=_write_loop, args=(target,), name="torrent trace writer", daemon=True)
    _writer.start()

    previous_hook = sys.excepthook

    def unhandled(kind, value, tb):
        block(ERROR, "UNHANDLED EXCEPTION", "".join(traceback.format_exception(kind, value, tb)))
        close()
        previous_hook(kind, value, tb)

    sys.excepthook = unhandled

    def thread_failed(args):
        block(ERROR, f"thread {args.thread.name if args.thread else '?'} failed",
              "".join(traceback.format_exception(args.exc_type, args.exc_value, args.exc_traceback)))

    threading.excepthook = thread_failed
    atexit.register(close)
    info(f"trace log opened: component {component}, level {_NAMES.get(level, 'OFF').strip()}, pid {_pid}, "
         f"python {sys.version.split()[0]} at {sys.executable}, arguments {sys.argv[1:]}, file {target}, "
         f"rotation {ROTATE_BYTES // (1024 * 1024)} MB x {KEEP_FILES}")
    if level <= TRACE:
        threading.settrace(_global)
        sys.settrace(_global)


def close(timeout=3.0):
    global _queue
    if _queue is None:
        return
    sys.settrace(None)
    threading.settrace(None)
    info(f"process {_pid} closing the trace log")
    pending = _queue
    try:
        pending.put(None, timeout=timeout)
    except Exception:
        pass
    if _writer is not None:
        _writer.join(timeout)
    _queue = None
