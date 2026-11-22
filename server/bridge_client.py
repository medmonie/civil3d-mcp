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

from typing_extensions import TypedDict

READ_TIMEOUT = 90.0     # the plugin gives up waiting for a busy Civil 3D after 30 s
EDIT_TIMEOUT = 630.0    # an edit may wait for the one-time permission dialog (the plugin stops honouring it at 9 min)


class BridgeError(RuntimeError):
    pass


class DrawingTarget(TypedDict):
    session_id: str
    drawing_id: str


def _uuid(value: Any, name: str) -> str:
    try:
        parsed = uuid.UUID(value) if isinstance(value, str) else None
    except ValueError:
        parsed = None
    if parsed is None or parsed.int == 0:
        raise ValueError(f"'{name}' must be a non-empty UUID.")
    return str(parsed)


def drawing_target(params: dict[str, Any]) -> DrawingTarget:
    target = params.get("target")
    if not isinstance(target, dict):
        raise ValueError("A drawing target is required. Read get_drawing_info and reuse its target.")
    return DrawingTarget(session_id=_uuid(target.get("session_id"), "session_id"),
                         drawing_id=_uuid(target.get("drawing_id"), "drawing_id"))


def edit_request_id(params: dict[str, Any]) -> str:
    return _uuid(params.get("request_id"), "request_id")


def bridge_file() -> Path:
    override = os.environ.get("C3D_BRIDGE_FILE")  # used by the tests
    if override:
        return Path(override)
    base = os.environ.get("LOCALAPPDATA")
    if not base:
        raise BridgeError("LOCALAPPDATA is not set; this server is meant to run on the Windows machine with Civil 3D.")
    return Path(base) / "C3dMcpBridge" / "bridge.json"


def call(method: str, params: dict[str, Any] | None = None, timeout: float = READ_TIMEOUT) -> Any:
    params = dict(params or {})
    target = drawing_target(params) if method in ("edit", "get_edit_status") or "target" in params else None
    if target is not None:
        params["target"] = target
    if method in ("edit", "get_edit_status"):
        params["request_id"] = edit_request_id(params)

    path = bridge_file()
    try:
        info = json.loads(path.read_text(encoding="utf-8"))
        port, token = int(info["port"]), str(info["token"])
    except FileNotFoundError:
        raise BridgeError(
            "Civil 3D is not reachable. Start Civil 3D (2024, 2025 or 2027) with the C3dMcpBridge plugin "
            "(type MCPSTATUS in Civil 3D to confirm it is loaded), then try again."
        ) from None
    except (OSError, ValueError, KeyError, TypeError) as exc:
        raise BridgeError(f"Cannot read the bridge file {path}: {exc}") from None

    if target is not None:
        if "session_id" not in info:
            raise BridgeError("The loaded Civil 3D add-in does not support drawing targets. Update and restart Civil 3D.")
        if info["session_id"] != target["session_id"]:
            raise BridgeError("The selected Civil 3D session changed. Targeted requests require their original session.")

    request = {"id": uuid.uuid4().hex, "token": token, "method": method, "params": params}
    edit_outcome = (f" Edit outcome is unknown. Check get_edit_status with request_id {params['request_id']} "
                    "and the original target before retrying.") if method == "edit" else ""
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=5) as sock:
            sock.settimeout(timeout)
            sock.sendall((json.dumps(request, sort_keys=True, allow_nan=False) + "\n").encode("utf-8"))
            with sock.makefile("rb") as reply_file:
                line = reply_file.readline()
    except socket.timeout:
        raise BridgeError(f"Civil 3D did not answer within {timeout:.0f} s.{edit_outcome}") from None
    except OSError as exc:
        raise BridgeError(
            f"Could not reach Civil 3D on port {port} ({exc}). Civil 3D may have been closed; "
            f"if another Civil 3D is open, type MCPSTATUS in it.{edit_outcome}"
        ) from None

    if not line:
        raise BridgeError(f"Civil 3D closed the connection without a reply.{edit_outcome}")
    try:
        reply = json.loads(line.decode("utf-8"))
    except ValueError as exc:
        raise BridgeError(f"Civil 3D sent an unreadable reply: {exc}.{edit_outcome}") from None
    if not isinstance(reply, dict) or reply.get("id") != request["id"]:
        raise BridgeError(f"Civil 3D sent a reply with an unexpected request ID.{edit_outcome}")
    if not reply.get("ok"):
        raise BridgeError(f"Civil 3D reported: {reply.get('error', 'unknown error')}")
    result = reply.get("result")
    if not isinstance(result, dict):
        return result
    if isinstance(reply.get("target"), dict):
        result["target"] = reply["target"]
        if method == "get_drawing_info":
            result["new_edit_request_id"] = str(uuid.uuid4())
    if method == "edit":
        result["request_id"] = params["request_id"]
    return result
