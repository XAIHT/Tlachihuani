"""Read-only carriage/parity checks; never imports or executes either build.py."""
import ast
import importlib.util
import json
from pathlib import Path
import sys

roots = [Path(value).resolve() for value in sys.argv[1:3]]
summary = []
modules = ("remote_controller_views", "remote_controller_bridge", "remote_controller_runtime", "remote_controller_guard")
for root in roots:
    spec = importlib.util.spec_from_file_location("rc_asset_checks", root / "build_runtime_assets.py")
    assets = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(assets)
    assets.validate_source_assets(root)
    assert "docs/remote-controller.md" in assets.ROOT_SOURCES
    assert "agent/css/remote_controller.css" in assets.REQUIRED_STATIC
    builder = ast.parse((root / "build.py").read_text(encoding="utf-8-sig"))
    frozen = next(ast.literal_eval(node.value) for node in builder.body if isinstance(node, ast.Assign)
                  and any(isinstance(target, ast.Name) and target.id == "_FROZEN_REMOTE_CONTROLLER_MODULES" for target in node.targets))
    for module in modules:
        assert "agent." + module in frozen, module
    assert "agent.migrations.0224_remote_controller" in frozen
    snapshot = ast.parse((root / "copy_source_assets.py").read_text(encoding="utf-8-sig"))
    required = next(ast.literal_eval(node.value) for node in snapshot.body if isinstance(node, ast.Assign)
                    and any(isinstance(target, ast.Name) and target.id == "REQUIRED_SNAPSHOT_FILES" for target in node.targets))
    for relative in [*(f"Tlamatini/agent/{module}.py" for module in modules),
                     "Tlamatini/agent/migrations/0224_remote_controller.py",
                     "Tlamatini/agent/templates/agent/remote_controller.html",
                     "Tlamatini/agent/static/agent/css/remote_controller.css", "docs/remote-controller.md"]:
        assert relative in required, relative
        assert (root / relative).is_file(), relative
    for relative in [*(f"Tlamatini/agent/{module}.py" for module in modules),
                     "Tlamatini/agent/models.py", "Tlamatini/agent/apps.py", "Tlamatini/agent/consumers.py",
                     "Tlamatini/agent/urls.py", "Tlamatini/agent/migrations/0224_remote_controller.py"]:
        ast.parse((root / relative).read_text(encoding="utf-8-sig"), filename=relative)
    summary.append({"edition": str(root), "carriage_and_syntax": True})
    print("PASS source carriage and syntax:", root, flush=True)

for name in ["remote_controller_runtime.py", "remote_controller_guard.py",
             "migrations/0224_remote_controller.py", "templates/agent/remote_controller.html",
             "static/agent/css/remote_controller.css"]:
    assert (roots[0] / "Tlamatini/agent" / name).read_bytes() == (roots[1] / "Tlamatini/agent" / name).read_bytes(), name
english = (roots[0] / "Tlamatini/agent/remote_controller_bridge.py").read_text(encoding="utf-8")
spanish = (roots[1] / "Tlamatini/agent/remote_controller_bridge.py").read_text(encoding="utf-8")
assert english.replace('EDITION = "english"', 'EDITION = "spanish"') == spanish
print("PASS deterministic runtime/adapter/migration/layout parity across editions", flush=True)
if len(sys.argv) > 3:
    Path(sys.argv[3]).write_text(json.dumps({"editions": summary, "parity": True}, indent=2), encoding="utf-8")
