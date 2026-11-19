"""Civil 3D MCP server (stdio). Claude launches this process; it forwards every call to the C3dMcpBridge plugin
running inside Civil 3D (see bridge_client.py).

ONE tool by default - `civil3d(action, params)` - so the Claude app has a single thing to allow, once. Reading is
always available; editing is gated inside Civil 3D by a one-time permission per Civil 3D session (dialog or MCPALLOW).

Set C3D_MCP_SPLIT=1 to expose two tools instead (`civil3d_read`, `civil3d_edit`) if you prefer to allow reading
without allowing edits in the Claude app.
"""
from __future__ import annotations

import os
from typing import Any

from mcp.server.fastmcp import FastMCP
from mcp.types import ToolAnnotations

import catalog
import qa
import sewergems
from bridge_client import EDIT_TIMEOUT, BridgeError, call

mcp = FastMCP("civil3d")


def _help(params: dict[str, Any]) -> dict[str, Any]:
    data = catalog.help_data(params.get("topic"))
    if params.get("topic"):
        return data
    # Tell Claude when the installed plugin and this catalogue disagree (e.g. after updating only one of them).
    try:
        live = call("list_actions")
        plugin_actions = {a["name"] for a in live["actions"]}
        plugin_ops = set(live.get("editOperations", []))
        data["plugin"] = {
            "bridgeVersion": live.get("bridgeVersion"),
            "editPermission": live.get("editPermission"),
            "missingInPlugin": sorted((set(catalog.READ_ACTIONS) | {"edit"}) - plugin_actions)
                               + sorted(f"edit:{o}" for o in set(catalog.EDIT_OPS) - plugin_ops),
            "undocumentedInCatalogue": sorted(plugin_actions - set(catalog.READ_ACTIONS) - {"edit"})
                                       + sorted(f"edit:{o}" for o in plugin_ops - set(catalog.EDIT_OPS)),
        }
    except BridgeError as exc:
        data["plugin"] = {"unreachable": str(exc)}
    return data


def _dispatch(action: str, params: dict[str, Any] | None, allow_read: bool, allow_edit: bool) -> Any:
    params = params or {}
    if not isinstance(params, dict):
        raise ValueError("params must be an object, e.g. {\"network_name\": \"W-01\"}.")
    name = (action or "").strip()

    if name == "edit":
        if not allow_edit:
            raise ValueError("This tool only reads. Use the civil3d_edit tool to change the drawing.")
        ops = params.get("operations")
        if not isinstance(ops, list) or not ops:
            raise ValueError("edit needs params.operations: a non-empty list of {op: ..., ...}. See action='help', topic='edit'.")
        unknown = sorted({str(o.get("op")) for o in ops if isinstance(o, dict)} - set(catalog.EDIT_OPS))
        if unknown:
            raise ValueError(f"Unknown edit operation(s): {', '.join(unknown)}. Available: {', '.join(catalog.EDIT_OPS)}")
        return call("edit", params, timeout=EDIT_TIMEOUT)

    if name == "sewergems_import":
        if not allow_edit:
            raise ValueError("This tool only reads. Use the civil3d_edit tool to import a SewerGEMS network.")
        return sewergems.build(params)

    if not allow_read:
        raise ValueError("This tool only edits (action='edit'). Use the civil3d_read tool for everything else.")
    if name == "help":
        return _help(params)
    if name == "check_layer_names":
        if "pattern" not in params:
            raise ValueError("check_layer_names needs params.pattern (a regular expression).")
        return qa.check_layer_names(str(params["pattern"]), bool(params.get("skip_xref_layers", True)))
    if name == "check_default_names":
        return qa.check_default_names()
    if name == "sewergems_check":
        return sewergems.check(params)
    if name in catalog.READ_ACTIONS:
        return call(name, params)
    if name in catalog.EDIT_OPS:
        raise ValueError(f"'{name}' is an edit operation: call action='edit' with params={{\"operations\": [{{\"op\": \"{name}\", ...}}]}}.")
    raise ValueError(f"Unknown action '{name}'. Use action='help' for the list.")


READ_ONLY = ToolAnnotations(title="Civil 3D (read)", readOnlyHint=True, destructiveHint=False, openWorldHint=False)
READ_WRITE = ToolAnnotations(title="Civil 3D", readOnlyHint=False, destructiveHint=True, idempotentHint=False, openWorldHint=False)

if os.environ.get("C3D_MCP_SPLIT", "").strip() in ("1", "true", "yes"):
    @mcp.tool(name="civil3d_read", description=catalog.tool_description("read"), annotations=READ_ONLY)
    def civil3d_read(action: str, params: dict[str, Any] | None = None) -> Any:
        return _dispatch(action, params, allow_read=True, allow_edit=False)

    @mcp.tool(name="civil3d_edit", description=catalog.tool_description("edit"),
              annotations=ToolAnnotations(title="Civil 3D (edit)", readOnlyHint=False, destructiveHint=True, openWorldHint=False))
    def civil3d_edit(operations: list[dict[str, Any]], dry_run: bool = False) -> Any:
        return _dispatch("edit", {"operations": operations, "dry_run": dry_run}, allow_read=False, allow_edit=True)
else:
    @mcp.tool(name="civil3d", description=catalog.tool_description("all"), annotations=READ_WRITE)
    def civil3d(action: str, params: dict[str, Any] | None = None) -> Any:
        return _dispatch(action, params, allow_read=True, allow_edit=True)


if __name__ == "__main__":
    mcp.run()  # stdio transport
