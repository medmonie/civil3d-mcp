"""Add or remove the "civil3d" server in the MCP clients on this PC (install.bat / uninstall.bat).

    c3d-mcp-server.exe --configure-clients <path of the installed exe>
    c3d-mcp-server.exe --remove-clients
    server.py --configure-clients                 entry for this python + server.py (development)

Done in Python rather than PowerShell: company Group Policy often blocks PowerShell scripts. Every file is backed up,
merged (other servers such as sap2000 and revit, and other settings, are kept), written, then read back to confirm.
An existing civil3d entry keeps its env and the --read-only flag; other args (e.g. a server.py path left from a
source install, which the exe refuses) are removed. Codex also gets
env C3D_MCP_SPLIT = "1" (separate civil3d_read / civil3d_edit tools) unless the entry already sets it.
Exit code 0 when every client found was handled.

Claude Desktop reads its settings from one of:
  %LOCALAPPDATA%\\Claude-Data\\claude_*\\claude_desktop_config.json            Claude Desktop 6.x (the one it reads now)
  %APPDATA%\\Claude\\claude_desktop_config.json                               classic installer (older versions)
  %LOCALAPPDATA%\\Packages\\Claude_*\\LocalCache\\Roaming\\Claude\\...            Microsoft Store app (older versions)
All that exist are updated, so the entry is found whichever version runs. Codex: ~/.codex/config.toml.
Claude Desktop keeps its settings in memory and may write them back over this change: install.bat stops while it runs.
"""
from __future__ import annotations

import json
import os
import uuid
from pathlib import Path
from typing import Any

KEY = "civil3d"
# Arguments the packaged exe understands and an existing entry may keep. Anything else (e.g. the path of server.py left
# from a source install) would make the exe exit with "unrecognized arguments", so it is dropped.
KEEP_ARGS = ("--read-only",)


def _merge_args(previous: object, entry: dict[str, Any]) -> list[str] | None:
    """The args of the new entry: the entry's own when it has some (a source install), else the kept flags."""
    if entry.get("args") is not None:
        return list(entry["args"])
    old = previous if isinstance(previous, list) else []
    kept = [a for a in old if isinstance(a, str) and a in KEEP_ARGS]
    return kept or None


def _env(name: str) -> Path | None:
    value = os.environ.get(name)
    return Path(value) if value else None


def claude_files() -> list[Path]:
    appdata, local = _env("APPDATA"), _env("LOCALAPPDATA")
    files: list[Path] = []
    data = local / "Claude-Data" if local else None
    if data and data.is_dir():
        for d in sorted(data.glob("claude_*")):
            if d.is_dir():
                files.append(d / "claude_desktop_config.json")
    packages = sorted((local / "Packages").glob("Claude_*")) if local and (local / "Packages").is_dir() else []
    installed = bool(packages) or bool(local and (local / "AnthropicClaude").is_dir()) or bool(files)
    if appdata and ((appdata / "Claude").is_dir() or installed):
        files.append(appdata / "Claude" / "claude_desktop_config.json")
    for package in packages:
        store = package / "LocalCache" / "Roaming" / "Claude"
        if store.is_dir():
            files.append(store / "claude_desktop_config.json")
    return files


def codex_file() -> Path:
    return (_env("CODEX_HOME") or ((_env("USERPROFILE") or Path.home()) / ".codex")) / "config.toml"


def _backup(path: Path) -> None:
    if path.exists():
        path.with_name(path.name + ".bak-" + uuid.uuid4().hex[:8]).write_bytes(path.read_bytes())


def _write(path: Path, text: str) -> None:
    """Write next to the target, then replace it in one step (a crash never leaves half a file)."""
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp-" + uuid.uuid4().hex[:8])
    try:
        tmp.write_text(text, encoding="utf-8")
        os.replace(tmp, path)
    finally:
        if tmp.exists():
            tmp.unlink()


