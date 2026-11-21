"""Private UTF-8 line protocol inherited from Jobs. EOF terminates the provider."""

import json
import os
import queue
import sys
import threading
import time
import traceback

import tracing
from api import Api
from engine import Engine
from storage import Journal, Problem


def main():
    first = sys.stdin.readline().strip()
    tracing.info(f"first line from Jobs: {first!r}")
    if first != "START":
        tracing.warn("Jobs did not send START; the engine exits without starting")
        return
    journal = Journal(sys.argv[1])
    engine = Engine(journal, sys.argv[2] == "development")
    api = Api(journal, engine)
    with journal.db:
        journal.db.execute(
            "UPDATE jobs SET desired='paused',revision=revision+1 WHERE desired='running' AND completed IS NULL AND json_extract(options,'$.resume_on_boot')=0"
        )
    print("READY", flush=True)
    tracing.info("engine ready: READY sent to Jobs")
    incoming = queue.Queue(4)

    @tracing.quiet
    def reader():
        try:
            for line in sys.stdin:
                incoming.put(line)
        finally:
            incoming.put(None)

    threading.Thread(target=reader, daemon=True).start()
    try:
        serve(journal, engine, api, incoming)
    finally:
        tracing.info("engine closing: pausing torrents and saving resume data")
        engine.close()
        journal.db.close()
        tracing.info("engine closed")


@tracing.quiet
def serve(journal, engine, api, incoming):
    """The waiting loop itself is not traced. Each tick and each request is, as a pass:
    written in full when it differs from the previous one, left out while identical."""
    next_tick = 0
    while True:
        if time.monotonic() >= next_tick:
            with tracing.pass_scope("engine tick"):
                tick(journal, engine, api)
            next_tick = time.monotonic() + 0.3
        try:
            line = incoming.get(timeout=max(0, next_tick - time.monotonic()))
        except queue.Empty:
            continue
        if line is None:
            tracing.info("Jobs closed the engine's input: shutting down")
            return
        with tracing.pass_scope("engine request") as current:
            handle(journal, api, line, current)


def tick(journal, engine, api):
    with journal.db:
        api.reconcile_moves()
        api.reconcile_deletions()
        engine.tick()
        api.settle_receipts()


def handle(journal, api, line, current):
    started = time.monotonic()
    tracing.info("engine ⇐ " + tracing.cut(line.strip()))
    try:
        request = json.loads(line)
        current.rename("engine request " + str(request.get("op") if isinstance(request, dict) else "?"))
        with journal.db:
            status, body = api.dispatch(request)
    except Problem as exc:
        status, body = exc.status, {"error": str(exc)}
        tracing.warn(f"request refused with {status}: {exc}")
    except (ValueError, KeyError, TypeError, RuntimeError) as exc:
        status, body = 422, {"error": "Invalid request or unsupported torrent."}
        tracing.block(tracing.WARN, f"request rejected (422): {type(exc).__name__}: {exc}", traceback.format_exc())
    except Exception:
        traceback.print_exc(file=sys.stderr)
        tracing.block(tracing.ERROR, "request failed (500)", traceback.format_exc())
        status, body = 500, {"error": "Provider operation failed."}
    reply = json.dumps({"code": status, "body": body}, allow_nan=False)
    tracing.info(f"engine ⇒ ({(time.monotonic() - started) * 1000:.1f} ms) " + tracing.cut(reply))
    print(reply, flush=True)


if __name__ == "__main__":
    # Opened before main() so that main itself is traced line by line.
    tracing.setup("torrent", os.path.join(sys.argv[1], "logs") if len(sys.argv) > 1 else "logs")
    try:
        main()
    finally:
        tracing.close()
