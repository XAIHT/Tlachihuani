"""Whole-desktop test photo taken by Tlamatini's Shoter agent.

Tlachihuani — Angela López Mendoza · XAIHT.
Every browser-test photo goes through Shoter (all screens), never a page-only
browser capture and never Pillow called directly. If Shoter fails, the test
photo fails: there is no fallback.

Usage: python rc_shoter_photo.py OUTPUT_DIR NAME.png
Prints one JSON line: {"ok": true, "path": ...} or {"ok": false, "error": ...}.
"""
import importlib.util
import json
import os
import pathlib
import shutil
import sys

ENGLISH = pathlib.Path(os.environ.get("TL_TLAMATINI_ENGLISH", "C:/Development/XAIHT/Tlamatini"))


def report(value, code):
    print(json.dumps(value), flush=True)
    return code


def main():
    # Shoter logs emoji; a piped console code page must not stop the photo.
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    if len(sys.argv) != 3:
        return report({"ok": False, "error": "usage: rc_shoter_photo.py OUTPUT_DIR NAME.png"}, 2)
    output = pathlib.Path(sys.argv[1]).resolve()
    name = sys.argv[2]
    if pathlib.Path(name).name != name or not name.lower().endswith(".png"):
        return report({"ok": False, "error": "the photo name must be a plain .png file name"}, 2)
    source = ENGLISH / "Tlamatini/agent/agents/shoter/shoter.py"
    if not source.is_file():
        return report({"ok": False, "error": "Shoter agent not found at " + str(source)}, 3)
    runtime = output / "shoter-runtime"
    runtime.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, runtime / "shoter.py")
    previous = os.getcwd()
    try:
        spec = importlib.util.spec_from_file_location("rc_test_shoter", runtime / "shoter.py")
        shoter = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(shoter)
    finally:
        os.chdir(previous)
    try:
        saved = shoter.capture_screenshot(str(output), all_screens=True, filename=name)
    except Exception as error:  # Reported, never replaced by another capture.
        return report({"ok": False, "error": "Shoter could not take the photo: " + repr(error)}, 1)
    if not os.path.isfile(saved) or os.path.getsize(saved) == 0:
        return report({"ok": False, "error": "Shoter reported " + str(saved) + " but no image was saved"}, 1)
    return report({"ok": True, "path": saved}, 0)


if __name__ == "__main__":
    sys.exit(main())
