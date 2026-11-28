"""Offline tests of 0.23: the server's file policy, the bridge client's deadline and cancellation, the SDK guard.

    python tests/policy_test.py
"""
from __future__ import annotations

import json
import os
import socket
import sys
import tempfile
import threading
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
import bridge_client  # noqa: E402
import filepolicy  # noqa: E402
import qa  # noqa: E402


def refused(fn, *needles: str) -> str:
    try:
        fn()
    except (ValueError, bridge_client.BridgeError) as exc:
        text = str(exc)
        assert all(n.lower() in text.lower() for n in needles), f"expected {needles} in: {text}"
        return text
    raise AssertionError(f"not refused: {fn}")


def file_policy() -> None:
    with tempfile.TemporaryDirectory() as tmp:
        tmp = str(Path(tmp).resolve())
        inside, outside = Path(tmp, "jobs"), Path(tmp, "elsewhere")
        inside.mkdir(); outside.mkdir()
        good, bad = inside / "export.csv", outside / "export.csv"
        good.write_text("Label,X\nMH-1,1\n", encoding="utf-8")
        bad.write_text("Label,X\nMH-1,1\n", encoding="utf-8")
        policy = Path(tmp, "policy.json")
        os.environ["C3D_POLICY_FILE"] = str(policy)
        os.environ["LOCALAPPDATA"] = str(Path(tmp, "local"))
        Path(tmp, "local", "C3dMcpBridge").mkdir(parents=True)
        token = Path(tmp, "local", "C3dMcpBridge", "bridge.json")
        token.write_text("{}", encoding="utf-8")

        # no policy file: any absolute file, never the bridge folder
        assert filepolicy.allowed_roots() is None
        assert filepolicy.readable(str(good), "path") == good.resolve()
        assert filepolicy.readable(str(bad), "path") == bad.resolve()
        refused(lambda: filepolicy.readable("export.csv", "path"), "full path")
        refused(lambda: filepolicy.readable(None, "path"), "give path")
        refused(lambda: filepolicy.readable("\\\\.\\C:\\x.csv", "path"), "device")
        refused(lambda: filepolicy.readable(str(token), "path"), "bridge's own folder")
        refused(lambda: filepolicy.readable(str(inside / "missing.csv"), "path"), "not found")

        # with a policy: only under the roots, also through '..'
        policy.write_text(json.dumps({"allowedRoots": [str(inside)]}), encoding="utf-8")
        assert filepolicy.readable(str(good), "path") == good.resolve()
        refused(lambda: filepolicy.readable(str(bad), "path"), "outside the folders")
        refused(lambda: filepolicy.readable(str(inside / ".." / "elsewhere" / "export.csv"), "path"), "outside the folders")
        sibling = Path(tmp, "jobs-old"); sibling.mkdir()
        (sibling / "export.csv").write_text("x", encoding="utf-8")
        refused(lambda: filepolicy.readable(str(sibling / "export.csv"), "path"), "outside the folders")   # not a prefix match

        # an unusable policy refuses everything
        for text in ("{not json", json.dumps({"allowedRoots": []}), json.dumps({"allowedRoots": ["relative"]}), json.dumps([1])):
            policy.write_text(text, encoding="utf-8")
            refused(lambda: filepolicy.readable(str(good), "path"), "not usable")
        policy.unlink()

        # size and row limits
        old_bytes, old_rows = filepolicy.MAX_INPUT_BYTES, filepolicy.MAX_ROWS
        try:
            filepolicy.MAX_INPUT_BYTES = 4
            refused(lambda: filepolicy.readable(str(good), "path"), "limit")
            filepolicy.MAX_INPUT_BYTES, filepolicy.MAX_ROWS = old_bytes, 3
            assert list(filepolicy.bounded(range(3), "t")) == [0, 1, 2]
            refused(lambda: list(filepolicy.bounded(range(4), "t")), "more than 3 rows")
        finally:
            filepolicy.MAX_INPUT_BYTES, filepolicy.MAX_ROWS = old_bytes, old_rows
    print("file policy ok: roots, '..', sibling folders, bridge folder, device paths, broken policy, size and row limits")


class SlowBridge:
    """A bridge that accepts a request and then answers late, in pieces, or never."""

    def __init__(self, tmp: str, behaviour: str):
        self.behaviour = behaviour
        self.sock = socket.socket()
        self.sock.bind(("127.0.0.1", 0))
        self.sock.listen(4)
        self.file = Path(tmp, f"bridge-{behaviour}.json")
        self.file.write_text(json.dumps({"port": self.sock.getsockname()[1], "token": "t", "session_id": "s"}), encoding="utf-8")
        self.closed_by_client = threading.Event()
        threading.Thread(target=self.serve, daemon=True).start()

    def serve(self) -> None:
        conn, _ = self.sock.accept()
        with conn:
            request = json.loads(conn.makefile("rb").readline())
            if self.behaviour == "drip":
                # one byte every 0.2 s: each recv succeeds, so only a whole-call deadline can end the wait
                reply = json.dumps({"id": request["id"], "ok": True, "result": {"x": "y" * 400}}).encode()
                try:
                    for b in reply:
                        conn.sendall(bytes([b]))
                        time.sleep(0.2)
                except OSError:
                    self.closed_by_client.set()
            else:   # silent
                conn.settimeout(30)
                try:
                    if conn.recv(1) == b"":
                        self.closed_by_client.set()
                except OSError:
                    self.closed_by_client.set()


