"""Civil 3D MCP server (stdio). An MCP client launches this process; it forwards calls to the C3dMcpBridge plugin
running inside Civil 3D (see bridge_client.py).

The default `civil3d(action, params)` tool reads and edits. Edits require permission inside Civil 3D.
Pass --read-only to expose only `civil3d_read`.

Set C3D_MCP_SPLIT=1 to expose separate read and edit tools in a regular session.
"""
from __future__ import annotations

import argparse
import functools
import json
import os
import sys
from threading import Event
from typing import Any, Callable

import anyio
from mcp.server.fastmcp import Context, FastMCP
from mcp.types import CallToolResult, TextContent, ToolAnnotations
from pydantic import StrictBool

import catalog
import qa
import sewergems
import watergems
import sheets
from bridge_client import EDIT_TIMEOUT, BridgeError, DrawingTarget, call, request_cancelled

def _help(params: dict[str, Any], *, include_edits: bool) -> dict[str, Any]:
    topic = params.get("topic")
    if topic is not None and not isinstance(topic, str):
        raise ValueError("help topic must be a string.")
    data = catalog.help_data(topic, include_edits=include_edits)
    if topic is not None:
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


def _write_result(action: str, result: dict[str, Any]) -> dict[str, Any] | CallToolResult:
    """Preserve the CAD receipt when a drawing operation reports failure."""
    if action == "edit":
        failed = (result.get("afterCommitSucceeded") is False or result.get("state") == "error"
                  or bool(result.get("error")) or "failedAt" in result or result.get("permission") == "not granted"
                  or (result.get("applied") is False and result.get("dryRun") is not True))
    else:
        core = result.get("core") or {}
        successful_dry_run = (result.get("dryRun") is True and core.get("dryRun") is True
                              and "failedAt" not in core and not core.get("error")
                              and core.get("permission") != "not granted")
        failed = result.get("built") is False and result.get("pending") is not True and not successful_dry_run
    if not failed:
        return result
    return CallToolResult(isError=True, structuredContent=result,
                          content=[TextContent(type="text", text=json.dumps(result, ensure_ascii=False, indent=2))])


def _dispatch(action: str, params: dict[str, Any] | None, allow_read: bool, allow_edit: bool,
              *, include_edit_help: bool = True) -> Any:
    if params is None:
        params = {}
    if not isinstance(params, dict):
        raise ValueError("params must be an object, e.g. {\"network_name\": \"W-01\"}.")
    name = (action or "").strip()

    if name == "edit":
        if not allow_edit:
            raise ValueError("This tool only reads. Use a write-enabled Civil 3D tool to change the drawing.")
        if type(params.get("dry_run", False)) is not bool:
            raise ValueError("dry_run must be true or false.")
        ops = params.get("operations")
        if not isinstance(ops, list) or not ops:
            raise ValueError("edit needs params.operations: a non-empty list of {op: ..., ...}. See action='help', topic='edit'.")
        if not all(isinstance(o, dict) for o in ops):
            raise ValueError("Every item of params.operations must be an object {op: ..., ...}.")
        unknown = sorted({str(o.get("op")) for o in ops} - set(catalog.EDIT_OPS))
        if unknown:
            raise ValueError(f"Unknown edit operation(s): {', '.join(unknown)}. Available: {', '.join(catalog.EDIT_OPS)}")
        return _write_result(name, call("edit", params, timeout=EDIT_TIMEOUT))

    if name == "sewergems_import":
        if not allow_edit:
            raise ValueError("SewerGEMS import needs a write-enabled tool: civil3d, or civil3d_import in split mode.")
        return _write_result(name, sewergems.build(params))
    if name == "pp_sheets_create":
        if not allow_edit:
            raise ValueError("pp_sheets_create needs a write-enabled tool: civil3d, or civil3d_import in split mode.")
        return _write_result(name, sheets.build(params))
    if name == "watergems_import":
        if not allow_edit:
            raise ValueError("WaterGEMS import needs a write-enabled tool: civil3d, or civil3d_import in split mode.")
        return _write_result(name, watergems.build(params))

    if not allow_read:
        raise ValueError("This tool only edits (action='edit'). Use the civil3d_read tool for everything else.")
    if name == "help":
        return _help(params, include_edits=include_edit_help)
    if name == "check_layer_names":
        pattern = params.get("pattern")
        skip_xrefs = params.get("skip_xref_layers", True)
        if not isinstance(pattern, str):
            raise ValueError("check_layer_names needs params.pattern as a string.")
        if type(skip_xrefs) is not bool:
            raise ValueError("skip_xref_layers must be true or false.")
        return qa.check_layer_names(pattern, skip_xrefs)
    if name == "check_default_names":
        return qa.check_default_names(params.get("target"))
    if name == "sewergems_check":
        return sewergems.check(params)
    if name == "watergems_check":
        return watergems.check(params)
    if name == "pp_sheets_plan":
        return sheets.check(params)
    if name in catalog.READ_ACTIONS:
        return call(name, params)
    if name in catalog.EDIT_OPS:
        raise ValueError(f"'{name}' is an edit operation: call action='edit' with params={{\"operations\": [{{\"op\": \"{name}\", ...}}]}}.")
    raise ValueError(f"Unknown action '{name}'. Use action='help' for the list.")


