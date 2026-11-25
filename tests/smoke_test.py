"""End-to-end test of the MCP server WITHOUT Civil 3D.

Starts fake_bridge.py, launches server.py over stdio like ChatGPT desktop does, and exercises the single
`civil3d` tool: reads, QA checks, help, the edit path, the declined-permission path and error handling.
Also checks that the catalogue matches the plugin sources, that the version numbers agree and that
docs/ACTIONS.md is current.

    python tests/smoke_test.py
"""
from __future__ import annotations

import asyncio
import json
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from threading import Event
from uuid import uuid4

import anyio
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

ROOT = Path(__file__).resolve().parent.parent
SERVER = ROOT / "server" / "server.py"
sys.path.insert(0, str(ROOT / "server"))
import catalog  # noqa: E402


async def stdio_lifecycle() -> None:
    """Exercise actual stdout frames independently of the SDK client wrapper."""
    process = await asyncio.create_subprocess_exec(sys.executable, "-B", str(SERVER),
                                                  stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE,
                                                  stderr=asyncio.subprocess.DEVNULL)
    assert process.stdin is not None and process.stdout is not None

    async def send(method, request_id=None, params=None):
        message = {"jsonrpc": "2.0", "method": method}
        if request_id is not None:
            message["id"] = request_id
        if params is not None:
            message["params"] = params
        process.stdin.write((json.dumps(message) + "\n").encode("utf-8"))
        await process.stdin.drain()
        if request_id is not None:
            raw = await asyncio.wait_for(process.stdout.readline(), 10)
            response = json.loads(raw)
            assert response["jsonrpc"] == "2.0" and response["id"] == request_id, response
            return response

    try:
        init = await send("initialize", "start", {"protocolVersion": "2025-06-18", "capabilities": {},
                                                   "clientInfo": {"name": "audit", "version": "1"}})
        assert init["result"]["protocolVersion"] == "2025-06-18", init
        assert "tools" in init["result"]["capabilities"], init
        await send("notifications/initialized")
        assert (await send("ping", 7))["result"] == {}
        assert {tool["name"] for tool in (await send("tools/list", "tools"))["result"]["tools"]} == {"civil3d"}
    finally:
        process.stdin.close()
        try:
            await asyncio.wait_for(process.wait(), 10)
        except TimeoutError:
            process.kill()
            await process.wait()
            raise
    assert process.returncode == 0
    print("MCP lifecycle and stdout framing verified using raw JSON-RPC")


async def cancellation_barrier() -> None:
    import bridge_client
    from server import _off_loop

    started, release = Event(), Event()
    errors = []
    with anyio.CancelScope() as scope:
        def work():
            signal = bridge_client.request_cancelled.get()
            started.set()
            assert signal is not None and release.wait(5)
            try:
                bridge_client.call("ping")
            except bridge_client.BridgeError as exc:
                errors.append(str(exc))

        async def cancel_running():
            try:
                with anyio.fail_after(5):
                    while not started.is_set():
                        await anyio.sleep(0.01)
                scope.cancel()
                with anyio.CancelScope(shield=True):
                    await anyio.sleep(0.05)
            finally:
                release.set()

        async with anyio.create_task_group() as tg:
            tg.start_soon(cancel_running)
            await _off_loop(None, work)
    assert len(errors) == 1 and "cancelled before" in errors[0], errors
    print("Cancellation prevented the worker from sending another bridge request")


def parse(result):
    if result.isError:
        raise AssertionError(result.content[0].text)
    return json.loads(result.content[0].text)


async def run(session, action, params=None):
    return parse(await session.call_tool("civil3d", {"action": action, "params": params or {}}))


async def expect_error(session, action, params, needle):
    res = await session.call_tool("civil3d", {"action": action, "params": params})
    assert res.isError, f"{action} should have failed"
    assert needle.lower() in res.content[0].text.lower(), res.content[0].text
    return res.content[0].text


