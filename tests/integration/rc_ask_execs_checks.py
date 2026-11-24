"""Run real executor-gate regressions with isolated, application-owned scratch."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import sys
import unittest

from rc_main_fixture import protect_fixture_sources

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("edition", type=Path)
parser.add_argument("output", type=Path)
parser.add_argument("--visible-development", action="store_true")
options = parser.parse_args()
if not options.visible_development:
    parser.error("Run from the parent's verified visible console")
output = options.output.resolve()
output.relative_to(Path(__file__).resolve().parents[2] / "artifacts")
output.mkdir(parents=True, exist_ok=False)
scratch = output / "scratch"
scratch.mkdir()
for key in ("TEMP", "TMP", "TMPDIR", "TLAMATINI_TEMP", "TLAMATINI_TEST_SCRATCH"):
    os.environ[key] = str(scratch)
os.environ["PYTHONDONTWRITEBYTECODE"] = "1"
sys.dont_write_bytecode = True
sys.path.insert(0, str(options.edition.resolve() / "Tlamatini"))
from django.apps import AppConfig
from django.conf import settings

class IsolatedAgentConfig(AppConfig):
    name = "agent"
    path = str(options.edition.resolve() / "Tlamatini/agent")

settings.configure(SECRET_KEY="isolated-ask-execs-check", USE_TZ=True,
                   INSTALLED_APPS=["django.contrib.auth", "django.contrib.contenttypes", "__main__.IsolatedAgentConfig"],
                   DATABASES={"default": {"ENGINE": "django.db.backends.sqlite3", "NAME": str(output / "unused.sqlite3")}},
                   DEFAULT_AUTO_FIELD="django.db.models.BigAutoField")
from agent import external_mcp_manager
catalog = output / "external_mcps.json"
catalog.write_text('{"mcpServers": {}, "active": []}\n', encoding="utf-8")
external_mcp_manager.catalog_path = lambda: str(catalog)
denied = protect_fixture_sources(options.edition.resolve())
import django
django.setup()
suite = unittest.defaultTestLoader.loadTestsFromName("agent.test_remote_ask_execs_dispatch")
result = unittest.TextTestRunner(verbosity=2).run(suite)
report = {"tests": result.testsRun, "failures": len(result.failures), "errors": len(result.errors),
          "skipped": len(result.skipped), "passed": result.wasSuccessful() and not denied,
          "source_write_attempts": denied, "scope": "Real executor and broker; deterministic model and inert tool bodies"}
(output / "result.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report), flush=True)
raise SystemExit(0 if report["passed"] else 1)
