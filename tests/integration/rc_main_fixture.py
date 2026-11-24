# Tlachihuani / Tlamatini — Created for Angela López Mendoza · XAIHT
"""Visible-only, isolated main-application integration host for RC acceptance.

This harness renders the real edition's account page, persists real auth/profile
and chat models, runs its actual AccountWorker and RuntimeSession, and imports
the actual AgentConsumer and reply/history code. By default the model boundary is
deterministic. --live-model instead uses the edition's actual configured RAG/LLM
chain. The additional --storage-fixture flag enables one exact, fresh QA token
that returns a labelled large deterministic reply solely to test saved-response
transport, separately from real model prompts. --control-fixtures adds exact-token
no-effect approval and cancellation cases using the real broker and run epoch.
Host speech, process reaping, account-wizard execution and Torrenter
staging are suppressed explicitly inside this QA process.

The default mode does NOT certify real model inference. Neither mode certifies an
installed main application or normal application startup. Never point it at a production database or launch it
headlessly. Its parent must verify the visible foreground console first.
"""
from __future__ import annotations

import argparse
import hashlib
import html
import json
import logging
import os
from pathlib import Path
import re
import secrets
import socketserver
import stat
import subprocess
import sys
import threading
import time
import types
from wsgiref.simple_server import WSGIServer, make_server


QA_PASSWORD = "RC-QA-only-2026!"
CSS_ALLOWLIST = {
    "agent/css/ui_tokens.css",
    "agent/css/remote_controller.css",
    "agent/css/dialog_theme.css",
}


def confined(path: Path, parent: Path) -> Path:
    """Reject junction/symlink escape before creating any fixture storage."""
    absolute = path.absolute()
    for component in (absolute, *absolute.parents):
        if component.exists():
            info = component.lstat()
            if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
                raise ValueError("QA paths may not contain reparse points")
    resolved = absolute.resolve()
    resolved.relative_to(parent.resolve())
    return resolved


def protect_fixture_sources(edition_root: Path) -> list[dict[str, str]]:
    """Refuse source/profile writes even when imported helpers catch the error."""
    roots = [edition_root.resolve()]
    if os.environ.get("USERPROFILE"):
        roots.append(Path(os.environ["USERPROFILE"]).resolve())
    denied: list[dict[str, str]] = []

    def audit(event, args):
        paths = ()
        if event == "open":
            mode, flags = args[1], args[2]
            if ((isinstance(mode, str) and any(char in mode for char in "wax+"))
                    or (isinstance(flags, int) and flags & (os.O_WRONLY | os.O_RDWR | os.O_CREAT | os.O_TRUNC | os.O_APPEND))):
                paths = args[:1]
        elif event in {"os.mkdir", "os.remove", "os.rmdir", "os.chmod", "sqlite3.connect"}:
            paths = args[:1]
        elif event in {"os.rename", "os.replace", "os.link"}:
            paths = args[:2]
        elif event == "os.symlink":
            paths = args[1:2]
        for raw in paths:
            if not isinstance(raw, (str, bytes, os.PathLike)):
                continue
            path = Path(os.fsdecode(raw)).resolve()
            if any(path.is_relative_to(root) for root in roots):
                # os.makedirs(existing_directory, exist_ok=True) still emits
                # os.mkdir before Windows reports that it already exists.
                # That check changes nothing. New directories and every file
                # write/remove/rename remain forbidden below these roots.
                if event == "os.mkdir" and path.is_dir():
                    continue
                denied.append({"event": event, "path": str(path)})
                raise PermissionError("QA refuses writes to the source checkout or Windows user profile")

    sys.addaudithook(audit)
    return denied


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--edition-root", required=True, type=Path)
    parser.add_argument("--port", required=True, type=int)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--rc-image", required=True, type=Path)
    parser.add_argument("--dev-nonce", required=True)
    parser.add_argument("--dev-pid", required=True, type=int)
    parser.add_argument("--visible-development", action="store_true")
    parser.add_argument("--live-model", action="store_true",
                        help="Use the actual configured model and RAG chain; retain isolated accounts and host-effect suppression")
    parser.add_argument("--storage-fixture", action="store_true",
                        help="Permit the separately labelled exact-token large-response storage check in live mode")
    parser.add_argument("--control-fixtures", action="store_true",
                        help="Permit separately labelled no-effect approval and cancellation checks in live mode")
    result = parser.parse_args()
    if not result.visible_development:
        parser.error("Verify a visible foreground development console, then pass --visible-development")
    if (result.storage_fixture or result.control_fixtures) and not result.live_model:
        parser.error("Storage/control fixtures require --live-model and are reported separately from inference")
    if not 1024 <= result.port <= 65535 or result.port == 12064:
        parser.error("Choose an isolated non-Torrenter loopback port")
    if not re.fullmatch(r"[0-9a-f]{32}", result.dev_nonce) or result.dev_pid <= 0:
        parser.error("An explicit QA service nonce and process identity are required")
    repository = Path(__file__).resolve().parents[2]
    artifacts = repository / "artifacts"
    result.output = confined(result.output, artifacts)
    result.rc_image = confined(result.rc_image, artifacts)
    if result.rc_image.name != "Tlachihuani.RC.Server.exe" or not result.rc_image.is_file():
        parser.error("Use the isolated built RC server executable under this repository's artifacts")
    workspace = repository.parent.parent
    editions = {str((workspace / "Tlamatini").resolve()).casefold(),
                str((workspace / "Tlamatini-Spanish").resolve()).casefold()}
    result.edition_root = result.edition_root.resolve()
    if str(result.edition_root).casefold() not in editions:
        parser.error("Only the explicitly authorized English or Spanish source checkout is accepted")
    if (result.output / "fixture.sqlite3").exists() or (result.output / "ready.json").exists():
        parser.error("Choose a fresh artifact directory; previous evidence is never overwritten")
    return result