async def single_tool(bridge_file: Path) -> None:
    params = StdioServerParameters(command=sys.executable, args=[str(SERVER)],
                                   env={**os.environ, "C3D_BRIDGE_FILE": str(bridge_file)})
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            tools = (await session.list_tools()).tools
            assert [t.name for t in tools] == ["civil3d"], [t.name for t in tools]
            desc = tools[0].description
            for name in list(catalog.READ_ACTIONS) + list(catalog.EDIT_OPS) + list(catalog.LOCAL_ACTIONS):
                assert name in desc, f"{name} missing from the tool description"
            print(f"one tool 'civil3d', description {len(desc)} chars, "
                  f"{len(catalog.READ_ACTIONS)} read actions, {len(catalog.EDIT_OPS)} edit operations")

            info = await run(session, "get_drawing_info")
            assert info["civil"]["surfaces"] == 2, info
            pipes = await run(session, "list_pipes", {"network_name": "ss-main"})
            assert pipes["returned"] == 2, pipes
            pp = await run(session, "list_pressure_pipes", {"network_name": "pn-test"})
            assert pp["returned"] == 2, pp
            sp = await run(session, "alignment_station_to_point", {"alignment_name": "RD-01", "station": 10})
            assert sp["easting"] == 100.0, sp
            print("read actions ok")

            layers = await run(session, "check_layer_names", {"pattern": r"[A-Z]-[A-Z]+-[A-Z]+"})
            assert layers["nonConforming"] == ["layer1"], layers
            names = await run(session, "check_default_names")
            assert names["totalFlagged"] == 3, names  # Surface1, Alignment - (2), Pipe Network - (1)
            assert names["target"] == info["target"], names
            print("QA checks ok:", names["defaultNamed"])

            h = await run(session, "help")
            assert h["plugin"]["missingInPlugin"] == [] and h["plugin"]["undocumentedInCatalogue"] == [], h["plugin"]
            one = await run(session, "help", {"topic": "pressure_add"})
            assert "pipes?" in one["pressure_add"]["params"], one
            print("help ok")

            assert (await run(session, "ping"))["editingAllowed"] is False
            target = info["target"]
            await expect_error(session, "edit", {"operations": [{"op": "layer_create", "name": "C-TEST"}]}, "drawing target is required")
            await expect_error(session, "edit", {"operations": [{"op": "layer_create", "name": "C-TEST"}],
                                                 "target": {**target, "drawing_id": str(uuid4())}, "request_id": str(uuid4())},
                               "different from the request target")
            await expect_error(session, "edit", {"operations": [{"op": "layer_create", "name": "C-TEST"}],
                                                 "target": {**target, "session_id": str(uuid4())}, "request_id": str(uuid4())},
                               "session changed")
            assert (await run(session, "ping"))["editingAllowed"] is False
            dry = await run(session, "edit", {"target": target, "request_id": info["new_edit_request_id"],
                                               "dry_run": True, "operations": [{"op": "layer_create", "name": "C-TEST"}]})
            assert dry["applied"] is False and dry["dryRun"] is True, dry
            edit_params = {"target": target, "request_id": str(uuid4()), "operations": [
                {"op": "pressure_network_create", "name": "W-01", "parts_list": "Water"},
                {"op": "pressure_add", "network": "W-01",
                 "pipes": [{"start": [0, 0, 1], "end": [10, 0, 1], "size": 150, "match": "ductile"}]}]}
            done = await run(session, "edit", edit_params)
            assert done["applied"] is True and done["operations"] == 2, done
            assert await run(session, "edit", edit_params) == done
            assert (await run(session, "get_edit_permission"))["appliedEdits"] == 1
            status = await run(session, "get_edit_status", {"target": target, "request_id": edit_params["request_id"]})
            assert status["state"] == "completed" and status["applied"] is True, status
            await expect_error(session, "edit", {**edit_params, "dry_run": True}, "different edit")
            assert (await run(session, "ping"))["editingAllowed"] is True   # granted once, stays granted
            print("edit path ok (permission granted once, then reused)")

            await expect_error(session, "edit", {"operations": [{"op": "format_disk"}]}, "unknown edit operation")
            await expect_error(session, "edit", {}, "non-empty list")
            await expect_error(session, "edit", {**edit_params, "dry_run": "false"}, "true or false")
            await expect_error(session, "layer_create", {"name": "X"}, "is an edit operation")
            await expect_error(session, "no_such_action", {}, "unknown action")
            await expect_error(session, "check_layer_names", {"pattern": "("}, "invalid regular expression")
            print("error paths ok")


