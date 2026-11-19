"""Talks to the C3dMcpBridge plugin inside Civil 3D over 127.0.0.1.

Connection details (port + per-session token) are read from %LOCALAPPDATA%\\C3dMcpBridge\\bridge.json on every
call, so restarting Civil 3D never requires reconfiguring anything.
"""
from __future__ import annotations

import json
import os
import socket
import uuid
from pathlib import Path
from typing import Any

READ_TIMEOUT = 90.0     # the plugin gives up waiting for a busy Civil 3D after 30 s
EDIT_TIMEOUT = 630.0    # an edit may wait for the one-time permission dialog (the plugin stops honouring it at 9 min)


class BridgeError(RuntimeError):
    pass


def bridge_file() -> Path:
    override = os.environ.get("C3D_BRIDGE_FILE")  # used by the tests
    if override:
        return Path(override)
    base = os.environ.get("LOCALAPPDATA")
    if not base:
        raise BridgeError("LOCALAPPDATA is not set; this server is meant to run on the Windows machine with Civil 3D.")
    return Path(base) / "C3dMcpBridge" / "bridge.json"


def call(method: str, params: dict[str, Any] | None = None, timeout: float = READ_TIMEOUT) -> Any:
    path = bridge_file()
    try:
        info = json.loads(path.read_text(encoding="utf-8"))
        port, token = int(info["port"]), str(info["token"])
    except FileNotFoundError:
        raise BridgeError(
            "Civil 3D is not reachable. Start Civil 3D 2027 with the C3dMcpBridge plugin "
            "(type MCPSTATUS in Civil 3D to confirm it is loaded), then try again."
        ) from None
    except (OSError, ValueError, KeyError, TypeError) as exc:
        raise BridgeError(f"Cannot read the bridge file {path}: {exc}") from None

    request = {"id": uuid.uuid4().hex, "token": token, "method": method, "params": params or {}}
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=5) as sock:
            sock.settimeout(timeout)
            sock.sendall((json.dumps(request) + "\n").encode("utf-8"))
            line = sock.makefile("rb").readline()
    except socket.timeout:
        raise BridgeError(
            f"Civil 3D did not answer within {timeout:.0f} s. If this was an edit, check Civil 3D for an open "
            "permission dialog before sending it again (it may still be applied when the dialog is answered)."
        ) from None
    except OSError as exc:
        raise BridgeError(
            f"Could not reach Civil 3D on port {port} ({exc}). Civil 3D may have been closed; "
            "if another Civil 3D is open, type MCPSTATUS in it."
        ) from None

    if not line:
        raise BridgeError("Civil 3D closed the connection without a reply.")
    try:
        reply = json.loads(line.decode("utf-8"))
    except ValueError as exc:
        raise BridgeError(f"Civil 3D sent an unreadable reply: {exc}") from None
    if not reply.get("ok"):
        raise BridgeError(f"Civil 3D reported: {reply.get('error', 'unknown error')}")
    return reply.get("result")