def main():
    options = arguments()
    # Publish this actual isolated listener through the same entry-point contract
    # manage.py uses. Native RC never receives a browser-supplied host/port.
    globals()["_resolve_django_port"] = lambda: options.port
    options.output.mkdir(parents=True, exist_ok=True)
    scratch = options.output / "scratch"
    scratch.mkdir()
    for key in ("TEMP", "TMP", "TMPDIR", "TLAMATINI_TEMP"):
        os.environ[key] = str(scratch)
    for key, relative in {
        "TLAMATINI_MEMORY_FILE": "mcp-memory/memory.json",
        "TLAMATINI_RUNTIMES": "mcp-runtimes",
        "UV_CACHE_DIR": "cache/uv", "npm_config_cache": "cache/npm",
        "XDG_CACHE_HOME": "cache", "HF_HOME": "cache/huggingface",
        "HUGGINGFACE_HUB_CACHE": "cache/huggingface/hub",
        "TORCH_HOME": "cache/torch", "MPLCONFIGDIR": "cache/matplotlib",
    }.items():
        os.environ[key] = str(options.output / relative)
    os.environ["PYTHONDONTWRITEBYTECODE"] = "1"
    sys.dont_write_bytecode = True
    denied_writes = protect_fixture_sources(options.edition_root)
    # This process never imports the product settings/manage.py or calls its
    # startup hooks. Every database access below resolves to this fresh file.
    os.environ.pop("DJANGO_SETTINGS_MODULE", None)
    sys.path.insert(0, str(options.edition_root / "Tlamatini"))
    logging.basicConfig(level=logging.INFO, format="[RC MAIN QA] %(levelname)s %(message)s")

    # load_catalog seeds defaults on its READ path. Redirect the real manager
    # before Django/consumers/RAG import it: no source catalog, credentials or
    # active external servers belong to this isolated account test. Keep real
    # source config/model reads unchanged; only mutable MCP state is relocated.
    qa_catalog_path = options.output / "external_mcps.json"
    qa_catalog_path.write_text('{"mcpServers": {}, "active": []}\n', encoding="utf-8")
    from agent import external_mcp_manager as qa_mcp_manager
    qa_mcp_manager.catalog_path = lambda: str(qa_catalog_path)

    from django.apps import AppConfig
    from django.conf import settings

    class IsolatedAgentConfig(AppConfig):
        name = "agent"
        path = str(options.edition_root / "Tlamatini" / "agent")

        def ready(self):
            # Deliberately no catalog/MCP/discovery/model startup.
            return None

    fixture_module = types.ModuleType("rc_main_fixture_settings")
    fixture_module.IsolatedAgentConfig = IsolatedAgentConfig
    sys.modules[fixture_module.__name__] = fixture_module
    settings.configure(
        SECRET_KEY=secrets.token_hex(32), DEBUG=False,
        ALLOWED_HOSTS=["127.0.0.1", "localhost"],
        ROOT_URLCONF=fixture_module.__name__,
        INSTALLED_APPS=["django.contrib.contenttypes", "django.contrib.auth",
                        "rc_main_fixture_settings.IsolatedAgentConfig"],
        DATABASES={"default": {"ENGINE": "django.db.backends.sqlite3",
                               "NAME": str(options.output / "fixture.sqlite3"),
                               "OPTIONS": {"timeout": 20}}},
        TEMPLATES=[{"BACKEND": "django.template.backends.django.DjangoTemplates",
                    "DIRS": [str(options.edition_root / "Tlamatini/agent/templates")],
                    "APP_DIRS": True,
                    "OPTIONS": {"context_processors": ["django.template.context_processors.request"]}}],
        MIDDLEWARE=["rc_main_fixture_settings.QaAccountMiddleware",
                    "django.middleware.csrf.CsrfViewMiddleware"],
        CHANNEL_LAYERS={"default": {"BACKEND": "channels.layers.InMemoryChannelLayer"}},
        DEFAULT_AUTO_FIELD="django.db.models.BigAutoField",
        STATIC_URL="/static/", USE_TZ=True, TIME_ZONE="UTC",
        LOGIN_URL="/qa/", DATA_UPLOAD_MAX_MEMORY_SIZE=65536,
        CSRF_COOKIE_NAME="rc_main_qa_csrf", CSRF_COOKIE_SAMESITE="Strict",
    )
    import django
    django.setup()
    from django.contrib.auth.models import AnonymousUser, Group, Permission, User
    from django.contrib.contenttypes.models import ContentType
    from django.db import connection, transaction
    from agent.models import (AcpSession, AgentMessage, ChatAgentRun, InteractionSettings,
                              RemoteControllerProfile, RemoteControllerRevocation,
                              SessionState, SkillInvocation, TorrenterProfile, UsageDaily)

    with connection.schema_editor() as schema:
        # The additional empty user-relation tables let the real ORM exercise
        # User.delete() and its profile cascade later. No catalog/workflow,
        # Torrenter, session, skill or usage row is registered by this fixture.
        for model in (ContentType, Permission, Group, User, RemoteControllerProfile,
                      RemoteControllerRevocation, AgentMessage, TorrenterProfile,
                      ChatAgentRun, SessionState, AcpSession, SkillInvocation,
                      InteractionSettings, UsageDaily):
            schema.create_model(model)
        if options.live_model:
            # Normal RAG and tool discovery may read their catalog tables. They
            # are empty in this owned database; never borrow production rows.
            from django.apps import apps
            existing = set(connection.introspection.table_names())
            for model in apps.get_app_config("agent").get_models():
                if model._meta.db_table not in existing:
                    schema.create_model(model)
                    existing.add(model._meta.db_table)

    from agent import remote_controller_bridge as bridge
    from agent.global_state import global_state
    edition = bridge.EDITION
    username = "rc-qa-" + edition
    user = User.objects.create_user(username=username, password=QA_PASSWORD, first_name="QA")
    qa_user_id = user.pk
    other = User.objects.create_user(username="rc-qa-other-" + edition, password=secrets.token_hex(32))
    AgentMessage.objects.create(user=other, conversation_user=other,
                                message="OTHER_ACCOUNT_QA_SENTINEL_DO_NOT_DISCLOSE")
    audit = []
    audit_lock = threading.Lock()

    def record(kind, **fields):
        with audit_lock:
            audit.append({"at": time.time(), "kind": kind, **fields})
            del audit[:-512]
        print("[RC MAIN QA] " + kind, flush=True)

    def qa_native(request):
        # Fixed, trusted harness arguments; no browser-controlled executable or
        # command line. The helper verifies the QA pipe's server PID and image.
        payload = json.dumps(request, ensure_ascii=True, separators=(",", ":"))
        if len(payload.encode("utf-8")) > 65536:
            raise bridge.RcBridgeError("request_too_large", "QA native request exceeds the limit")
        try:
            result = subprocess.run(
                [str(options.rc_image), "--dev-control", options.dev_nonce, str(options.dev_pid)],
                input=payload + "\n", stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                text=True, encoding="utf-8", timeout=12, check=False,
            )
            if len(result.stdout.encode("utf-8")) > 65536:
                raise ValueError("QA native response exceeds the limit")
            reply = json.loads(result.stdout)
            if not isinstance(reply, dict):
                raise ValueError("QA native response must be an object")
            if reply.get("error"):
                error = reply["error"]
                raise bridge.RcBridgeError(str(error.get("code", "service_unavailable")),
                                           str(error.get("message", "QA service refused the request")))
            if result.returncode:
                raise ValueError("QA native helper failed")
            if request.get("op") not in {"adapter-heartbeat", "adapter-next", "status"}:
                record("native." + str(request.get("op")))
            return reply
        except bridge.RcBridgeError:
            raise
        except (OSError, ValueError, subprocess.SubprocessError) as exc:
            raise bridge.RcBridgeError("service_unavailable", "Isolated QA service unavailable") from exc

    bridge.native = qa_native

    # Default mode replaces RAG before importing the real consumer. Live mode
    # leaves the actual RAG implementation intact. Both keep the normal message,
    # history, permission broker, cancellation and response-parser paths.
    from agent.cancellation import clear_cancel_generation, is_run_cancelled, request_cancel_generation
    from agent.exec_permission import get_broker

    class QaChain:
        pass

    def qa_model(chain, payload, chat_history=None, inet_enabled=False):
        owner = payload.get("conversation_user_id")
        text = str(payload.get("input", ""))
        if owner != user.pk or not payload.get("multi_turn_enabled") or not payload.get("ask_execs_enabled"):
            raise RuntimeError("QA model received incorrect account or permission flags")
        history = [str(getattr(item, "content", item)) for item in (chat_history or [])]
        if any("OTHER_ACCOUNT_QA_SENTINEL" in item for item in history):
            raise RuntimeError("QA detected another account in conversation history")
        record("model.fixture", user_id=owner, history_count=len(history),
               input_digest=hashlib.sha256(text.encode("utf-8")).hexdigest())
        outcome = "REPLY"
        if text.startswith("QA_PERMISSION"):
            broker = get_broker(owner)
            if broker is None:
                raise RuntimeError("QA permission broker was not registered")
            decision = broker.request_permission({
                "tool_name": "qa_no_effect", "agent_display": "QA permission fixture",
                "kind": "Tool", "program": "No command is executed", "shell": "none",
                "parameters": "This checks only the real Proceed/Deny round trip.",
            })
            outcome = "PERMISSION_" + decision.upper()
            record("permission.fixture", decision=decision)
        elif text.startswith("QA_CANCEL"):
            deadline = time.monotonic() + 40
            while time.monotonic() < deadline:
                if is_run_cancelled(owner, payload.get("cancel_run_epoch")):
                    record("cancellation.fixture", cancelled=True)
                    outcome = "CANCELLED"
                    break
                time.sleep(0.05)
            else:
                raise RuntimeError("QA cancellation was not requested within the fixture bound")
        global_state.set_state(f"last_request_meta::{owner}", {
            "multi_turn_used": True, "exec_report_enabled": True, "exec_report_entries": [],
        })
        safe_text = html.escape(text[:1500]).replace("`", "&#96;")
        return f"QA_MODEL_{edition.upper()}_{outcome}: {safe_text}"

    if not options.live_model:
        rag = types.ModuleType("agent.rag")
        rag.__path__ = [str(options.edition_root / "Tlamatini/agent/rag")]
        rag.ask_rag = qa_model
        rag.setup_llm = lambda *args, **kwargs: QaChain()
        rag.setup_llm_with_context = lambda *args, **kwargs: QaChain()
        rag.request_cancel_generation = request_cancel_generation
        rag.clear_cancel_generation = clear_cancel_generation
        rag.BasicPromptOnlyChain = type("QaBasicPromptOnlyChain", (), {})
        rag.OptimizedHistoryAwareRAGChain = type("QaOptimizedHistoryAwareRAGChain", (), {})
        sys.modules["agent.rag"] = rag
    wizard = types.ModuleType("agent.user_creation_wizard")
    wizard.request_candidate = lambda *args, **kwargs: False
    wizard.handle_request = lambda *args, **kwargs: None
    sys.modules["agent.user_creation_wizard"] = wizard
    torrenter = types.ModuleType("agent.torrenter_bridge")
    torrenter.stage_chat_magnets = lambda _user, message: message
    sys.modules["agent.torrenter_bridge"] = torrenter
    from agent import host_voice
    host_voice.answer_finished = lambda *args, **kwargs: None
    from agent.consumers import AgentConsumer

    async def no_reaping(_self, _survivors=None):
        record("reaping.suppressed")
    AgentConsumer._tier2_orphan_sweep = no_reaping
    model_description = "deterministic QA replacement; no real inference"
    storage_token = "QA_STORAGE_RESPONSE_" + secrets.token_hex(16) if options.storage_fixture else None
    storage_end = "QA_STORAGE_END_" + edition.upper()
    control_tokens = ({"permission_prompt": "QA_PERMISSION_" + secrets.token_hex(16),
                       "cancel_prompt": "QA_CANCEL_" + secrets.token_hex(16)} if options.control_fixtures else {})
    if options.live_model:
        # The source config is read, never copied with its credentials into QA
        # evidence and never changed. Only isolated accounts/history are used.
        # RuntimeSession/AgentConsumer call the actual ask_rag implementation.
        from agent.rag import setup_llm
        from agent.config_loader import load_config
        model_config = load_config()
        model_name = str(model_config.get("unified_agent_model")
                         if model_config.get("enable_unified_agent")
                         else model_config.get("chained-model"))
        model_description = "actual configured model: " + model_name
        from agent import consumers
        actual_ask_rag = consumers.ask_rag

        def observed_live_model(chain, payload, chat_history=None, inet_enabled=False):
            owner = payload.get("conversation_user_id")
            if owner != qa_user_id or not payload.get("ask_execs_enabled"):
                raise RuntimeError("Live QA requires its isolated account and Ask-Execs enabled")
            if payload.get("input") in control_tokens.values():
                # Only the known QA tokens use deterministic no-effect work.
                # qa_model uses the actual broker and owned cancellation epoch.
                return qa_model(chain, payload, chat_history, inet_enabled)
            if storage_token is not None and payload.get("input") == storage_token:
                # Explicit storage-only fixture: never recorded as live inference.
                # The real consumer persists this, signs the actual content ticket
                # and exposes the real response-content ownership checks unchanged.
                response = ("<p>DETERMINISTIC STORAGE FIXTURE; NOT A MODEL ANSWER.</p>"
                            + "<p>Saved response integrity: áéíóú, ñ, 漢字, 😀 &amp; brackets.</p>" * 1600
                            + "<p>" + storage_end + "</p>")
                record("storage.fixture.returned", bytes=len(response.encode("utf-8")),
                       answer_digest=hashlib.sha256(response.encode("utf-8")).hexdigest())
                global_state.set_state(f"last_request_meta::{owner}", {
                    "multi_turn_used": True, "exec_report_enabled": True, "exec_report_entries": [],
                })
                return response
            record("model.live.started", model=model_name, user_id=owner,
                   input_digest=hashlib.sha256(str(payload.get("input", "")).encode("utf-8")).hexdigest())
            try:
                answer = actual_ask_rag(chain, payload, chat_history, inet_enabled)
            except BaseException as exc:
                record("model.live.failed", error_type=type(exc).__name__)
                raise
            record("model.live.returned", model=model_name,
                   answer_digest=hashlib.sha256(str(answer).encode("utf-8")).hexdigest())
            return answer

        consumers.ask_rag = observed_live_model
        chain = setup_llm(agents=[], mcps=[], tools=[], include_application_context=False)
        if chain is None:
            raise RuntimeError("The real configured RAG/model chain did not initialize")
        record("model.live.ready", model=model_name, chain_type=type(chain).__name__)
    else:
        chain = QaChain()
    global_state.set_state(f"rag_chain_{user.pk}", chain)
    global_state.set_state("rag_chain_ready", True)

    from django.http import FileResponse, HttpResponse, HttpResponseForbidden, JsonResponse
    from django.urls import path
    from django.views.decorators.csrf import csrf_exempt
    from agent import remote_controller_views as views
    from agent import remote_response_files

    class QaAccountMiddleware:
        """Only this QA process supplies a fixed, clearly labelled test identity."""
        def __init__(self, get_response):
            self.get_response = get_response

        def __call__(self, request):
            if request.META.get("REMOTE_ADDR") != "127.0.0.1":
                return HttpResponseForbidden("QA fixture accepts loopback only")
            request.user = User.objects.filter(pk=qa_user_id, username=username).first()
            if request.user is None:
                if request.path not in {"/", "/qa/", "/qa/status", "/qa/shutdown", "/qa/delete-account"}:
                    return HttpResponseForbidden("The isolated QA account has been deleted")
                request.user = AnonymousUser()
            return self.get_response(request)
    fixture_module.QaAccountMiddleware = QaAccountMiddleware
    shutdown_token = secrets.token_hex(32)
    server_box = {}

    def home(_request):
        deleted = not User.objects.filter(pk=qa_user_id, username=username).exists()
        return HttpResponse(
            "<!doctype html><meta charset='utf-8'><title>RC main QA fixture</title>"
            "<h1>Isolated Remote-Controller integration fixture</h1>"
            "<p>Actual account page, worker, chat consumer and history; "
            + html.escape(model_description) + ".</p>"
            + ("<p id='qa-account-deleted'>The isolated QA account has been deleted.</p>"
               if deleted else "<a href='/agent/remote-controller/'>Open Remote-Controller</a>"),
        )

    def status(_request):
        profile = RemoteControllerProfile.objects.filter(user=user).first()
        with audit_lock:
            events = list(audit)
        return JsonResponse({
            "fixture": True, "edition": edition, "username": username,
            "account_exists": User.objects.filter(pk=qa_user_id, username=username).exists(),
            "runtime": "actual AgentConsumer, RuntimeSession and AccountWorker",
            "model": model_description, "live_model": options.live_model,
            "profile": None if profile is None else {
                "account_id": profile.account.hex, "adapter_id": profile.adapter.hex,
                "binding_id": profile.binding_id, "enabled": profile.enabled,
            },
            "history": list(AgentMessage.objects.filter(conversation_user=user)
                            .order_by("pk").values("id", "user_id", "conversation_user_id", "message")),
            "other_account_history_count": AgentMessage.objects.filter(conversation_user=other).count(),
            "events": events,
        })

    @csrf_exempt
    def delete_account(request):
        if request.method != "POST":
            return HttpResponse("POST required", status=405)
        if not secrets.compare_digest(request.headers.get("X-RC-QA-Token", ""), shutdown_token):
            return HttpResponseForbidden("Unknown QA owner")
        if request.body:
            return HttpResponse("This fixed QA action accepts no account or other request body", status=400)
        # Only the identity created by this process may be removed, and only
        # after durable disable plus native unbinding have already completed.
        with bridge.enrollment_lock, transaction.atomic():
            current = RemoteControllerProfile.objects.filter(user_id=qa_user_id).first()
            if current and (current.enabled or current.binding_id):
                return JsonResponse({"error": "Disable and unbind the QA account first"}, status=409)
            User.objects.filter(pk=qa_user_id, username=username).delete()
        record("account.deleted", user_id=qa_user_id)
        return JsonResponse({"fixture": True, "deleted": True, "user_id": qa_user_id})

    def static_asset(_request, asset):
        if asset not in CSS_ALLOWLIST:
            return HttpResponse("Fixture asset not allowed", status=404)
        return FileResponse((options.edition_root / "Tlamatini/agent/static" / asset).open("rb"),
                            content_type="text/css; charset=utf-8")

    @csrf_exempt
    def shutdown(request):
        if request.method != "POST":
            return HttpResponse("POST required", status=405)
        if not secrets.compare_digest(request.headers.get("X-RC-QA-Token", ""), shutdown_token):
            return HttpResponseForbidden("Unknown QA owner")
        bridge._stop.set()
        threading.Thread(target=server_box["server"].shutdown, name="RcQaShutdown", daemon=True).start()
        return JsonResponse({"stopping": True, "fixture": True})

    fixture_module.urlpatterns = [
        path("", home, name="agent_page"), path("qa/", home),
        path("agent/remote-controller/", views.account, name="remote_controller"),
        path("agent/remote-controller/response-content/", remote_response_files.response_content,
             name="remote_response_content"),
        path("qa/status", status), path("qa/shutdown", shutdown),
        path("qa/delete-account", delete_account),
        path("static/<path:asset>", static_asset),
    ]
    from django.core.wsgi import get_wsgi_application

    class ThreadedServer(socketserver.ThreadingMixIn, WSGIServer):
        daemon_threads = True

    application = get_wsgi_application()
    server = make_server("127.0.0.1", options.port, application, server_class=ThreadedServer)
    server_box["server"] = server
    previous_threads = set(threading.enumerate())
    if not bridge.start_adapter():
        raise RuntimeError("The actual account adapter did not start")
    adapter_threads = [thread for thread in threading.enumerate()
                       if thread not in previous_threads and thread.name == "RemoteControllerAdapter"]
    metadata = {
        "fixture": True, "edition": edition, "pid": os.getpid(),
        "live_model": options.live_model, "model": model_description,
        "isolated_mcp_catalog": str(qa_catalog_path),
        "storage_fixture": None if storage_token is None else {"prompt": storage_token, "end_marker": storage_end},
        "control_fixtures": control_tokens,
        "origin": f"http://127.0.0.1:{options.port}",
        "account_url": f"http://127.0.0.1:{options.port}/agent/remote-controller/",
        "username": username, "password": QA_PASSWORD,
        "shutdown_token": shutdown_token, "database": str(options.output / "fixture.sqlite3"),
        "real_components": ["account view and template", "auth and profile models", "AccountWorker",
                            "RuntimeSession", "AgentConsumer", "DBChatHistoryLoader", "response_parser", "ExecPermissionBroker"],
        "fixture_boundaries": (["empty catalog and application context", "application startup hooks disabled"]
                               if options.live_model else ["model/RAG"]) + ["host speech disabled", "orphan reaping disabled",
                               "account wizard disabled", "Torrenter staging disabled", "fixed QA login"]
                              + (["exact-token deterministic large-response storage fixture"] if options.storage_fixture else [])
                              + (["exact-token deterministic no-effect approval/cancellation fixtures"] if options.control_fixtures else []),
    }
    with (options.output / "ready.json").open("x", encoding="utf-8") as stream:
        json.dump(metadata, stream, indent=2)
    print(f"[RC MAIN QA] READY {metadata['account_url']} — {edition}; isolated database; {model_description}", flush=True)
    try:
        server.serve_forever(poll_interval=0.2)
    finally:
        bridge._stop.set()
        server.server_close()
        for thread in adapter_threads:
            thread.join(timeout=20)
        survivors = [thread.name for thread in adapter_threads if thread.is_alive()]
        with audit_lock:
            final_audit = list(audit)
        (options.output / "fixture-events.json").write_text(json.dumps(final_audit, indent=2), encoding="utf-8")
        (options.output / "fixture-isolation.json").write_text(json.dumps({
            "isolated_mcp_catalog": str(qa_catalog_path), "denied_writes": denied_writes,
            "source_and_profile_write_guard": True,
        }, indent=2), encoding="utf-8")
        print("[RC MAIN QA] STOPPED; adapter threads remaining: " + str(survivors), flush=True)
        if denied_writes:
            raise RuntimeError("Imported source attempted a write outside the isolated fixture; see fixture-isolation.json")
        if survivors:
            raise RuntimeError("The isolated account adapter has not stopped")


if __name__ == "__main__":
    main()
