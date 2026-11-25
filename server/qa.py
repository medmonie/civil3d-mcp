"""QA checks computed here from read actions (no extra plugin code needed)."""
from __future__ import annotations

import re
from typing import Any

from bridge_client import call

# Names Civil 3D generates itself, e.g. "Surface1", "Alignment - (3)", "Pipe Network - (2)".
DEFAULT_NAME_RX = re.compile(
    r"^(Surface|Alignment|Pipe Network|Network|Pressure Network|Pipe Run)[\s\-]*\(?\d+\)?$", re.IGNORECASE)


def check_layer_names(pattern: str, skip_xref_layers: bool = True) -> dict[str, Any]:
    try:
        rx = re.compile(pattern)
    except re.error as exc:
        raise ValueError(f"Invalid regular expression: {exc}") from None

    data = call("list_layers", {"limit": 20000})
    checked, bad = 0, []
    for layer in data["layers"]:
        name = layer["name"]
        if name in ("0", "Defpoints"):
            continue
        if skip_xref_layers and (layer.get("xref") or "|" in name):
            continue
        checked += 1
        if not rx.fullmatch(name):
            bad.append(name)
    return {
        "pattern": pattern,
        "checked": checked,
        "nonConformingCount": len(bad),
        "nonConforming": sorted(bad)[:200],
        "listTruncated": len(bad) > 200,
        "layersTruncatedAtSource": data.get("truncated", False),
    }


def check_default_names() -> dict[str, Any]:
    flagged: dict[str, list[str]] = {}
    totals: dict[str, int] = {}
    truncated: list[str] = []
    for label, method, key in (
        ("surfaces", "list_surfaces", "surfaces"),
        ("alignments", "list_alignments", "alignments"),
        ("pipeNetworks", "list_pipe_networks", "pipeNetworks"),
        ("pressureNetworks", "list_pressure_networks", "pressureNetworks"),
    ):
        data = call(method, {"limit": 5000})
        items = data[key]
        # 'total' counts every object even when the list was cut at 'limit'; fall back for actions without paging.
        totals[label] = data.get("total", len(items))
        if data.get("truncated"):
            truncated.append(label)
        flagged[label] = [i["name"] for i in items if DEFAULT_NAME_RX.match(i.get("name") or "")]
    return {"totals": totals, "defaultNamed": flagged, "totalFlagged": sum(len(v) for v in flagged.values()),
            **({"truncated": truncated} if truncated else {})}
