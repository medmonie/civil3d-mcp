"""Live test of the MCP transport (docs/AUDIT_TEST_PLAN.md A1, A2, A3, A5, A6): a real MCP client talks to server.py
from source over stdio, exactly like Claude Desktop does.

  transport_live.py edit   A1-A3. Run in a NEW drawing that has NOT been granted (no MCPALLOW). The first edit opens the
                           Yes/No dialog in Civil 3D: leave it open for 2 minutes, then click Yes.
  transport_live.py loop   A6. Calls reads in a loop for 90 s; type MCPSTATUS in Civil 3D several times meanwhile.
  transport_live.py down   A5. Run with Civil 3D closed.
Output: tests\\results\\transport_<mode>.txt
"""
import asyncio
import json
import sys
import time
import uuid
from datetime import timedelta
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

SERVER = Path(__file__).resolve().parent.parent / "server" / "server.py"
RESULTS: dict[str, str] = {}
T0 = time.monotonic()


def ts():
    return f"[{time.monotonic() - T0:7.1f}s]"


def check(name, ok, detail=""):
    RESULTS[name] = "PASS" if ok else "FAIL"
    print(f"{ts()} {RESULTS[name]:5} {name} {detail}", flush=True)


def text_of(res):
    parts = [getattr(c, "text", "") for c in res.content]
    return "\n".join(parts)


async def tool(session, action, params=None, timeout=60, progress=None):
    t = time.monotonic()
    try:
        res = await session.call_tool("civil3d", {"action": action, "params": params or {}},
                                      read_timeout_seconds=timedelta(seconds=timeout), progress_callback=progress)
    except Exception as exc:     # noqa: BLE001 - a client-side timeout is a result here, not a crash
        return True, f"CLIENT ERROR {type(exc).__name__}: {exc}", time.monotonic() - t
    body = text_of(res)
    try:
        data = json.loads(body)
    except ValueError:
        data = body
    return res.isError, data, time.monotonic() - t


async def mode_edit(session):
    err, info, _ = await tool(session, "get_drawing_info")
    print(ts(), "drawing:", json.dumps(info, default=str)[:400], flush=True)
    if err or not isinstance(info, dict):
        check("get_drawing_info", False, str(info))
        return
    if info.get("editingAllowed"):
        print("REFUSED: editing is already allowed in this drawing. Open a NEW drawing without MCPALLOW (or run MCPREVOKE).")
        return
    target = info["target"]
    beats = []

    async def on_progress(progress, total, message):
        beats.append(time.monotonic())
        print(f"{ts()} progress {progress} {message}", flush=True)

    rid1, rid2 = str(uuid.uuid4()), str(uuid.uuid4())
    op1 = [{"op": "draw_line", "start": [0, 0, 0], "end": [10, 0, 0]}]
    op2 = [{"op": "draw_line", "start": [0, 5, 0], "end": [10, 5, 0]}]
    print(ts(), "A1: edit sent - the Yes/No dialog should be open in Civil 3D now. Leave it 2 minutes, then click Yes.", flush=True)
    first = asyncio.create_task(tool(session, "edit", {"operations": op1, "target": target, "request_id": rid1}, timeout=900, progress=on_progress))

    await asyncio.sleep(5)
    err, second, dt = await tool(session, "edit", {"operations": op2, "target": target, "request_id": rid2}, timeout=60)
    s = json.dumps(second, default=str)
    print(ts(), "A3 second edit reply:", s[:600], flush=True)
    check("A3 a second edit is refused while the first waits", "Another MCP edit is still running" in s, f"{dt:.1f}s")

    await asyncio.sleep(3)
    err, ping, dt = await tool(session, "ping", timeout=20)
    check("A2 ping answers at once while the dialog is open", not err and dt < 5, f"{dt:.2f}s {json.dumps(ping, default=str)[:200]}")

    err, res1, dt = await first
    s1 = json.dumps(res1, default=str)
    print(ts(), "A1 first edit reply:", s1[:800], flush=True)
    gaps = [b - a for a, b in zip(beats, beats[1:])]
    check("A1 the client kept waiting and got the result", not err and isinstance(res1, dict) and res1.get("applied") is True and dt >= 110,
          f"waited {dt:.0f}s")
    check("A1 progress about every 10 s", len(beats) >= dt / 10 - 2 and (not gaps or max(gaps) < 15),
          f"{len(beats)} notifications, largest gap {max(gaps) if gaps else 0:.1f}s")

    err, again, dt = await tool(session, "edit", {"operations": op2, "target": target, "request_id": rid2}, timeout=120)
    print(ts(), "A3 resend reply:", json.dumps(again, default=str)[:600], flush=True)
    check("A3 the resend with the same request_id works", not err and isinstance(again, dict) and again.get("applied") is True)

    err, lines, _ = await tool(session, "list_entities", {"type": "LINE"})
    n = len(lines.get("entities", [])) if isinstance(lines, dict) else -1
    check("A3 exactly two lines drawn (nothing extra from the refused call)", n == 2, f"{n} lines")


async def mode_loop(session):
    print(ts(), "A6: reading in a loop for 90 s - type MCPSTATUS in Civil 3D several times now.", flush=True)
    calls = errors = 0
    bad = []
    end = time.monotonic() + 90
    while time.monotonic() < end:
        for action in ("ping", "list_layers", "get_drawing_info"):
            err, data, _ = await tool(session, action, {"limit": 5} if action == "list_layers" else None, timeout=30)
            calls += 1
            s = json.dumps(data, default=str)
            if err or "Cannot read the bridge file" in s:
                errors += 1
                bad.append(s[:300])
        await asyncio.sleep(0.2)
    for b in bad[:10]:
        print("  error:", b, flush=True)
    check("A6 no bridge-file errors while MCPSTATUS runs", errors == 0, f"{calls} calls, {errors} errors")


async def mode_down(session):
    err, data, dt = await tool(session, "get_drawing_info", timeout=60)
    s = json.dumps(data, default=str)
    print(ts(), "A5 reply:", s[:800], flush=True)
    check("A5 clear 'Civil 3D is not reachable' message", err and "Civil 3D is not reachable" in s and "TaskGroup" not in s, f"{dt:.1f}s")


async def main(mode):
    params = StdioServerParameters(command=sys.executable, args=[str(SERVER)], cwd=str(SERVER.parent))
    async with stdio_client(params) as (r, w):
        async with ClientSession(r, w) as session:
            await session.initialize()
            await {"edit": mode_edit, "loop": mode_loop, "down": mode_down}[mode](session)
    for k, v in RESULTS.items():
        print(f"{v:5} {k}")
    print("ALL PASS" if RESULTS and all(v == "PASS" for v in RESULTS.values()) else "FAILED", flush=True)


if __name__ == "__main__":
    asyncio.run(main(sys.argv[1] if len(sys.argv) > 1 else "edit"))