def _claude(path: Path, entry: dict[str, Any] | None) -> str:
    text = path.read_text(encoding="utf-8-sig") if path.exists() else ""
    try:
        config = json.loads(text) if text.strip() else {}
    except ValueError as exc:
        raise RuntimeError(f"not valid JSON ({exc}); fix it or add the entry by hand") from None
    if not isinstance(config, dict):
        raise RuntimeError("the file is not a JSON object; add the entry by hand")
    servers = config.get("mcpServers", {})
    if not isinstance(servers, dict):
        raise RuntimeError('"mcpServers" is not an object; add the entry by hand')
    if entry is None:
        if KEY not in servers:
            return f"no {KEY} entry"
        _backup(path)
        del servers[KEY]
    else:
        previous = servers.get(KEY, {})
        if not isinstance(previous, dict):
            raise RuntimeError(f'the existing "{KEY}" entry is not an object; fix it by hand')
        updated = {**previous, **entry}          # env and other keys of an existing entry are kept
        args = _merge_args(previous.get("args"), entry)
        if args is None:
            updated.pop("args", None)
        else:
            updated["args"] = args
        if KEY in servers and updated == previous:
            return "already configured"
        _backup(path)
        servers[KEY] = updated
    config["mcpServers"] = servers
    _write(path, json.dumps(config, indent=2, ensure_ascii=False) + "\n")
    check = json.loads(path.read_text(encoding="utf-8")).get("mcpServers", {})
    if check.get(KEY) != servers.get(KEY):
        raise RuntimeError("written but not found when read back")
    return "removed" if entry is None else ("updated" if previous else "added")


def _codex(path: Path, entry: dict[str, Any] | None) -> str:
    import tomlkit
    original = path.read_text(encoding="utf-8-sig")
    doc = tomlkit.parse(original)
    servers = doc.get("mcp_servers")
    if entry is None:
        if servers is None or KEY not in servers:
            return f"no {KEY} entry"
        del servers[KEY]
    else:
        if servers is None:
            servers = tomlkit.table(is_super_table=True)
            doc["mcp_servers"] = servers
        target = servers.get(KEY)
        if target is None:
            target = tomlkit.table()
            servers[KEY] = target
        target["command"] = entry["command"]     # env and sub-tables of an existing entry are kept
        args = _merge_args(target.get("args"), entry)
        if args is None:
            if "args" in target:
                del target["args"]
        elif list(target.get("args") or []) != args:
            target["args"] = args
        # Codex approves per tool: separate civil3d_read / civil3d_edit tools let reads be allowed without edits
        # (same as the CSI servers' CSI_MCP_SPLIT). An existing value is kept.
        env = target.get("env")
        if env is None:
            env = tomlkit.table()
            target["env"] = env
        if "C3D_MCP_SPLIT" not in env:
            env["C3D_MCP_SPLIT"] = "1"
    revised = tomlkit.dumps(doc)
    if revised == original:
        return "already configured"
    tomlkit.parse(revised)                         # must still be valid TOML
    _backup(path)
    _write(path, revised)
    return "removed" if entry is None else "configured"


def run(entry: dict[str, Any] | None) -> int:
    """entry: {"command": ..., "args"?: [...]} to add, or None to remove. Prints one line per client."""
    failed = 0
    files = claude_files()
    if not files:
        print("  Claude Desktop: not installed for this user (no Claude folder or app found)")
    for path in files:
        try:
            print(f"  Claude Desktop: {_claude(path, entry)}: {path}")
        except Exception as exc:  # noqa: BLE001 - report every client, then fail at the end
            failed += 1
            print(f"  Claude Desktop: FAILED: {path}: {exc}")
    codex = codex_file()
    if codex.exists():
        try:
            print(f"  Codex: {_codex(codex, entry)}: {codex}")
        except Exception as exc:  # noqa: BLE001
            failed += 1
            print(f"  Codex: FAILED: {codex}: {exc}")
    elif entry is not None:
        print(f"  Codex: not installed for this user (no {codex})")
    if failed and entry is not None:
        print("  Add the entry by hand: Claude Desktop > Settings > Developer > Edit Config, inside \"mcpServers\": "
              + json.dumps({KEY: entry}))
    return 1 if failed else 0
