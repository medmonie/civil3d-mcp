"""Launch OpenAI's Secure MCP Tunnel for this server (ChatGPT in a browser or cloud chat), read-only.

    c3d-mcp-server.exe --run-chatgpt-tunnel
    server\\.venv\\Scripts\\python server\\server.py --run-chatgpt-tunnel

Needs OpenAI's tunnel-client on PATH and CONTROL_PLANE_TUNNEL_ID / CONTROL_PLANE_API_KEY set in the launching shell.
The server runs with --read-only: ChatGPT gets civil3d_read only (same launch as the CSI MCP servers).
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path


def run() -> int:
    client = shutil.which("tunnel-client")
    if not client:
        print("Install OpenAI's tunnel-client CLI and add it to PATH: see 'ChatGPT in a browser or cloud chat' in "
              "README.md.", file=sys.stderr)
        return 2
    for name in ("CONTROL_PLANE_TUNNEL_ID", "CONTROL_PLANE_API_KEY"):
        if not os.environ.get(name):
            print(f"Set {name} in the launching shell.", file=sys.stderr)
            return 2
    if getattr(sys, "frozen", False):
        exe = Path(sys.executable)
        folder, command = exe.parent, "./" + exe.name
    else:
        folder = Path(__file__).resolve().parent
        if not (folder / ".venv/Scripts/python.exe").is_file():
            print("Run setup_server.bat first to create server\\.venv.", file=sys.stderr)
            return 2
        command = "./.venv/Scripts/python.exe server.py"
    # A relative command is resolved in the working directory, so paths with spaces need no quoting.
    args = [client, "run", "--mcp.command", f"{command} --read-only",
            "--mcp.stdio-send-initialized-notification", "--health.listen-addr", "127.0.0.1:0"]
    try:
        return subprocess.run(args, cwd=folder, check=False).returncode
    except OSError as exc:
        print(f"Could not start tunnel-client: {exc.strerror or type(exc).__name__}", file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        return 130
