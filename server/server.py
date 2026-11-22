"""Civil 3D MCP server (stdio). An MCP client launches this process; it forwards calls to the C3dMcpBridge plugin
running inside Civil 3D (see bridge_client.py).

The default `civil3d(action, params)` tool reads and edits. Edits require permission inside Civil 3D.
Pass --read-only to expose only `civil3d_read`.

Set C3D_MCP_SPLIT=1 to expose separate read and edit tools in a regular session.
"""
from __future__ import annotations

import argparse
import os
import sys
from typing import Any

from mcp.server.fastmcp import FastMCP
from mcp.types import ToolAnnotations

import catalog
import qa
import sewergems
from bridge_client import EDIT_TIMEOUT, BridgeError, DrawingTarget, call

def _help(params: dict[str, Any], *, include_edits: bool) -> dict[str, Any]:
    data = catalog.help_data(params.get("topic"), include_edits=include_edits)
    if params.get("topic"):
        return data
    # Report version drift between the running plugin and this catalogue.
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


def _dispatch(action: str, params: dict[str, Any] | None, allow_read: bool, allow_edit: bool,
              *, include_edit_help: bool = True) -> Any:
    params = params or {}
    if not isinstance(params, dict):
        raise ValueError("params must be an object, e.g. {\"network_name\": \"W-01\"}.")
    name = (action or "").strip()

    if name == "edit":
        if not allow_edit:
            raise ValueError("This tool only reads. Use a write-enabled Civil 3D tool to change the drawing.")
        ops = params.get("operations")
        if not isinstance(ops, list) or not ops:
            raise ValueError("edit needs params.operations: a non-empty list of {op: ..., ...}. See action='help', topic='edit'.")
        unknown = sorted({str(o.get("op")) for o in ops if isinstance(o, dict)} - set(catalog.EDIT_OPS))
        if unknown:
            raise ValueError(f"Unknown edit operation(s): {', '.join(unknown)}. Available: {', '.join(catalog.EDIT_OPS)}")
        return call("edit", params, timeout=EDIT_TIMEOUT)

    if name == "sewergems_import":
        if not allow_edit:
            raise ValueError("SewerGEMS import requires the combined write-enabled civil3d tool.")
        return sewergems.build(params)

    if not allow_read:
        raise ValueError("This tool only edits (action='edit'). Use the civil3d_read tool for everything else.")
    if name == "help":
        return _help(params, include_edits=include_edit_help)
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

def create_server(*, read_only: bool = False) -> FastMCP:
    mcp = FastMCP("civil3d")
    split = os.environ.get("C3D_MCP_SPLIT", "").strip() in ("1", "true", "yes")

    if read_only or split:
        @mcp.tool(name="civil3d_read", description=catalog.tool_description("read"), annotations=READ_ONLY)
        def civil3d_read(action: str, params: dict[str, Any] | None = None) -> Any:
            return _dispatch(action, params, allow_read=True, allow_edit=False, include_edit_help=not read_only)

    if not read_only and split:
        @mcp.tool(name="civil3d_edit", description=catalog.tool_description("edit"), annotations=READ_WRITE)
        def civil3d_edit(operations: list[dict[str, Any]], target: DrawingTarget, request_id: str, dry_run: bool = False) -> Any:
            return _dispatch("edit", {"operations": operations, "target": target, "request_id": request_id,
                                      "dry_run": dry_run}, allow_read=False, allow_edit=True)

    if not read_only and not split:
        @mcp.tool(name="civil3d", description=catalog.tool_description("all"), annotations=READ_WRITE)
        def civil3d(action: str, params: dict[str, Any] | None = None) -> Any:
            return _dispatch(action, params, allow_read=True, allow_edit=True)

    return mcp


def run_stdio(mcp: FastMCP) -> None:
    # The SDK wraps and closes its stdin/stdout buffers on disconnect. Give it
    # separate buffers so the bundled launcher can still flush the original streams.
    stdin, stdout = sys.stdin, sys.stdout
    with open(stdin.fileno(), encoding="utf-8", closefd=False) as mcp_in, \
         open(stdout.fileno(), "w", encoding="utf-8", closefd=False) as mcp_out:
        sys.stdin, sys.stdout = mcp_in, mcp_out
        try:
            mcp.run(transport="stdio")
        finally:
            sys.stdin, sys.stdout = stdin, stdout


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Civil 3D stdio MCP server")
    parser.add_argument("--read-only", action="store_true", help="Expose only drawing reads and checks, even when C3D_MCP_SPLIT is set.")
    run_stdio(create_server(read_only=parser.parse_args().read_only))
else:
    mcp = create_server()
