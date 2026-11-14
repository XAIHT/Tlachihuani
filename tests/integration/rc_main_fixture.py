# Tlachihuani / Tlamatini — Created for Angela López Mendoza · XAIHT
"""Visible-only, isolated main-application integration host for RC acceptance.

This harness renders the real edition's account page, persists real auth/profile
and chat models, runs its actual AccountWorker and RuntimeSession, and imports
the actual AgentConsumer and reply/history code. Only the model boundary is
deterministic. Host speech, process reaping, account-wizard execution and Torrenter
staging are suppressed explicitly inside this QA process.

It does NOT certify real model inference, an installed main application, or the
normal application startup. Never point it at a production database or launch it
headlessly. Its parent must verify the visible foreground console first.
"""
from __future__ import annotations

import argparse
import asyncio
import hashlib
import html
import importlib
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


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--edition-root", required=True, type=Path)
    parser.add_argument("--port", required=True, type=int)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--rc-image", required=True, type=Path)
    parser.add_argument("--dev-nonce", required=True)
    parser.add_argument("--dev-pid", required=True, type=int)
    parser.add_argument("--visible-development", action="store_true")
    result = parser.parse_args()
    if not result.visible_development:
        parser.error("Verify a visible foreground development console, then pass --visible-development")
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
    options.output.mkdir(parents=True, exist_ok=True)
    scratch = options.output / "scratch"
    scratch.mkdir()
    for key in ("TEMP", "TMP", "TMPDIR", "TLAMATINI_TEMP"):
        os.environ[key] = str(scratch)
    os.environ["PYTHONDONTWRITEBYTECODE"] = "1"
    sys.dont_write_bytecode = True
    # This process never imports the product settings/manage.py or calls its
    # startup hooks. Every database access below resolves to this fresh file.
    os.environ.pop("DJANGO_SETTINGS_MODULE", None)
    sys.path.insert(0, str(options.edition_root / "Tlamatini"))
    logging.basicConfig(level=logging.INFO, format="[RC MAIN QA] %(levelname)s %(message)s")

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

    # Replace the RAG/model boundary before importing the real consumer. Its
    # normal save-message, history, broker, cancellation and response-parser
    # paths remain intact. No model client, tool executor or agent is started.
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
    global_state.set_state(f"rag_chain_{user.pk}", QaChain())
    global_state.set_state("rag_chain_ready", True)

    from django.http import FileResponse, HttpResponse, HttpResponseForbidden, JsonResponse
    from django.urls import path
    from django.views.decorators.csrf import csrf_exempt
    from agent import remote_controller_views as views

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
            "<p>Actual account page, worker, chat consumer and history; deterministic model fixture.</p>"
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
            "model": "deterministic QA replacement; no real inference",
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
        "origin": f"http://127.0.0.1:{options.port}",
        "account_url": f"http://127.0.0.1:{options.port}/agent/remote-controller/",
        "username": username, "password": QA_PASSWORD,
        "shutdown_token": shutdown_token, "database": str(options.output / "fixture.sqlite3"),
        "real_components": ["account view and template", "auth and profile models", "AccountWorker",
                            "RuntimeSession", "AgentConsumer", "DBChatHistoryLoader", "response_parser", "ExecPermissionBroker"],
        "fixture_boundaries": ["model/RAG", "host speech disabled", "orphan reaping disabled",
                               "account wizard disabled", "Torrenter staging disabled", "fixed QA login"],
    }
    with (options.output / "ready.json").open("x", encoding="utf-8") as stream:
        json.dump(metadata, stream, indent=2)
    print(f"[RC MAIN QA] READY {metadata['account_url']} — {edition}; isolated database; deterministic model", flush=True)
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
        print("[RC MAIN QA] STOPPED; adapter threads remaining: " + str(survivors), flush=True)
        if survivors:
            raise RuntimeError("The isolated account adapter has not stopped")


if __name__ == "__main__":
    main()
