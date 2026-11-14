"""Isolated main-edition checks. No production AppConfig, database, or LLM boot."""
import importlib
import json
from pathlib import Path
import sys
import types
import unittest
import uuid

edition_root = Path(sys.argv[1]).resolve()
report = Path(sys.argv[2]).resolve()
# Scratch is always owned by this checkout, never TEMP, AppData or a profile.
scratch = Path(__file__).resolve().parents[2] / "artifacts" / "rc-implementation" / ("main-schema-" + uuid.uuid4().hex)
scratch.mkdir(parents=True, exist_ok=False)
isolated_database = scratch / "accounts.sqlite3"
sys.path.insert(0, str(edition_root / "Tlamatini"))
from django.apps import AppConfig
from django.conf import settings

class IsolatedAgentConfig(AppConfig):
    name = "agent"
    path = str(edition_root / "Tlamatini/agent")

settings.configure(
    SECRET_KEY="isolated-remote-controller-acceptance",
    INSTALLED_APPS=["django.contrib.auth", "django.contrib.contenttypes", "__main__.IsolatedAgentConfig"],
    DATABASES={"default": {"ENGINE": "django.db.backends.sqlite3", "NAME": str(isolated_database)}},
    USE_TZ=True, STATIC_URL="/static/", ROOT_URLCONF="rc_isolated_urls",
    TEMPLATES=[{"BACKEND": "django.template.backends.django.DjangoTemplates", "APP_DIRS": True,
                "OPTIONS": {"context_processors": ["django.template.context_processors.request"]}}],
    DEFAULT_AUTO_FIELD="django.db.models.BigAutoField",
)
import django
django.setup()
from django.http import HttpResponse
from django.urls import path
urls = types.ModuleType("rc_isolated_urls")
urls.urlpatterns = [path("", lambda request: HttpResponse("isolated"), name="agent_page"),
                    path("remote-controller/", lambda request: HttpResponse("isolated"), name="remote_controller")]
sys.modules[urls.__name__] = urls

suite = unittest.TestSuite()
for name in ["agent.test_remote_controller_runtime", "agent.test_remote_controller_views", "agent.test_remote_controller_bridge", "agent.test_remote_mobile_views"]:
    suite.addTests(unittest.defaultTestLoader.loadTestsFromName(name))
suite.addTests(unittest.defaultTestLoader.loadTestsFromName("agent.test_prompt_shape_sentences.PromptShapeSentenceTests"))
suite.addTests(unittest.defaultTestLoader.loadTestsFromName("rc_account_schema_checks"))
suite.addTests(unittest.defaultTestLoader.loadTestsFromName("rc_response_source_checks"))
result = unittest.TextTestRunner(verbosity=2).run(suite)
report.parent.mkdir(parents=True, exist_ok=True)
report.write_text(json.dumps({"edition": str(edition_root), "tests": result.testsRun,
    "failures": len(result.failures), "errors": len(result.errors), "skipped": len(result.skipped),
    "passed": result.wasSuccessful(), "isolated_database": str(isolated_database)}, indent=2), encoding="utf-8")
raise SystemExit(0 if result.wasSuccessful() else 1)
