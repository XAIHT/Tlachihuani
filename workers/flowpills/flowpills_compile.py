# Tlachihuani — Angela López Mendoza · XAIHT
"""Flow Pills compiler: turn a Tlamatini .flw into a runnable agent pool.

A faithful, standalone port of the write mode of Tlamatini's Flow Compiler
(agent/services/flow_spec.py, flow_compiler.py, agent_contracts.py,
agent_paths.py and flow_knowledge.write_runtime_knowledge). It imports no
Tlamatini module and no Django. It runs under Tlamatini's own Python (the same
interpreter that runs the agents), reads the agent templates from the agents
root, and writes one pool folder for one run.

The pool must live in a folder named ``pools``: the agents find their siblings
with ``get_pool_path()``, which recognises ``.../pools/<run>/<agent>``.

stdin:  {"flw": path, "agents_root": path, "pool": path}
stdout: exactly one JSON object.
"""
from __future__ import annotations

import csv
import importlib.util
import json
import re
import shutil
import sys
from copy import deepcopy
from pathlib import Path
from typing import Any

import yaml

REDACTED = "__REDACTED__"
DEFAULT_CONNECTION_FIELDS = (
    "source_agents", "target_agents", "output_agents", "source_agent", "target_agent",
    "source_agent_1", "source_agent_2", "target_agents_a", "target_agents_b",
    "target_agents_l", "target_agents_g",
)
SINGLE_FIELDS = {"source_agent", "target_agent", "source_agent_1", "source_agent_2"}
# The connection-slot exceptions of Tlamatini's built-in agent contracts. A
# built-in contract replaces the discovered one, so every built-in that keeps
# the default slots is listed too.
BUILTIN = {
    "starter": {}, "parametrizer": {}, "teletlamatini": {}, "telegrammer": {},
    "whatsapper": {}, "instant_messaging_doctor": {}, "kalier": {}, "discoverer": {},
    "apirer": {}, "sqler": {}, "mongoxer": {}, "jenkinser": {}, "emailer": {},
    "recmailer": {}, "kyber_decipher": {}, "esphomer": {}, "talker": {},
    "whisperer": {}, "image_interpreter": {}, "video_analyzer": {},
    "flowcreator": {"singleton": True},
    "flowhypervisor": {"singleton": True},
    "ender": {"output": {0: "output_agents"}},
    "stopper": {"output": {0: "output_agents"}},
    "cleaner": {"output": {0: "output_agents"}},
    "asker": {"output": {0: "target_agents_a", 1: "target_agents_a", 2: "target_agents_b"}},
    "forker": {"output": {0: "target_agents_a", 1: "target_agents_a", 2: "target_agents_b"}},
    "counter": {"output": {0: "target_agents_l", 1: "target_agents_l", 2: "target_agents_g"}},
    "and": {"input": {0: "source_agent_1", 1: "source_agent_1", 2: "source_agent_2"}},
    "or": {"input": {0: "source_agent_1", 1: "source_agent_1", 2: "source_agent_2"}},
}
PASSWORD_PATHS = {"emailer": ("smtp.password",), "recmailer": ("imap.password",)}
KNOWLEDGE_AGENTS = {"flowcreator", "flowhypervisor", "parametrizer"}
HELPERS = {
    "mouser": ("mouser_coordinates.py",), "keyboarder": ("keyboarder_input.py",),
    "flowcreator": ("result_to_flw.py",), "video_analyzer": ("video_content.py", "video_fetch.py"),
}
# Generated state never travels from a template into a fresh pool.
COPY_IGNORE = shutil.ignore_patterns("__pycache__", "*.pyc", "*.log", "agent.pid", "reanim*", "pools")


class CompileError(Exception):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


def normalize_agent_type(value: str) -> str:
    text = (value or "").strip()
    if not text:
        return ""
    text = re.sub(r"\s*\(\d+\)\s*$", "", text)
    text = text.replace("-", "_").replace(" ", "_")
    text = re.sub(r"[^A-Za-z0-9_]+", "_", text)
    return re.sub(r"_+", "_", text).strip("_").lower()


def _safe(value: str) -> bool:
    return bool(value) and not any(part in value for part in ("..", "/", "\\"))


