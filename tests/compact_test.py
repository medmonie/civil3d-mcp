"""Offline test of the compact tool description (C3D_MCP_COMPACT=1, roadmap #80) and multi-topic help."""
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "server"))
import catalog  # noqa: E402

full = catalog.tool_description("all")
os.environ["C3D_MCP_COMPACT"] = "1"
short = catalog.tool_description("all")
os.environ.pop("C3D_MCP_COMPACT")
assert len(short) < len(full) / 4, (len(short), len(full))
missing = [n for n in list(catalog.READ_ACTIONS) + list(catalog.EDIT_OPS) if n not in short]
assert not missing, missing
assert "help with topic" in short
both = catalog.help_data("pipe_break, network_trace")
assert set(both) == {"pipe_break", "network_trace"}, both.keys()
print(f"compact description {len(short)} chars vs {len(full)} ({100 * len(short) // len(full)} %), every name listed; multi-topic help ok")
print("ALL OK")