def deadline_and_cancellation() -> None:
    with tempfile.TemporaryDirectory() as tmp:
        # 1. a trickling reply ends at the deadline, not after len(reply) * 0.2 s
        drip = SlowBridge(tmp, "drip")
        os.environ["C3D_BRIDGE_FILE"] = str(drip.file)
        started = time.monotonic()
        refused(lambda: bridge_client.call("list_layers", timeout=1.5), "did not answer within 2 s")
        took = time.monotonic() - started
        assert took < 4, f"the deadline did not bound a trickling reply ({took:.1f} s)"

        # 2. a cancelled read is abandoned at once
        silent = SlowBridge(tmp, "silent")
        os.environ["C3D_BRIDGE_FILE"] = str(silent.file)
        stop = threading.Event()
        outcome: dict[str, object] = {}

        def reader() -> None:
            token = bridge_client.request_cancelled.set(stop)
            try:
                bridge_client.call("list_layers", timeout=60)
            except bridge_client.BridgeError as exc:
                outcome["error"] = str(exc)
            finally:
                bridge_client.request_cancelled.reset(token)

        worker = threading.Thread(target=reader, daemon=True)
        worker.start()
        time.sleep(0.4)
        started = time.monotonic()
        stop.set()
        worker.join(5)
        assert not worker.is_alive(), "a cancelled read kept its worker thread"
        assert time.monotonic() - started < 2 and "cancelled" in str(outcome.get("error")), outcome
        assert silent.closed_by_client.wait(3), "the abandoned read did not close its connection"

        # 3. an edit is never abandoned on cancellation: it waits for its outcome
        silent2 = SlowBridge(tmp, "silent")
        os.environ["C3D_BRIDGE_FILE"] = str(silent2.file)
        stop2 = threading.Event()
        result: dict[str, object] = {}

        def editor() -> None:
            token = bridge_client.request_cancelled.set(stop2)
            try:
                bridge_client.call("edit", {"operations": [{"op": "layer_create", "name": "x"}],
                                            "target": {"session_id": "11111111-1111-1111-1111-111111111111",
                                                       "drawing_id": "22222222-2222-2222-2222-222222222222"},
                                            "request_id": "33333333-3333-3333-3333-333333333333"}, timeout=2.0)
            except bridge_client.BridgeError as exc:
                result["error"] = str(exc)
            finally:
                bridge_client.request_cancelled.reset(token)

        # the session check needs the descriptor's session to match
        silent2.file.write_text(json.dumps({"port": silent2.sock.getsockname()[1], "token": "t",
                                            "session_id": "11111111-1111-1111-1111-111111111111"}), encoding="utf-8")
        worker = threading.Thread(target=editor, daemon=True)
        worker.start()
        time.sleep(0.5)
        stop2.set()
        worker.join(0.8)
        assert worker.is_alive(), "a cancelled edit was abandoned before its outcome was known"
        worker.join(5)
        assert "Edit outcome is unknown" in str(result.get("error")), result
    print("bridge client ok: one deadline per call, cancelled reads abandoned, edits wait for their outcome")


def regex_bounds() -> None:
    refused(lambda: qa.check_layer_names("a" * 301), "limit is 300")
    refused(lambda: qa.check_layer_names("("), "invalid regular expression")
    print("layer QA ok: pattern size bounded before any bridge call")


def sdk_guard() -> None:
    import importlib.metadata as metadata
    import server
    real = metadata.version
    try:
        metadata.version = lambda name: "1.18.0" if name == "mcp" else real(name)
        try:
            server._require_sdk()
        except SystemExit as exc:
            assert exc.code == 2
        else:
            raise AssertionError("an SDK that loses isError was accepted")
        metadata.version = lambda name: "1.19.0" if name == "mcp" else real(name)
        server._require_sdk()
    finally:
        metadata.version = real
    requirement = (Path(__file__).resolve().parent.parent / "server" / "requirements.txt").read_text(encoding="utf-8")
    assert "mcp[cli]>=1.19" in requirement, "requirements.txt allows an MCP SDK that reports failed edits as successes"
    print("SDK guard ok: 1.18 refused, 1.19 accepted, requirements.txt agrees")


if __name__ == "__main__":
    file_policy()
    deadline_and_cancellation()
    regex_bounds()
    sdk_guard()
    print("\nALL OK")