def parse_canvas_agent_name(agent_name: str) -> tuple[str, str]:
    parts = [part for part in (agent_name or "").split("-") if part]
    cardinal = parts.pop() if parts and parts[-1].isdigit() else None
    base = normalize_agent_type("_".join(parts))
    pool = f"{base}_{cardinal}" if cardinal else base
    if not _safe(base) or not _safe(pool):
        raise ValueError("Invalid agent name")
    return base, pool


class Contract:
    def __init__(self, agent_type: str, output=None, input_=None, singleton=False):
        self.agent_type = agent_type
        self.output = output or {0: "target_agents"}
        self.input = input_ or {0: "source_agents"}
        self.singleton = singleton
        self.password_paths = PASSWORD_PATHS.get(agent_type, ())


class Catalog:
    def __init__(self, root: Path):
        self.root = root
        self.contracts: dict[str, Contract] = {}
        for item in sorted(root.iterdir()) if root.is_dir() else []:
            if not item.is_dir() or item.name.lower() in {"pools", "__pycache__"}:
                continue
            agent_type = normalize_agent_type(item.name)
            if not (item / f"{agent_type}.py").is_file() or not (item / "config.yaml").is_file():
                continue
            config = read_yaml(item / "config.yaml")
            output = {0: "output_agents"} if "output_agents" in config and "target_agents" not in config else None
            self.contracts[agent_type] = Contract(agent_type, output=output)
        for agent_type, spec in BUILTIN.items():
            self.contracts[agent_type] = Contract(
                agent_type, output=spec.get("output"), input_=spec.get("input"), singleton=spec.get("singleton", False))

    def contract(self, agent_type: str) -> Contract:
        return self.contracts.get(agent_type) or Contract(agent_type)


class Node:
    def __init__(self, raw: dict, node_id: str, text: str):
        self.id = node_id
        self.text = text
        config = raw.get("configData")
        if config is None:
            config = raw.get("config")
        self.config = dict(config) if isinstance(config, dict) else {}
        try:
            base, _ = parse_canvas_agent_name(self.id)
        except ValueError:
            base = normalize_agent_type(self.text)
        self.agent_type = normalize_agent_type(base)
        try:
            self.pool_name = parse_canvas_agent_name(self.id)[1]
        except ValueError as exc:
            raise CompileError("invalid_node", f"The node id {self.id!r} is not a valid Tlamatini agent name.") from exc