async def declined(bridge_file: Path) -> None:
    params = StdioServerParameters(command=sys.executable, args=[str(SERVER)],
                                   env={**os.environ, "C3D_BRIDGE_FILE": str(bridge_file)})
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            info = await run(session, "get_drawing_info")
            res = await session.call_tool("civil3d", {"action": "edit", "params": {
                "operations": [{"op": "layer_create", "name": "C-TEST"}],
                "target": info["target"], "request_id": info["new_edit_request_id"]}})
            assert res.isError and res.structuredContent["applied"] is False, res
            assert res.structuredContent["permission"] == "not granted", res
            print("declined permission ok: nothing applied")


async def split_tools(bridge_file: Path) -> None:
    params = StdioServerParameters(command=sys.executable, args=[str(SERVER)],
                                   env={**os.environ, "C3D_BRIDGE_FILE": str(bridge_file), "C3D_MCP_SPLIT": "1"})
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            tools = sorted(t.name for t in (await session.list_tools()).tools)
            assert tools == ["civil3d_edit", "civil3d_import", "civil3d_read"], tools
            res = await session.call_tool("civil3d_import", {"action": "edit", "params": {}})
            assert res.isError, "the import tool must run imports only"
            res = await session.call_tool("civil3d_read", {"action": "edit", "params": {"operations": [{"op": "layer_create", "name": "X"}]}})
            assert res.isError, "the read tool must refuse edits"
            info = parse(await session.call_tool("civil3d_read", {"action": "get_drawing_info"}))
            invalid = await session.call_tool("civil3d_edit", {"operations": [{"op": "layer_create", "name": "X"}],
                                                               "target": info["target"], "request_id": info["new_edit_request_id"],
                                                               "dry_run": "false"})
            assert invalid.isError, invalid
            ok = parse(await session.call_tool("civil3d_edit", {"operations": [{"op": "layer_create", "name": "X"}],
                                                               "target": info["target"], "request_id": info["new_edit_request_id"]}))
            assert ok["applied"] is True, ok
            print("split mode ok: civil3d_read refuses edits, civil3d_edit applies them, civil3d_import runs imports only")


async def read_only(bridge_file: Path) -> None:
    params = StdioServerParameters(command=sys.executable, args=[str(SERVER), "--read-only"],
                                   env={**os.environ, "C3D_BRIDGE_FILE": str(bridge_file), "C3D_MCP_SPLIT": "1"})
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            (tool,) = (await session.list_tools()).tools
            assert tool.name == "civil3d_read" and tool.annotations.readOnlyHint is True, tool
            assert "sewergems_check" in tool.description and "sewergems_import" not in tool.description
            assert parse(await session.call_tool(tool.name, {"action": "ping"}))["editingAllowed"] is False
            help_data = parse(await session.call_tool(tool.name, {"action": "help"}))
            assert "edit" not in help_data and "editOperations" not in help_data, help_data.keys()
            assert "sewergems_import" not in help_data["localActions"]
            for action, data in (
                ("edit", {"operations": [{"op": "layer_create", "name": "C-TEST"}]}),
                ("sewergems_import", {}),
                ("help", {"topic": "edit"}),
            ):
                response = await session.call_tool(tool.name, {"action": action, "params": data})
                assert response.isError, (action, response)
            print("read-only mode ok: advertises and accepts reads only, even with split mode requested")


