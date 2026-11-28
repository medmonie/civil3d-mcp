"""Offline test of server/client_config.py (install.bat --configure-clients / uninstall.bat --remove-clients).
Runs on any OS with sample profile folders in a temp dir: Claude Desktop 6.x (Claude-Data), classic, Store, and Codex."""
from __future__ import annotations

import json
import os
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "server"))
import client_config  # noqa: E402

FAILS: list[str] = []


def check(name: str, ok: bool, detail: str = "") -> None:
    print(("PASS  " if ok else "FAIL  ") + name + (f"  {detail}" if detail and not ok else ""))
    if not ok:
        FAILS.append(name)


with tempfile.TemporaryDirectory() as root:
    r = Path(root)
    appdata, local, home = r / "Roaming", r / "Local", r / "home"
    v6 = local / "Claude-Data" / "claude_abc123"
    store = local / "Packages" / "Claude_xyz" / "LocalCache" / "Roaming" / "Claude"
    classic = appdata / "Claude"
    for d in (v6, store, classic, home / ".codex"):
        d.mkdir(parents=True)
    os.environ.update(APPDATA=str(appdata), LOCALAPPDATA=str(local), USERPROFILE=str(home))
    os.environ.pop("CODEX_HOME", None)
    # 6.x file holds other settings and another server; classic has an old civil3d entry with --read-only; store empty
    (v6 / "claude_desktop_config.json").write_text(json.dumps(
        {"preferences": {"x": 1}, "mcpServers": {"sap2000": {"command": "C:\\\\s.exe"}}}), encoding="utf-8")
    (classic / "claude_desktop_config.json").write_text(json.dumps(
        {"mcpServers": {"civil3d": {"command": "old.exe", "args": ["--read-only"], "env": {"A": "1"}}}}), encoding="utf-8-sig")
    # Store file: a source-install entry (python + server.py), as found on the laptop on 28 Nov 2026
    (store / "claude_desktop_config.json").write_text(json.dumps(
        {"mcpServers": {"civil3d": {"command": "C:\\venv\\python.exe", "args": ["C:\\civil3d-mcp\\server\\server.py"]}}}), encoding="utf-8")
    (home / ".codex" / "config.toml").write_text(
        'model = "x"\n\n[mcp_servers.civil3d]\ncommand = "old.exe"\nargs = ["--read-only"]\n\n[mcp_servers.civil3d.env]\nA = "1"\n\n'
        '[mcp_servers.revit]\ncommand = "r.exe"\n', encoding="utf-8")

    files = client_config.claude_files()
    check("finds the 6.x, classic and Store config files", len(files) == 3 and files[0].parent == v6, str(files))
    exe = r"C:\Users\u\AppData\Local\C3dMcpBridge\c3d-mcp-server.exe"
    rc = client_config.run({"command": exe})
    check("configure exit 0", rc == 0)
    a = json.loads((v6 / "claude_desktop_config.json").read_text(encoding="utf-8"))
    check("6.x: civil3d added, other server and settings kept",
          a["mcpServers"].get("civil3d") == {"command": exe} and "sap2000" in a["mcpServers"] and a["preferences"] == {"x": 1}, str(a))
    b = json.loads((classic / "claude_desktop_config.json").read_text(encoding="utf-8"))
    check("classic: command updated, args and env kept",
          b["mcpServers"]["civil3d"] == {"command": exe, "args": ["--read-only"], "env": {"A": "1"}}, str(b))
    c = json.loads((store / "claude_desktop_config.json").read_text(encoding="utf-8"))
    check("Store: server.py argument of a source install removed", c["mcpServers"].get("civil3d") == {"command": exe}, str(c))
    check("backups written", any(p.name.startswith("claude_desktop_config.json.bak-") for p in v6.iterdir()))
    import tomlkit
    t = tomlkit.parse((home / ".codex" / "config.toml").read_text(encoding="utf-8")).unwrap()
    check("Codex: command updated, args / env / other server kept",
          t["mcp_servers"]["civil3d"] == {"command": exe, "args": ["--read-only"], "env": {"A": "1", "C3D_MCP_SPLIT": "1"}}
          and t["mcp_servers"]["revit"] == {"command": "r.exe"} and t["model"] == "x", str(t))
    # the broken state itself: exe command already set, stale server.py argument still there
    (store / "claude_desktop_config.json").write_text(json.dumps(
        {"mcpServers": {"civil3d": {"command": exe, "args": ["C:\\civil3d-mcp\\server\\server.py"]}}}), encoding="utf-8")
    client_config.run({"command": exe})
    c = json.loads((store / "claude_desktop_config.json").read_text(encoding="utf-8"))
    check("exe + stale server.py argument repaired (not 'already configured')", c["mcpServers"]["civil3d"] == {"command": exe}, str(c))
    before = {p: p.read_text(encoding="utf-8") for p in files}
    rc = client_config.run({"command": exe})
    check("second run changes nothing", rc == 0 and all(p.read_text(encoding="utf-8") == before[p] for p in files))
    (store / "claude_desktop_config.json").write_text("{ not json", encoding="utf-8")
    rc = client_config.run({"command": exe})
    check("invalid JSON: reported, exit 1, file untouched",
          rc == 1 and (store / "claude_desktop_config.json").read_text(encoding="utf-8") == "{ not json")
    (store / "claude_desktop_config.json").write_text("{}", encoding="utf-8")
    rc = client_config.run(None)
    a = json.loads((v6 / "claude_desktop_config.json").read_text(encoding="utf-8"))
    t = tomlkit.parse((home / ".codex" / "config.toml").read_text(encoding="utf-8")).unwrap()
    check("remove: civil3d gone, others kept", rc == 0 and "civil3d" not in a["mcpServers"] and "sap2000" in a["mcpServers"]
          and "civil3d" not in t["mcp_servers"] and "revit" in t["mcp_servers"], f"{a} {t}")

    # no Codex file and an empty profile
    os.environ.update(USERPROFILE=str(r / "nobody"), APPDATA=str(r / "noapp"), LOCALAPPDATA=str(r / "nolocal"))
    check("nothing installed: no files, exit 0", client_config.claude_files() == [] and client_config.run({"command": exe}) == 0)

print("ALL PASS" if not FAILS else "FAILED: " + "; ".join(FAILS))
sys.exit(1 if FAILS else 0)