HEARTBEAT_SECONDS = 10.0


async def _off_loop(ctx: Context | None, work: Callable[[], Any]) -> Any:
    """Run a blocking bridge call (socket I/O for up to EDIT_TIMEOUT) in a worker thread.

    FastMCP awaits async tools but calls sync tools directly on the event loop, so a sync tool that blocks for minutes
    freezes the whole stdio session: no pings, no notifications/cancelled, no other requests. While the call runs, a
    progress notification is sent every HEARTBEAT_SECONDS when the client supplied a progressToken, so clients that
    reset their request timeout on progress keep waiting for the permission dialog or a long import.
    The worker stays attached until the active bridge request returns. A cancellation signal stops it from sending
    subsequent bridge requests, and an edit already sent to Civil 3D keeps its request identity.
    """
    stop = Event()

    async def watch_cancellation() -> None:
        try:
            await anyio.sleep_forever()
        finally:
            stop.set()

    async def heartbeat(context: Context) -> None:
        beats = 0
        while True:
            await anyio.sleep(HEARTBEAT_SECONDS)
            beats += 1
            try:
                await context.report_progress(beats, None, "Waiting for Civil 3D")
            except Exception:  # Progress is best effort.
                return

    token = request_cancelled.set(stop)
    try:
        async with anyio.create_task_group() as tg:
            tg.start_soon(watch_cancellation)
            if ctx is not None:
                tg.start_soon(heartbeat, ctx)
            try:
                result, error = await anyio.to_thread.run_sync(work), None
            except Exception as exc:  # Reraise outside the task group to preserve the original message.
                result, error = None, exc
            finally:
                tg.cancel_scope.cancel()
    finally:
        request_cancelled.reset(token)
    if error is not None:
        raise error
    return result


READ_ONLY = ToolAnnotations(title="Civil 3D (read)", readOnlyHint=True, destructiveHint=False, openWorldHint=True)
READ_WRITE = ToolAnnotations(title="Civil 3D", readOnlyHint=False, destructiveHint=True, idempotentHint=False, openWorldHint=True)

def create_server(*, read_only: bool = False) -> FastMCP:
    mcp = FastMCP("civil3d")
    split = os.environ.get("C3D_MCP_SPLIT", "").strip() in ("1", "true", "yes")

    if read_only or split:
        @mcp.tool(name="civil3d_read", description=catalog.tool_description("read"), annotations=READ_ONLY)
        async def civil3d_read(action: str, ctx: Context, params: dict[str, Any] | None = None) -> Any:
            return await _off_loop(ctx, functools.partial(
                _dispatch, action, params, allow_read=True, allow_edit=False, include_edit_help=not read_only))

    if not read_only and split:
        @mcp.tool(name="civil3d_edit", description=catalog.tool_description("edit"), annotations=READ_WRITE)
        async def civil3d_edit(operations: list[dict[str, Any]], target: DrawingTarget, request_id: str, ctx: Context,
                               dry_run: StrictBool = False) -> Any:
            return await _off_loop(ctx, functools.partial(
                _dispatch, "edit", {"operations": operations, "target": target, "request_id": request_id, "dry_run": dry_run},
                allow_read=False, allow_edit=True))

        @mcp.tool(name="civil3d_import", description=catalog.tool_description("import"), annotations=READ_WRITE)
        async def civil3d_import(action: str, ctx: Context, params: dict[str, Any] | None = None) -> Any:
            if action not in catalog.IMPORT_ACTIONS:
                raise ValueError(f"civil3d_import runs {', '.join(sorted(catalog.IMPORT_ACTIONS))} only.")
            return await _off_loop(ctx, functools.partial(_dispatch, action, params, allow_read=False, allow_edit=True))

    if not read_only and not split:
        @mcp.tool(name="civil3d", description=catalog.tool_description("all"), annotations=READ_WRITE)
        async def civil3d(action: str, ctx: Context, params: dict[str, Any] | None = None) -> Any:
            return await _off_loop(ctx, functools.partial(_dispatch, action, params, allow_read=True, allow_edit=True))

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
