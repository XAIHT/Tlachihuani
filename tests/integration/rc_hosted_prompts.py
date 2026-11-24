"""Visible hosted relay -> actual XT command handler -> RC -> EN/ES real-model checks.

All identities, state, ports and databases are owned by this test. Google login,
Cloud SQL, installed Windows services, actual phones and production startup are
outside this check. Two prompt answers use the real model. A separately labelled
large-response storage fixture tests ticket/chunk delivery, without a model call.
Separate no-effect broker and cancellation fixtures test the real controls.
Tool approvals are never granted.
Run only after the parent has verified its visible development console.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import socket
import subprocess
import time
import urllib.request

from playwright.sync_api import sync_playwright

SERVICE = Path(r"C:\Development\XAIHT\repo\Tlachihuani")
WORKSPACE = Path(r"C:\Development\XAIHT")
WEBSITE = WORKSPACE / "web-app"
PYTHON = WORKSPACE / "Tlamatini/python/python.exe"
CHROME = Path(r"C:\Program Files\Google\Chrome\Application\chrome.exe")
NODE = Path(r"C:\Program Files\nodejs\node.exe")


def read_json(url, *, token=None):
    request = urllib.request.Request(url, method="POST" if token else "GET")
    if token:
        request.add_header("X-RC-QA-Token", token)
    with urllib.request.urlopen(request, timeout=5) as response:
        return json.load(response)


def wait_until(action, label, timeout=60):
    deadline = time.monotonic() + timeout
    last = None
    announced = time.monotonic()
    while time.monotonic() < deadline:
        try:
            value = action()
            if value:
                return value
        except (OSError, ValueError, KeyError) as error:
            last = error
        if time.monotonic() - announced >= 10:
            print("WAIT " + label, flush=True)
            announced = time.monotonic()
        time.sleep(0.25)
    raise RuntimeError("Timed out: " + label + ("; " + str(last) if last else ""))


def configuration_fingerprints(repository):
    """Hash configuration without copying or logging any private values."""
    tracked = subprocess.check_output(["git", "ls-files", "-z"], cwd=repository).decode("utf-8").split("\0")
    paths = {name for name in tracked if Path(name).suffix.lower() in {".json", ".yaml", ".yml", ".keys"}}
    paths.update("Tlamatini/agent/" + name for name in ("config.json", "external_mcps.json", "contacts.json", "data.keys"))
    return {name: hashlib.sha256((repository / name).read_bytes()).hexdigest()
            if (repository / name).is_file() else None for name in sorted(paths)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rc-image", required=True, type=Path)
    parser.add_argument("--relay-driver", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--visible-development", action="store_true")
    parser.add_argument("--web-port", type=int, default=8096)
    args = parser.parse_args()
    if not args.visible_development:
        parser.error("The parent must verify its visible console before --visible-development")
    output = args.output.resolve()
    output.relative_to((SERVICE / "artifacts").resolve())
    if output.exists():
        parser.error("Use a fresh evidence directory; existing evidence is never overwritten")
    for image in (args.rc_image, args.relay_driver):
        image.resolve().relative_to(SERVICE.resolve())
        if not image.is_file():
            parser.error("Missing built QA image: " + str(image))
    if args.rc_image.name != "Tlachihuani.RC.Server.exe":
        parser.error("Use the actual built Tlachihuani.RC.Server.exe")
    ports = [args.web_port, 12487, 12488, 12489]
    if len(set(ports)) != 4 or any(port in {8000, 8052, 12064, 3000} or not 1024 <= port <= 65535 for port in ports):
        parser.error("Choose isolated unused ports")
    for port in ports:
        with socket.socket() as probe:
            if probe.connect_ex(("127.0.0.1", port)) == 0:
                parser.error(f"Port {port} is already occupied; no existing process will be stopped")
    output.mkdir(parents=True)
    # The account fixture intentionally permits only artifact-owned binaries.
    # Copy the freshly built repository output intact; never touch an installed
    # runtime or delete/overwrite a previous acceptance directory.
    source_runtime = args.rc_image.resolve().parent
    staged_runtime = output / "runtime"
    if staged_runtime.is_relative_to(source_runtime):
        raise ValueError("The staged runtime must not be inside its source output directory")
    pending = [source_runtime]
    while pending:
        item = pending.pop()
        info = item.lstat()
        if item.is_symlink() or getattr(info, "st_file_attributes", 0) & 0x400:
            raise ValueError("The QA runtime cannot contain symlinks or reparse points: " + str(item))
        item.resolve().relative_to(source_runtime)
        if item.is_dir():
            pending.extend(item.iterdir())
    shutil.copytree(source_runtime, staged_runtime)
    source_image = args.rc_image.resolve()
    args.rc_image = staged_runtime / source_image.name
    runtime_hashes = []
    for filename in (source_image.name, "Tlachihuani.RC.Server.dll", "Tlachihuani.RC.dll"):
        source_file, staged_file = source_runtime / filename, staged_runtime / filename
        source_digest = hashlib.sha256(source_file.read_bytes()).hexdigest()
        staged_digest = hashlib.sha256(staged_file.read_bytes()).hexdigest()
        if staged_digest != source_digest:
            raise RuntimeError("The staged QA runtime differs from the fresh build: " + filename)
        runtime_hashes.append({"name": filename, "source_sha256": source_digest,
                               "staged_sha256": staged_digest})
    (output / "runtime-source.json").write_text(json.dumps({"source": str(source_image),
        "staged": str(args.rc_image), "runtime_hashes": runtime_hashes}, indent=2), encoding="utf-8")
    print("STAGED QA RUNTIME " + str(args.rc_image), flush=True)
    scratch = output / "scratch"
    scratch.mkdir()
    env = {**os.environ, **{key: str(scratch) for key in ("TEMP", "TMP", "TMPDIR", "TLAMATINI_TEMP")},
           "PYTHONDONTWRITEBYTECODE": "1", "TSX_TSCONFIG_PATH": str(WEBSITE / "tsconfig.server.json"),
           "PUBLIC_BASE_URL": f"http://127.0.0.1:{args.web_port}", "NODE_ENV": "development"}
    os.environ.update({key: env[key] for key in ("TEMP", "TMP", "TMPDIR", "TLAMATINI_TEMP", "PYTHONDONTWRITEBYTECODE")})
    nonce = secrets.token_hex(16)
    results, fixtures, photos, errors = [], [], [], []
    host = relay = website = chrome = None
    browser_pid = None
    focus_identity = {}
    secret_file = output / "isolated-computer-key.txt"
    relay_output = output / "relay"
    base = f"http://127.0.0.1:{args.web_port}"
    source_config_before = {edition: configuration_fingerprints(WORKSPACE / directory)
                            for edition, directory in (("english", "Tlamatini"), ("spanish", "Tlamatini-Spanish"))}
    (output / "source-configuration-before.json").write_text(json.dumps(source_config_before, indent=2), encoding="utf-8")

    def check(label, condition, detail=None):
        results.append({"label": label, "passed": bool(condition), "detail": detail})
        print(("PASS " if condition else "FAIL ") + label, flush=True)
        if not condition:
            raise AssertionError(label)

    def start(image, arguments, cwd=SERVICE):
        print("START " + Path(image).name + " " + str(arguments[0]), flush=True)
        return subprocess.Popen([str(image), *map(str, arguments)], cwd=cwd, env=env)

    def native(payload):
        response = subprocess.run([str(args.rc_image), "--dev-control", nonce, str(host.pid)],
                                  input=json.dumps(payload) + "\n", capture_output=True, text=True,
                                  encoding="utf-8", timeout=15, env=env)
        reply = json.loads(response.stdout)
        if response.returncode or reply.get("error"):
            raise RuntimeError("Native QA request failed: " + str(reply.get("error", response.returncode)))
        return reply

    def foreground(page):
        nonlocal focus_identity
        page.bring_to_front()
        arguments = [str(PYTHON), "-B", str(SERVICE / "tests/integration/rc_browser_focus.py"),
                     "--pid", str(browser_pid), "--started-after-ms", str(browser_started), "--visible-development"]
        if focus_identity:
            arguments += ["--window", str(focus_identity["window"]), "--creation-time", str(focus_identity["creation_time"])]
        else:
            arguments += ["--title", page.title()]
        restored = subprocess.run(arguments, capture_output=True, text=True, encoding="utf-8", timeout=20, env=env)
        identity = json.loads(restored.stdout)
        if restored.returncode or not identity.get("foreground") or not identity.get("visible"):
            raise RuntimeError("The owned browser is not visibly in front: " + str(identity))
        focus_identity = identity
        page.bring_to_front()

    def photo(page, label):
        foreground(page)
        time.sleep(0.4)
        viewport_file = output / (label + "-viewport.png")
        page.screenshot(path=str(viewport_file), full_page=False, animations="disabled")
        check("Visible browser viewport capture: " + label, viewport_file.is_file())
        photos.append(str(viewport_file))
        result = subprocess.run([str(PYTHON), "-B", str(SERVICE / "tests/integration/rc_shoter_photo.py"),
                                 str(output), label + ".png"], env=env, timeout=30)
        check("Visible desktop capture: " + label, result.returncode == 0 and (output / (label + ".png")).is_file())
        photos.append(str(output / (label + ".png")))

    try:
        print("BOUNDARY: actual source chat/model and XT command handler; isolated local auth, memory relay, fresh accounts; no installed application restart.", flush=True)
        # Register tsx in this owned Node process, rather than using its CLI
        # launcher, so shutdown cannot leave a detached child listener behind.
        website = start(NODE, ["--import", "tsx", "scripts/rc-local-server.ts", str(args.web_port)], WEBSITE)
        wait_until(lambda: read_json(base + "/api/trpc/ping"), "local website readiness")
        host = start(args.rc_image, ["--console", output / "host", nonce, "12487"])
        def host_ready():
            if host.poll() is not None:
                raise RuntimeError("Isolated RC host exited: " + str(host.returncode))
            try:
                return native({"op": "status"}).get("health") == "ready"
            except RuntimeError:
                return False  # Named pipe may not exist during normal startup.
        wait_until(host_ready, "isolated RC readiness")
        profile = output / "chrome-profile"
        (profile / "Default").mkdir(parents=True)
        (profile / "Default/Preferences").write_text(json.dumps({"credentials_enable_service": False,
            "credentials_enable_autosignin": False, "profile": {"password_manager_enabled": False}}), encoding="utf-8")
        browser_started = int(time.time() * 1000)
        chrome = subprocess.Popen([str(CHROME), "--user-data-dir=" + str(profile), "--remote-debugging-port=0",
            "--remote-debugging-address=127.0.0.1", "--no-first-run", "--no-default-browser-check", "--disable-sync",
            "--disable-extensions", "--disable-background-networking", "--window-size=1200,1000", "--new-window", "about:blank"],
            env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        browser_pid = chrome.pid
        debug_port = wait_until(lambda: (profile / "DevToolsActivePort").read_text().splitlines()[0], "visible Chrome")
        (output / "browser.json").write_text(json.dumps({"pid": browser_pid, "debugPort": int(debug_port),
            "startedAt": browser_started, "profile": str(profile)}, indent=2), encoding="utf-8")
        with sync_playwright() as playwright:
            browser = playwright.chromium.connect_over_cdp("http://127.0.0.1:" + debug_port)
            context = browser.contexts[0]
            page = context.pages[0]
            page.on("pageerror", lambda error: errors.append(str(error)))
            chunk_requests = []
            page.on("request", lambda request: chunk_requests.append(time.monotonic())
                    if "/api/trpc/rcCommands.send" in request.url and '"response.content"' in (request.post_data or "") else None)
            for edition, repo_name, port in (("english", "Tlamatini", 12488), ("spanish", "Tlamatini-Spanish", 12489)):
                directory = output / edition
                child = start(WORKSPACE / repo_name / "python/python.exe", ["-B", SERVICE / "tests/integration/rc_main_fixture.py",
                    "--edition-root", WORKSPACE / repo_name, "--port", port, "--output", directory,
                    "--rc-image", args.rc_image, "--dev-nonce", nonce, "--dev-pid", host.pid,
                    "--visible-development", "--live-model", "--storage-fixture", "--control-fixtures"])
                fixture = {"edition": edition, "child": child, "directory": directory}
                fixtures.append(fixture)
                def load_ready():
                    if child.poll() is not None:
                        raise RuntimeError(edition + " live source host exited: " + str(child.returncode))
                    return json.loads((directory / "ready.json").read_text(encoding="utf-8"))
                ready = fixture["ready"] = wait_until(load_ready, edition + " live source host", 180)
                check(edition + " source host uses live model", ready.get("live_model") is True)
                page.goto(ready["account_url"])
                foreground(page)
                page.locator("#current-password").fill(ready["password"])
                page.locator('form:has(input[value="enable"]) button[type="submit"]').click()
                page.wait_for_selector("#account-state.active", timeout=30000)
                state = read_json(ready["origin"] + "/qa/status")
                fixture["binding"] = state["profile"]["binding_id"]
                wait_until(lambda: native({"op": "adapter-status", "binding_id": fixture["binding"]}).get("ready"), edition + " real adapter heartbeat")
                photo(page, edition + "-source-enabled")
            check("Both source editions use the same configured model", fixtures[0]["ready"]["model"] == fixtures[1]["ready"]["model"], fixtures[0]["ready"]["model"])
            page.goto(base + "/tlamatini-rc?lang=en")
            foreground(page)
            page.locator("#rc-new-name").fill("RC real prompt acceptance")
            page.locator('form:has(#rc-new-name) button[type="submit"]').click()
            key = page.locator('[data-testid="rc-new-key-value"]').inner_text(timeout=15000).strip()
            check("Local relay issued an isolated computer credential", bool(re.fullmatch(r"xrc_[A-Za-z0-9_-]{43}", key)))
            secret_file.write_text(key, encoding="utf-8")
            page.locator('[data-testid="rc-new-key"] button').last.click()
            bindings_file = output / "bindings.json"
            bindings_file.write_text(json.dumps([fixture["binding"] for fixture in fixtures]), encoding="utf-8")
            relay = start(args.relay_driver, ["--xaiht-relay", "--server", base, "--key-file", secret_file,
                "--pipe", "XAIHT.RC.Dev." + nonce, "--pid", host.pid, "--image", args.rc_image,
                "--bindings-file", bindings_file, "--output", relay_output])
            wait_until(lambda: json.loads((relay_output / "ready.json").read_text(encoding="utf-8")), "real XT command handler")
            page.reload()
            page.wait_for_selector('[data-testid^="rc-computer-"][data-online="true"]', timeout=60000)
            page.wait_for_function("[...(document.querySelector('#chat-account')?.options||[])].filter(option=>option.value).length===2", timeout=90000)
            check("No local account is selected implicitly", page.locator("#chat-account").input_value() == "")
            check("Send stays disabled until the user selects an account", not page.locator('[data-testid="rc-prompt-send"]').is_enabled())
            for fixture in fixtures:
                edition = fixture["edition"]
                page.set_viewport_size({"width": 390, "height": 844})
                foreground(page)
                page.select_option("#chat-account", fixture["binding"])
                token = "RC_" + edition.upper() + "_" + secrets.token_hex(5).upper()
                if edition == "english":
                    prompt = f"What is 17 + 25? Reply in one short sentence containing the answer and the exact identifier {token}. Do not use tools, commands, files, or external services."
                else:
                    prompt = f"¿Cuánto es 17 + 25? Responde con una oración corta que incluya el resultado y el identificador exacto {token}. No uses herramientas, comandos, archivos ni servicios externos."
                page.locator("#chat-prompt").fill(prompt)
                check(edition + " real model send is enabled", page.locator('[data-testid="rc-prompt-send"]').is_enabled())
                page.locator('[data-testid="rc-prompt-send"]').click()
                row = page.locator('[data-testid="rc-conversation"]').filter(has_text=token)
                row.wait_for(timeout=20000)
                began = time.monotonic()
                while time.monotonic() - began < 300:
                    if row.locator('[data-testid="rc-permission"]').count():
                        # Never approve execution in a harmless live-model test.
                        row.locator('[data-testid="rc-permission"] button').last.click()
                        raise RuntimeError(edition + " unexpectedly requested a tool; permission was denied")
                    state = row.get_attribute("data-status")
                    if state in {"succeeded", "failed", "cancelled", "expired", "outcome_unknown"}:
                        break
                    page.wait_for_timeout(1000)
                answer = row.locator('[data-testid="rc-answer"]').inner_text(timeout=10000)
                check(edition + " hosted real prompt succeeded", row.get_attribute("data-status") == "succeeded")
                check(edition + " actual answer contains the calculation and fresh nonce", "42" in answer and token in answer, answer)
                source = read_json(fixture["ready"]["origin"] + "/qa/status")
                check(edition + " normal RAG returned exactly once", sum(event["kind"] == "model.live.returned" for event in source["events"]) == 1)
                check(edition + " source assistant history persisted the answer", any(
                    message["user_id"] != message["conversation_user_id"] and token in message["message"]
                    for message in source["history"]))
                check(edition + " other account remains untouched", source["other_account_history_count"] == 1)
                check(edition + " mobile layout has no horizontal overflow", page.evaluate("document.documentElement.scrollWidth<=innerWidth+1"))
                row.scroll_into_view_if_needed()
                photo(page, edition + "-real-answer-mobile")
                before = len(source["history"])
                page.reload()
                page.wait_for_function("[...(document.querySelector('#chat-account')?.options||[])].filter(option=>option.value).length===2", timeout=90000)
                check(edition + " reload requires explicit account selection", page.locator("#chat-account").input_value() == "")
                page.select_option("#chat-account", fixture["binding"])
                restored = page.locator('[data-testid="rc-conversation"]').filter(has_text=token)
                restored.wait_for(timeout=30000)
                check(edition + " answer survives browser reload without resubmission", token in restored.locator('[data-testid="rc-answer"]').inner_text())
                after = read_json(fixture["ready"]["origin"] + "/qa/status")
                check(edition + " reload did not repeat model call or history", len(after["history"]) == before and sum(event["kind"] == "model.live.returned" for event in after["events"]) == 1)
                (output / (edition + "-live-evidence.json")).write_text(json.dumps({"prompt": prompt, "answer": answer,
                    "elapsed_seconds": round(time.monotonic() - began, 2), "source": source}, indent=2, ensure_ascii=False), encoding="utf-8")
                print("CONTROL FIXTURES (NO MODEL OR TOOL EXECUTION): " + edition, flush=True)
                controls = fixture["ready"]["control_fixtures"]
                page.locator("#chat-prompt").fill(controls["permission_prompt"])
                page.locator('[data-testid="rc-prompt-send"]').click()
                approval_row = page.locator('[data-testid="rc-conversation"]').filter(has_text=controls["permission_prompt"])
                permission = approval_row.locator('[data-testid="rc-permission"]')
                permission.wait_for(timeout=90000)
                check(edition + " real browser shows complete no-effect approval details", "No command is executed" in permission.inner_text())
                photo(page, edition + "-approval-awaiting-denial")
                permission.get_by_role("button", name=re.compile("^Deny$|^Denegar$|^Rechazar$", re.I)).click()
                wait_until(lambda: approval_row.get_attribute("data-status") in {"succeeded", "failed", "cancelled"},
                           edition + " denied broker completes", 60)
                check(edition + " browser denial reaches the exact real broker", "PERMISSION_DENY" in approval_row.locator('[data-testid="rc-answer"]').inner_text())
                wait_until(lambda: permission.count() == 0, edition + " resolved approval disappears", 15)
                denied_status = read_json(fixture["ready"]["origin"] + "/qa/status")
                check(edition + " source records one denial and no execution", sum(event["kind"] == "permission.fixture" and event.get("decision") == "deny" for event in denied_status["events"]) == 1)
                page.locator("#chat-prompt").fill(controls["cancel_prompt"])
                page.locator('[data-testid="rc-prompt-send"]').click()
                cancelled_row = page.locator('[data-testid="rc-conversation"]').filter(has_text=controls["cancel_prompt"])
                wait_until(lambda: cancelled_row.count() and cancelled_row.get_attribute("data-status") == "running",
                           edition + " owned cancellation fixture running", 30)
                cancel_digest = hashlib.sha256(controls["cancel_prompt"].encode("utf-8")).hexdigest()
                wait_until(lambda: any(event["kind"] == "model.fixture" and event.get("input_digest") == cancel_digest
                           for event in read_json(fixture["ready"]["origin"] + "/qa/status")["events"]),
                           edition + " no-effect work actually started before cancellation", 20)
                cancelled_row.get_by_role("button", name=re.compile("^Cancel prompt$|^Cancelar instrucción$", re.I)).click()
                wait_until(lambda: cancelled_row.get_attribute("data-status") in {"succeeded", "cancelled", "failed"},
                           edition + " cancellation settles", 60)
                cancelled_status = read_json(fixture["ready"]["origin"] + "/qa/status")
                check(edition + " browser cancellation reaches the owned source epoch", any(event["kind"] == "cancellation.fixture" and event.get("cancelled") is True for event in cancelled_status["events"]))
                check(edition + " cancelled operation is shown honestly", "cancel" in cancelled_row.locator('[role="status"]').inner_text().lower())
                check(edition + " control fixtures did not repeat actual model inference", sum(event["kind"] == "model.live.returned" for event in cancelled_status["events"]) == 1)
                photo(page, edition + "-cancelled-owned-prompt")
                # This is deliberately not counted as a model answer. The QA
                # token generates a labelled large source response; every save,
                # ticket, ownership check, relay chunk and UI assembly is real.
                print("STORAGE FIXTURE (NO MODEL): " + edition, flush=True)
                storage = fixture["ready"]["storage_fixture"]
                page.locator("#chat-prompt").fill(storage["prompt"])
                page.locator('[data-testid="rc-prompt-send"]').click()
                saved = page.locator('[data-testid="rc-conversation"]').filter(has_text=storage["prompt"])
                saved.wait_for(timeout=20000)
                terminal = {"succeeded", "failed", "cancelled", "expired", "outcome_unknown"}
                wait_until(lambda: saved.get_attribute("data-status") in terminal, edition + " stored-response receipt", 120)
                check(edition + " labelled storage fixture reached the real saved receipt", saved.get_attribute("data-status") == "succeeded")
                preview = saved.locator('[data-testid="rc-answer"]').inner_text()
                check(edition + " large response initially exposes only its bounded preview", storage["end_marker"] not in preview)
                before_chunks = len(chunk_requests)
                saved.get_by_role("button", name=re.compile("complete answer|respuesta completa", re.I)).click()
                wait_until(lambda: storage["end_marker"] in saved.locator('[data-testid="rc-answer"]').inner_text(),
                           edition + " authorized full response assembly", 180)
                completed = saved.locator('[data-testid="rc-answer"]').inner_text()
                check(edition + " full saved response crossed multiple actual relay chunks", len(chunk_requests) - before_chunks >= 2,
                      {"chunks": len(chunk_requests) - before_chunks, "characters": len(completed)})
                check(edition + " complete answer retains Unicode and its final marker", "漢字" in completed and "😀" in completed and len(completed) > 24000)
                source_after_storage = read_json(fixture["ready"]["origin"] + "/qa/status")
                check(edition + " storage fixture was not counted as a real model call", sum(event["kind"] == "model.live.returned"
                      for event in source_after_storage["events"]) == 1 and sum(event["kind"] == "storage.fixture.returned"
                      for event in source_after_storage["events"]) == 1)
                saved.locator('[data-testid="rc-answer"]').scroll_into_view_if_needed()
                photo(page, edition + "-complete-stored-response")
            check("Hosted page has no uncaught JavaScript errors", not errors, errors)
            photo(page, "both-editions-complete")
    except BaseException as error:
        results.append({"label": "live acceptance", "passed": False, "error": str(error)})
        print("FAIL " + str(error), flush=True)
    finally:
        if relay:
            relay_output.mkdir(parents=True, exist_ok=True)
            (relay_output / "stop").write_text("stop", encoding="utf-8")
            try:
                relay.wait(timeout=45)
            except subprocess.TimeoutExpired:
                results.append({"label": "owned relay shutdown", "passed": False})
        if secret_file.exists() and (relay is None or relay.poll() is not None):
            secret_file.unlink()
        for fixture in fixtures:
            try:
                if "ready" in fixture:
                    read_json(fixture["ready"]["origin"] + "/qa/shutdown", token=fixture["ready"]["shutdown_token"])
                    fixture["child"].wait(timeout=30)
                    check(fixture["edition"] + " owned source adapter stopped cleanly", fixture["child"].returncode == 0)
                elif fixture["child"].poll() is None:
                    fixture["child"].terminate()
                    fixture["child"].wait(timeout=10)
            except BaseException as error:
                results.append({"label": fixture["edition"] + " shutdown", "passed": False, "error": str(error)})
        if host and host.poll() is None:
            try:
                native({"op": "development-stop"})
                host.wait(timeout=25)
                check("owned RC host stopped cleanly", host.returncode == 0)
            except BaseException as error:
                results.append({"label": "RC shutdown", "passed": False, "error": str(error)})
        if website and website.poll() is None:
            website.terminate()
            website.wait(timeout=10)
        config_evidence = {}
        for edition, directory in (("english", "Tlamatini"), ("spanish", "Tlamatini-Spanish")):
            after = configuration_fingerprints(WORKSPACE / directory)
            before = source_config_before[edition]
            changed = [name for name in sorted(before.keys() | after.keys()) if before.get(name) != after.get(name)]
            config_evidence[edition] = {"unchanged": not changed, "files": len(after), "changed_paths": changed,
                                       "before": before, "after": after}
            results.append({"label": edition + " source configuration remains byte-identical",
                            "passed": not changed, "detail": {"files": len(after), "changed_paths": changed}})
            print(("PASS " if not changed else "FAIL ") + edition + " source configuration remains byte-identical", flush=True)
        (output / "source-configuration-integrity.json").write_text(json.dumps(config_evidence, indent=2), encoding="utf-8")
        passed = bool(results) and all(item["passed"] for item in results)
        (output / "results.json").write_text(json.dumps({"passed": passed, "checks": results, "photos": photos,
            "scope": "Actual hosted relay, XT command handler, native RC, EN/ES AccountWorker/RuntimeSession/AgentConsumer and configured real model",
            "limitations": ["isolated local authenticated test user and memory relay", "fresh source account databases and empty catalogs/context",
                "large-response cases are explicitly deterministic storage fixtures, separate from two real model prompts",
                "approval-deny and cancellation cases use deterministic no-effect fixtures with the actual broker and epoch",
                "application startup hooks, host speech, reaping, wizard and Torrenter side effects suppressed", "no installed-service or actual-cellphone acceptance"],
            "browser_retained": browser_pid}, indent=2, ensure_ascii=False), encoding="utf-8")
        print(("PASS" if passed else "FAIL") + " LIVE HOSTED PROMPTS: " + str(output / "results.json"), flush=True)
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