def _intish(value: Any, default: int = 0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def normalize_flow(payload: dict, catalog: Catalog):
    counters: dict[str, int] = {}
    nodes: list[Node] = []
    for raw in payload.get("nodes") or []:
        if not isinstance(raw, dict):
            continue
        text = str(raw.get("text") or raw.get("agentName") or raw.get("agent_name") or "")
        node_id = str(raw.get("id") or "")
        if not node_id:
            agent_type = normalize_agent_type(text)
            if catalog.contract(agent_type).singleton:
                node_id = agent_type.replace("_", "-")
            else:
                counters[agent_type] = counters.get(agent_type, 0) + 1
                node_id = f"{agent_type.replace('_', '-')}-{counters[agent_type]}"
        nodes.append(Node(raw, node_id, text or node_id))
    connections = []
    for raw in payload.get("connections") or []:
        if not isinstance(raw, dict):
            continue
        source_id, target_id = raw.get("sourceId"), raw.get("targetId")
        if source_id is None and raw.get("sourceIndex") is not None:
            index = _intish(raw.get("sourceIndex"), -1)
            if 0 <= index < len(nodes):
                source_id = nodes[index].id
        if target_id is None and raw.get("targetIndex") is not None:
            index = _intish(raw.get("targetIndex"), -1)
            if 0 <= index < len(nodes):
                target_id = nodes[index].id
        if not source_id or not target_id:
            continue
        connections.append((str(source_id), str(target_id), _intish(raw.get("inputSlot"), 0), _intish(raw.get("outputSlot"), 0)))
    artifacts = payload.get("artifacts") if isinstance(payload.get("artifacts"), dict) else {}
    return nodes, connections, artifacts


def read_yaml(path: Path) -> dict:
    if not path.exists():
        return {}
    try:
        with path.open("r", encoding="utf-8") as handle:
            data = yaml.safe_load(handle) or {}
        return data if isinstance(data, dict) else {}
    except Exception:
        return {}


def merge(base: dict, updates: dict, path: str, kept: list[str]) -> dict:
    """Tlamatini's deep merge, except that a value the .flw stores redacted keeps the
    installed template's own value instead of sending the placeholder to the agent."""
    result = deepcopy(base)
    for key, value in updates.items():
        dotted = f"{path}.{key}" if path else str(key)
        current = result.get(key)
        if isinstance(current, dict) and isinstance(value, dict):
            result[key] = merge(current, value, dotted, kept)
        elif isinstance(value, str) and value == REDACTED and current:
            kept.append(dotted)
        elif isinstance(value, str) and f"{REDACTED}@" in value and isinstance(current, str) and "@" in current:
            kept.append(dotted)
        else:
            result[key] = deepcopy(value)
    return result


def _add_unique(config: dict, key: str, value: str) -> None:
    target = config.get(key)
    if not isinstance(target, list):
        target = []
        config[key] = target
    if value and value not in target:
        target.append(value)


def _set_connection(config: dict, key: str, value: str) -> None:
    if key in SINGLE_FIELDS:
        if not config.get(key):
            config[key] = value
    else:
        _add_unique(config, key, value)


def _upstream(start: str, reverse: dict[str, list[str]]) -> list[str]:
    result, seen, stack = [], set(), [start]
    while stack:
        node_id = stack.pop()
        if node_id in seen:
            continue
        seen.add(node_id)
        result.append(node_id)
        stack.extend(reverse.get(node_id, []))
    return result


def compile_configs(nodes, connections, catalog: Catalog):
    warnings: list[str] = []
    kept: list[str] = []
    configs: dict[str, dict] = {}
    preserved: dict[str, dict] = {}
    for node in nodes:
        template = read_yaml(catalog.root / node.agent_type / "config.yaml")
        own = {key: value for key, value in node.config.items() if not str(key).startswith("_")}
        redacted: list[str] = []
        merged = merge(template, own, "", redacted)
        kept.extend(f"{node.pool_name} {dotted}" for dotted in redacted)
        preserved[node.id] = {field: deepcopy(merged[field]) for field in DEFAULT_CONNECTION_FIELDS if field in merged}
        configs[node.id] = merged
    by_id = {node.id: node for node in nodes}
    reverse: dict[str, list[str]] = {}
    for source_id, target_id, _, _ in connections:
        reverse.setdefault(target_id, []).append(source_id)
    for source_id, target_id, input_slot, output_slot in connections:
        source, target = by_id.get(source_id), by_id.get(target_id)
        if not source or not target:
            warnings.append(f"Skipped connection with missing endpoint: {source_id} -> {target_id}")
            continue
        source_contract, target_contract = catalog.contract(source.agent_type), catalog.contract(target.agent_type)
        output_field = source_contract.output.get(output_slot, source_contract.output.get(0, "target_agents"))
        if output_field:
            _set_connection(configs[source.id], output_field, target.pool_name)
        input_field = target_contract.input.get(input_slot, target_contract.input.get(0, "source_agents"))
        if input_field:
            _set_connection(configs[target.id], input_field, source.pool_name)
    for node in nodes:
        if node.agent_type != "ender":
            continue
        user_targets = preserved.get(node.id, {}).get("target_agents")
        if isinstance(user_targets, list) and user_targets:
            configs[node.id]["target_agents"] = user_targets
            continue
        incoming = [source_id for source_id, target_id, _, _ in connections if target_id == node.id]
        if not incoming:
            if isinstance(user_targets, list):
                configs[node.id]["target_agents"] = user_targets
            continue
        kill_list: list[str] = []
        for incoming_id in incoming:
            for upstream_id in _upstream(incoming_id, reverse):
                upstream = by_id.get(upstream_id)
                if not upstream or upstream.id == node.id or upstream.agent_type == "cleaner":
                    continue
                if upstream.pool_name not in kill_list:
                    kill_list.append(upstream.pool_name)
        configs[node.id]["target_agents"] = kill_list
    for node in nodes:
        if node.agent_type != "parametrizer":
            continue
        config = configs[node.id]
        sources, targets = config.get("source_agents"), config.get("target_agents")
        if isinstance(sources, list) and len(sources) == 1:
            config["source_agent"] = sources[0]
        if isinstance(targets, list) and len(targets) == 1:
            config["target_agent"] = targets[0]
        if isinstance(sources, list) and len(sources) > 1:
            warnings.append(f"{node.pool_name} has {len(sources)} sources; Parametrizer expects exactly one.")
        if isinstance(targets, list) and len(targets) > 1:
            warnings.append(f"{node.pool_name} has {len(targets)} targets; Parametrizer expects exactly one.")
    for node in nodes:
        if node.agent_type == "monitor_log":
            target = configs[node.id].setdefault("target", {})
            if isinstance(target, dict):
                target["logfile_path"] = f"{node.pool_name}.log"
    return configs, warnings, kept


class _QuotedStr(str):
    """A value written with double quotes (passwords), exactly like Tlamatini."""


class _Dumper(yaml.SafeDumper):
    """Per-call dumper: the representers never leak into PyYAML's global table."""


def _str_representer(dumper, value):
    if "\n" in value:
        if not value.endswith("\n"):
            value = value + "\n"
        return dumper.represent_scalar("tag:yaml.org,2002:str", value, style="|")
    return dumper.represent_scalar("tag:yaml.org,2002:str", value)


def _quoted_representer(dumper, value):
    return dumper.represent_scalar("tag:yaml.org,2002:str", str(value), style='"')


_Dumper.add_representer(str, _str_representer)
_Dumper.add_representer(_QuotedStr, _quoted_representer)


def dump_config(config: dict, path: Path, password_paths: tuple[str, ...]) -> None:
    payload = deepcopy(config)
    for dotted in password_paths:
        parts = [part for part in dotted.split(".") if part]
        parent: Any = payload
        for part in parts[:-1]:
            parent = parent.get(part) if isinstance(parent, dict) else None
        if parts and isinstance(parent, dict) and parts[-1] in parent:
            current = parent[parts[-1]]
            parent[parts[-1]] = _QuotedStr("" if current is None else str(current))
    with path.open("w", encoding="utf-8") as handle:
        yaml.dump(payload, handle, Dumper=_Dumper, default_flow_style=False, allow_unicode=True, sort_keys=False)


def model_agents(root: Path) -> frozenset[str] | None:
    try:
        spec = importlib.util.spec_from_file_location("flowpills_model_settings", root / "model_settings.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return frozenset(module.AGENTS)
    except Exception:
        return None


def _copy(source: Path, target: Path) -> None:
    if source.is_file() and source.resolve() != target.resolve():
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)


def write_runtime_knowledge(root: Path, destination: Path, agent_type: str, models: frozenset[str] | None) -> None:
    # Unknown model registry: carry the helper everywhere rather than starving a model agent.
    if models is None or agent_type in models:
        _copy(root / "model_settings.py", destination / "model_settings.py")
    if agent_type in {"image_interpreter", "video_analyzer"}:
        _copy(root / "visual_errors.py", destination / "visual_errors.py")
    for filename in HELPERS.get(agent_type, ()):
        _copy(root / agent_type / filename, destination / filename)
    if agent_type in KNOWLEDGE_AGENTS:
        _copy(root / "flowcreator" / "flow_knowledge.py", destination / "flow_knowledge.py")
        _copy(root / "flowcreator" / "flow_catalog.json", destination / "flow_catalog.json")


def parametrizer_mappings(node: Node, artifacts: dict) -> list:
    mappings = node.config.get("_parametrizer_mappings")
    if isinstance(mappings, list):
        return mappings
    for key in ("parametrizerMappings", "parametrizer_mappings", "parametrizerSchemes", "parametrizer_schemes"):
        candidate = artifacts.get(key)
        if isinstance(candidate, dict):
            value = candidate.get(node.id) or candidate.get(node.pool_name)
            if isinstance(value, list):
                return value
    return []


def write_scheme(pool_dir: Path, mappings: list) -> int:
    saved = 0
    with (pool_dir / "interconnection-scheme.csv").open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=["source_field", "target_param", "target_marker"])
        writer.writeheader()
        for mapping in mappings:
            if not isinstance(mapping, dict):
                continue
            source_field = str(mapping.get("source_field", "")).strip()
            target_param = str(mapping.get("target_param", "")).strip()
            target_marker = str(mapping.get("target_marker", "")).strip().strip("{}").strip()
            if not source_field or not target_param:
                continue
            writer.writerow({"source_field": source_field, "target_param": target_param, "target_marker": target_marker})
            saved += 1
    return saved


