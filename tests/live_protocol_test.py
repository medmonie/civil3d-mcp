"""Verify the source stdio server against a running Civil 3D instance on a fresh unsaved DrawingN.dwg (any units)."""
from __future__ import annotations

import argparse
import asyncio
import json
import os
import re
import sys
from pathlib import Path
from uuid import uuid4

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

SERVER = Path(__file__).resolve().parents[1] / "server" / "server.py"


async def verify(edits: bool) -> None:
    settings = StdioServerParameters(command=sys.executable, args=["-B", str(SERVER)],
                                      env={**os.environ, "C3D_MCP_SPLIT": "0"})
    async with stdio_client(settings) as (read, write), ClientSession(read, write) as session:
        async def action(name, params=None, *, error=False):
            reply = await session.call_tool("civil3d", {"action": name, "params": params or {}})
            assert bool(reply.isError) == error, (name, reply)
            if reply.structuredContent is not None:
                return reply.structuredContent
            message = reply.content[0].text
            return message if error else json.loads(message)

        handshake = await session.initialize()
        assert [tool.name for tool in (await session.list_tools()).tools] == ["civil3d"]
        ping = await action("ping")
        info = await action("get_drawing_info")
        assert re.fullmatch(r"Drawing\d+\.dwg", info["name"]) and info["name"] == info["path"], "Use a fresh unsaved DrawingN.dwg."
        assert info["modelSpaceEntityCount"] == 0 and not any(info["civil"].values()), info
        print("drawing units:", info["civilDrawingUnits"])
        target = info["target"]
        names = await action("check_default_names", {"target": target})
        assert names["target"] == target and names["totalFlagged"] == 0, names
        invalid_target = {**target, "drawing_id": str(uuid4())}
        rejected = await action("list_layers", {"target": invalid_target}, error=True)
        assert "target" in rejected.lower(), rejected
        line = {"op": "draw_line", "start": [0, 0, 0], "end": [10, 0, 0]}
        rejected = await action("edit", {"target": invalid_target, "request_id": str(uuid4()),
                                         "dry_run": True, "operations": [line]}, error=True)
        assert "different from the request target" in rejected, rejected
        print(f"Live stdio {handshake.protocolVersion}, bridge {ping['bridgeVersion']}, "
              "drawing identity, Civil object reads, and stale target rejection for reads and edits passed.")
        if not edits:
            return

        assert info["editingAllowed"] is True, "Enable MCPALLOW in the empty scratch drawing before running --edits."
        edit = {"target": target, "request_id": info["new_edit_request_id"], "operations": [line]}

        async def model_count():
            return (await action("get_drawing_info", {"target": target}))["modelSpaceEntityCount"]

        assert (await action("edit", {**edit, "dry_run": True}))["dryRun"] is True
        assert await model_count() == 0
        rollback = {**edit, "request_id": str(uuid4()), "operations": [line,
                    {"op": "draw_circle", "center": [0, 0, 0], "radius": -1}]}
        failure = await action("edit", rollback, error=True)
        assert failure["applied"] is False and failure["failedAt"] == 2, failure
        assert await model_count() == 0

        committed = await action("edit", edit)
        assert committed["applied"] is True, committed
        handle = committed["results"][0]["result"]["handle"]
        try:
            assert await action("edit", edit) == committed, committed
            assert await model_count() == 1
            status = await action("get_edit_status", {"target": target, "request_id": edit["request_id"]})
            assert status["state"] == "completed" and status["applied"] is True and status["retryable"] is False, status
            duplicate = await action("edit", {**edit, "operations": [{**line, "end": [20, 0, 0]}]}, error=True)
            assert "different edit" in duplicate, duplicate
        finally:
            clean = await action("edit", {"target": target, "request_id": str(uuid4()),
                                          "operations": [{"op": "entity_erase", "handles": [handle]}]})
            assert clean["applied"] is True, clean
        assert await model_count() == 0
        print("Live dry run, atomic rollback, commit, replay, edit status, and scratch cleanup passed.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--edits", action="store_true", help="Exercise native commits on the blank scratch drawing.")
    asyncio.run(verify(parser.parse_args().edits))
