"""Offline regressions for SewerGEMS validation and its two edit transactions."""

import sys
import unittest
from pathlib import Path
from unittest.mock import patch
from uuid import uuid4

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
import sewergems  # noqa: E402


def node(x, y=0, kind="manhole"):
    return {"kind": kind, "x": x, "y": y, "invert": 0, "ground": 3}


def pipe(start, stop, *, slope=0):
    return {"label": f"{start}-{stop}", "start": start, "stop": stop, "length": 10,
            "inv_start": 0, "inv_stop": 0, "diameter_mm": 150, "material": "PVC", "slope_permille": slope}


def trunk(count):
    nodes = {str(i): node(i * 10, kind="outfall" if i == 0 else "manhole") for i in range(count + 1)}
    return nodes, [pipe(str(i + 1), str(i), slope=i / 100) for i in range(count)]


def catalogue_size(mm):
    return {"inner_diameter": mm, "Units": "mm", "Label": f"PID {mm}", "size_index": 0}


class SewerGemsTest(unittest.TestCase):
    def test_imported_columns_reject_incompatible_units_and_duplicates(self):
        self.assertEqual(sewergems._keys(["Diameter (mm)", "Unused", "Unused"], "Conduit")[0], "diameter")
        with self.assertRaisesRegex(ValueError, "must use mm"):
            sewergems._keys(["Diameter (in)"], "Conduit")
        with self.assertRaisesRegex(ValueError, "duplicate column"):
            sewergems._keys(["Diameter (mm)", "Diameter"], "Conduit")

    def test_flat_cycle_cannot_disappear_from_plan(self):
        nodes = {"outfall": node(0, kind="outfall"), "a": node(10), "b": node(40), "c": node(50)}
        pipes = [pipe("a", "outfall"), pipe("b", "c"), pipe("c", "b")]
        self.assertIn("cycle", " ".join(sewergems.validate(nodes, pipes)))

    def test_multiple_branches_at_one_junction_get_distinct_names(self):
        nodes = {"o": node(0, kind="outfall"), "J-1": node(10), "a": node(20), "b": node(10, 10), "c": node(10, -10)}
        pipes = [pipe("J-1", "o"), *(pipe(k, "J-1") for k in ("a", "b", "c"))]
        self.assertEqual(sewergems.validate(nodes, pipes), [])
        lines = sewergems.split_lines(nodes, pipes, {}, "LINE")
        self.assertEqual([line["name"] for line in lines], ["LINE1", "LINE1-1", "LINE1-1-2"])
        self.assertEqual(sum(len(line["pipes"]) for line in lines), len(pipes))

    def test_long_trunk_does_not_recurse(self):
        nodes, pipes = trunk(1200)
        lines = sewergems.split_lines(nodes, pipes, {}, "LINE")
        self.assertEqual(len(lines), 1)
        self.assertEqual(len(lines[0]["pipes"]), 1200)

    def test_catalogue_keeps_nominal_allowance_and_refuses_undersizing(self):
        data = {"pipeFamilies": [{"family": "PVC", "sizes": [catalogue_size(200), catalogue_size(250)]}],
                "structureFamilies": []}
        with patch.object(sewergems, "call", return_value=data):
            parts = sewergems._choose_parts({}, "Sanitary", "PVC", "Manhole", 1200, {191})
            self.assertEqual(parts["pipe_sizes"][191]["inner_diameter"], 200)
            with self.assertRaisesRegex(ValueError, "350 mm"):
                sewergems._choose_parts({}, "Sanitary", "PVC", "Manhole", 1200, {350})

    def test_large_table_is_grouped_into_one_annotation_operation(self):
        nodes, pipes = trunk(100)
        for p in pipes:
            p["nominal_mm"] = 150
        lines = sewergems.split_lines(nodes, pipes, {}, "LINE")
        view = {"handle": "AF", "extents": {"min": [0, 0, 0], "max": [1000, 20, 0]}, "stationStart": 0}
        batch = sewergems._finish_ops([100, 0], lines, nodes, {"LINE1 - Profile": view}, 30, True)
        (annotations,) = [op for op in batch if op["op"] == "draw_annotations"]
        self.assertGreater(len(annotations["items"]), 500)
        self.assertLess(len(batch), 500)

    def test_failed_finish_is_visible_and_retry_reuses_core(self):
        nodes, pipes = trunk(1)
        plan = {"nodes": nodes, "pipes": pipes, "lines": sewergems.split_lines(nodes, pipes, {}, "LINE"),
                "rename": {}, "source": "test.csv", "problems": []}
        parts = {"pipe_family": "PVC", "pipe_sizes": {150: catalogue_size(150)}, "structure_family": "Manhole",
                 "structure_diameter": 1200}
        target = {"session_id": str(uuid4()), "drawing_id": str(uuid4())}
        request = {"target": target, "request_id": str(uuid4()), "presentation": False}
        registered = {}
        finished = []
        queued_timeout = set()

        def bridge(action, params, **kwargs):
            self.assertEqual(params["target"], target)
            if action == "get_drawing_info":
                return {"civilDrawingUnits": "Meters"}
            if action == "get_edit_status":
                row = registered.get(params["request_id"])
                if row is None and params["request_id"] in queued_timeout:
                    return {"state": "error", "retryable": True}
                return {"state": "completed", "applied": row[1]["applied"], "result": row[1]} if row else {"state": "unknown"}
            if action == "list_profile_views":
                return {"profileViews": [{"name": "LINE1 - Profile", "handle": "AF", "stationStart": 0,
                                          "extents": {"min": [0, 0, 0], "max": [10, 20, 0]}}]}
            if action == "list_structures":
                return {"structures": []}
            self.assertEqual(action, "edit")
            req_id = params["request_id"]
            if req_id in registered:
                previous, reply = registered[req_id]
                self.assertEqual(previous, params)
                return reply
            if req_id == request["request_id"]:
                names = [op["op"] for op in params["operations"]]
                self.assertIn("pipe_network_add", names)
                self.assertIn("profile_view_add_parts", names)
                reply = {"applied": True}
            else:
                if not queued_timeout:
                    queued_timeout.add(req_id)
                    raise sewergems.BridgeError("Timed out before the CAD queue started")
                if not finished:
                    self.assertIn(req_id, queued_timeout)
                finished.append(req_id)
                reply = {"applied": len(finished) > 1, "request_id": req_id}
            registered[req_id] = params, reply
            return reply

        with patch.object(sewergems, "_plan", return_value=plan), \
             patch.object(sewergems, "_choose_parts", return_value=parts), \
             patch.object(sewergems, "call", side_effect=bridge):
            result = sewergems.build(request)
            self.assertTrue(result["coreApplied"])
            self.assertFalse(result["built"])
            self.assertIn("CAD queue", result["error"])
            self.assertFalse(finished)
            self.assertFalse(sewergems.build(request)["built"])
            self.assertEqual(len(finished), 1)
            self.assertFalse(sewergems.build(request)["built"])
            self.assertEqual(len(finished), 1)
            self.assertTrue(sewergems.build({**request, "finish_attempt": 2})["built"])
            self.assertEqual(len(finished), 2)
            self.assertFalse(sewergems.build({**request, "finish_attempt": 3})["built"])
            self.assertEqual(len(finished), 2)


if __name__ == "__main__":
    unittest.main()