def static_checks() -> None:
    cs = (ROOT / "plugin" / "BridgeInfo.cs").read_text(encoding="utf-8")
    version = re.search(r'Version = "([\d.]+)"', cs).group(1)
    xml = (ROOT / "plugin" / "PackageContents.xml").read_text(encoding="utf-8")
    assert f'AppVersion="{version}"' in xml, "PackageContents.xml AppVersion differs from BridgeInfo.Version"
    # one bundle component per supported release, each matching a build target in the csproj
    proj = (ROOT / "plugin" / "C3dMcpBridge.csproj").read_text(encoding="utf-8")
    for release, tfm in (("2024", "net48"), ("2025", "net8.0-windows"), ("2027", "net10.0-windows")):
        assert 'Platform="AutoCAD*"' not in xml, "PackageContents.xml must load in Civil 3D only (Platform=\"Civil3D\")"
        assert f"./Contents/{release}/C3dMcpBridge.dll" in xml, f"PackageContents.xml has no Civil 3D {release} component"
        assert f"'$(TargetFramework)'=='{tfm}'" in proj and f"<C3dRelease>{release}</C3dRelease>" in proj, f"csproj does not build {release} as {tfm}"
    # every catalogue name must be registered in the plugin sources, and the other way round
    src = "\n".join(p.read_text(encoding="utf-8") for p in (ROOT / "plugin").rglob("*.cs") if "obj" not in p.parts and "bin" not in p.parts)
    read_names = re.findall(r'Read\("([a-z_0-9]+)"', src)
    op_names = re.findall(r'ops\["([a-z_0-9]+)"\]', src)
    reads, ops = set(read_names), set(op_names)
    assert len(read_names) == len(reads), "Duplicate native read registration"
    assert len(op_names) == len(ops), "Duplicate native edit operation registration"
    assert reads == set(catalog.READ_ACTIONS), (sorted(reads ^ set(catalog.READ_ACTIONS)))
    assert ops == set(catalog.EDIT_OPS), (sorted(ops ^ set(catalog.EDIT_OPS)))
    # the README quotes the version and the number of actions
    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    short = ".".join(version.split(".")[:2])
    for needle in (f"Version {short}", f"{len(catalog.READ_ACTIONS)} read actions", f"{len(catalog.EDIT_OPS)} edit operations"):
        assert needle in readme, f"README.md does not say '{needle}'"
    # the generated action reference must be current
    docs = subprocess.run([sys.executable, str(ROOT / "tools" / "gen_docs.py"), "--check"], capture_output=True, text=True)
    assert docs.returncode == 0, docs.stderr or docs.stdout
    print(f"static checks ok: version {version}, catalogue matches the plugin sources, docs/ACTIONS.md is current")


def with_fake(coro, **env) -> None:
    with tempfile.TemporaryDirectory() as tmp:
        bridge_file = Path(tmp) / "bridge.json"
        fake = subprocess.Popen([sys.executable, str(ROOT / "tests" / "fake_bridge.py"), str(bridge_file)],
                                env={**os.environ, **env}, stdout=subprocess.DEVNULL)
        try:
            for _ in range(50):
                if bridge_file.exists():
                    break
                time.sleep(0.1)
            else:
                raise SystemExit("fake bridge did not start")
            asyncio.run(coro(bridge_file))
        finally:
            fake.terminate()
            fake.wait(timeout=5)


if __name__ == "__main__":
    static_checks()
    asyncio.run(stdio_lifecycle())
    anyio.run(cancellation_barrier)
    with_fake(single_tool)
    with_fake(declined, FAKE_EDIT="decline")
    with_fake(split_tools)
    with_fake(read_only)
    # offline importer tests (each prints its own checks)
    for script in ("sewergems_test.py", "watergems_test.py", "sheets_test.py"):
        run = subprocess.run([sys.executable, str(ROOT / "tests" / script)], capture_output=True, text=True)
        assert run.returncode == 0, run.stdout + run.stderr
        print(f"{script}: ok")
    print("\nALL OK")