def compile_flow(request: dict) -> dict:
    flw_path = Path(str(request.get("flw") or ""))
    root = Path(str(request.get("agents_root") or ""))
    pool = Path(str(request.get("pool") or ""))
    if pool.parent.name.lower() != "pools":
        raise CompileError("invalid_pool", "The pool must be inside a folder named pools.")
    if not root.is_dir():
        raise CompileError("agents_missing", f"The Tlamatini agents folder does not exist: {root}")
    try:
        payload = json.loads(flw_path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as exc:
        raise CompileError("invalid_flw", f"The .flw file could not be read as JSON: {exc}") from exc
    if not isinstance(payload, dict) or not isinstance(payload.get("nodes"), list):
        raise CompileError("invalid_flw", "This is not a Tlamatini .flw document (it has no nodes list).")
    catalog = Catalog(root)
    nodes, connections, artifacts = normalize_flow(payload, catalog)
    if not nodes:
        raise CompileError("empty_flow", "The flow has no agents.")
    seen: set[str] = set()
    for node in nodes:
        if node.pool_name in seen:
            raise CompileError("duplicate_node", f"Two nodes would share the pool folder {node.pool_name}.")
        seen.add(node.pool_name)
        if not (root / node.agent_type / f"{node.agent_type}.py").is_file():
            raise CompileError("agent_missing", f"Tlamatini has no {node.agent_type!r} agent template in {root}.")
    starters = [node.pool_name for node in nodes if node.agent_type == "starter"]
    if not starters:
        raise CompileError("no_starter", "The flow has no Starter, so Tlamatini would not start it either.")
    configs, warnings, kept = compile_configs(nodes, connections, catalog)
    pool.mkdir(parents=True, exist_ok=True)
    models = model_agents(root)
    if models is None:
        warnings.append("model_settings.py could not be read; it was copied into every agent folder.")
    result_nodes = []
    for node in nodes:
        source_dir = root / node.agent_type
        pool_dir = pool / node.pool_name
        if not pool_dir.exists():
            shutil.copytree(source_dir, pool_dir, ignore=COPY_IGNORE)
        _copy(source_dir / f"{node.agent_type}.py", pool_dir / f"{node.agent_type}.py")
        write_runtime_knowledge(root, pool_dir, node.agent_type, models)
        dump_config(configs[node.id], pool_dir / "config.yaml", catalog.contract(node.agent_type).password_paths)
        if node.agent_type == "parametrizer":
            count = write_scheme(pool_dir, parametrizer_mappings(node, artifacts))
            if count:
                warnings.append(f"Saved {count} Parametrizer mapping(s) for {node.pool_name}.")
        result_nodes.append({
            "id": node.id, "pool_name": node.pool_name, "agent_type": node.agent_type,
            "dir": str(pool_dir), "script": str(pool_dir / f"{node.agent_type}.py"),
        })
    hypervisors = [node.pool_name for node in nodes if node.agent_type == "flowhypervisor"]
    return {
        "ok": True, "pool": str(pool), "nodes": result_nodes, "starters": starters,
        "hypervisors": hypervisors, "warnings": warnings, "kept_template_values": kept,
    }


def main() -> int:
    try:
        request = json.loads(sys.stdin.read() or "{}")
        if not isinstance(request, dict):
            raise CompileError("invalid_request", "The compile request must be a JSON object.")
        result = compile_flow(request)
    except CompileError as exc:
        result = {"ok": False, "code": exc.code, "error": str(exc)}
    except Exception as exc:  # noqa: BLE001 - one honest answer, never a traceback on stdout
        result = {"ok": False, "code": "compile_failed", "error": f"{type(exc).__name__}: {exc}"}
    # ASCII only: under -I the pipe's encoding is the ANSI code page, and a path such as
    # C:\Users\López must reach Tlachihuani exactly.
    sys.stdout.buffer.write(json.dumps(result, ensure_ascii=True).encode("ascii"))
    sys.stdout.buffer.flush()
    return 0 if result.get("ok") else 1


if __name__ == "__main__":
    sys.exit(main())
