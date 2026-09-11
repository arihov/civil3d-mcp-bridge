"""Design & profile reports.

Pure-Python composition over the existing read-only bridge tools. These do not
require any C# change, so they are live the moment the package is installed.
Each returns a formatted string (markdown by default, or json / csv).
"""

from __future__ import annotations

import csv
import io
import json

from civil3d_mcp._instance import READ_ANN, as_json, mcp
from civil3d_mcp.bridge import invoke
from civil3d_mcp.models import (
    AlignmentReport,
    InventoryReport,
    ProfileReport,
    QuantityReport,
)


# ─── small formatting helpers ──────────────────────────────────────────────

def _f(x, nd: int = 3) -> str:
    return f"{x:.{nd}f}" if isinstance(x, (int, float)) else ("" if x is None else str(x))


def _md_table(headers: list[str], rows: list[list[str]]) -> str:
    out = ["| " + " | ".join(headers) + " |",
           "| " + " | ".join("---" for _ in headers) + " |"]
    out += ["| " + " | ".join(cells) + " |" for cells in rows]
    return "\n".join(out)


def _extract_list(data, *keys: str) -> list:
    if not isinstance(data, dict):
        return []
    for k in keys:
        v = data.get(k)
        if isinstance(v, list):
            return v
    return []


def _is_curve(t: str) -> bool:
    return any(w in t for w in ("PARABOLA", "CURVE", "ARC"))


def _is_tangent(t: str) -> bool:
    return any(w in t for w in ("TANGENT", "LINE"))


# ─── Profile design report ─────────────────────────────────────────────────

@mcp.tool(name="civil3d_profile_design_report",
          annotations={**READ_ANN, "title": "Vertical profile design report"})
async def civil3d_profile_design_report(params: ProfileReport) -> str:
    """Summarise a profile's vertical geometry: PVI table, grade breaks,
    curve count/lengths, min/max elevation and design stats. Formats:
    markdown (default), json, csv (PVI table)."""
    from civil3d_mcp.bridge import BridgeError
    try:
        data = await invoke("civil3d_get_profile_info",
                            {"alignment": params.alignment, "profile": params.profile})
    except BridgeError as exc:
        return as_json({"error": str(exc), "tool": "civil3d_profile_design_report"})

    entities = _extract_list(data, "entities")
    pvis = _extract_list(data, "pvis")
    etypes = [str(e.get("type", "")) for e in entities]
    curves = [e for e in entities if _is_curve(str(e.get("type", "")).upper())]
    tangents = [e for e in entities if _is_tangent(str(e.get("type", "")).upper())]
    grades = [abs(e.get("grade", 0) or 0) for e in tangents]
    start = data.get("startStation", 0) or 0
    end = data.get("endStation", 0) or 0

    stats = {
        "alignment": data.get("alignment"),
        "profile": data.get("name"),
        "profileType": data.get("profileType"),
        "startStation": start,
        "endStation": end,
        "length": end - start,
        "elevationMin": data.get("elevationMin"),
        "elevationMax": data.get("elevationMax"),
        "entityCount": len(entities),
        "curveCount": len(curves),
        "tangentCount": len(tangents),
        "pviCount": len(pvis),
        "maxAbsGrade": (max(grades) if grades else 0),
        "minCurveLength": (min((c.get("length", 0) for c in curves), default=0) if curves else 0),
    }

    if params.format == "json":
        return as_json({"stats": stats, "pvis": pvis, "entities": entities})

    if params.format == "csv":
        buf = io.StringIO()
        w = csv.writer(buf)
        w.writerow(["pvi_station", "elevation", "grade_in", "grade_out", "pvi_type"])
        for p in pvis:
            w.writerow([_f(p.get("station"), 2), _f(p.get("elevation"), 3),
                        _f(p.get("gradeIn"), 4), _f(p.get("gradeOut"), 4),
                        p.get("pviType", "")])
        return buf.getvalue()

    # markdown
    pvi_rows = [[_f(p.get("station"), 2), _f(p.get("elevation"), 3),
                 _f((p.get("gradeIn", 0) or 0) * 100, 2) + "%",
                 _f((p.get("gradeOut", 0) or 0) * 100, 2) + "%",
                 str(p.get("pviType", ""))] for p in pvis]
    ent_rows = [[str(e.get("type", "")), _f(e.get("startStation"), 2), _f(e.get("endStation"), 2),
                 _f(e.get("length"), 2), _f((e.get("grade", 0) or 0) * 100, 2) + "%"]
                for e in entities]
    lines = [
        f"## Profile design report — {stats['alignment']} / {stats['profile']}",
        "",
        f"- Type: {stats['profileType']}",
        f"- Station range: {_f(stats['startStation'],2)} → {_f(stats['endStation'],2)} "
        f"(length {_f(stats['length'],2)})",
        f"- Elevation: min {_f(stats['elevationMin'])}, max {_f(stats['elevationMax'])}",
        f"- Entities: {stats['tangentCount']} tangents, {stats['curveCount']} vertical curves, "
        f"{stats['pviCount']} PVIs",
        f"- Max absolute grade: {_f(stats['maxAbsGrade']*100,2)}%",
        f"- Shortest vertical curve: {_f(stats['minCurveLength'],2)}",
        "",
        "### PVI table",
        "",
        _md_table(["Station", "Elevation", "Grade in", "Grade out", "PVI type"], pvi_rows),
        "",
        "### Element table",
        "",
        _md_table(["Type", "Start", "End", "Length", "Grade"], ent_rows),
    ]
    return "\n".join(lines)


# ─── Alignment overview report ─────────────────────────────────────────────

@mcp.tool(name="civil3d_alignment_overview_report",
          annotations={**READ_ANN, "title": "Horizontal alignment design report"})
async def civil3d_alignment_overview_report(params: AlignmentReport) -> str:
    """Summarise a horizontal alignment: overall geometry, breakdown of line /
    arc / spiral elements, station range and the profiles it carries."""
    from civil3d_mcp.bridge import BridgeError
    try:
        info = await invoke("civil3d_get_alignment_info", {"name": params.alignment})
    except BridgeError as exc:
        return as_json({"error": str(exc), "tool": "civil3d_alignment_overview_report"})

    entities = _extract_list(info, "entities")
    kinds = {"line": 0, "arc": 0, "spiral": 0, "other": 0}
    for e in entities:
        t = str(e.get("type", "")).upper()
        if t == "LINE":
            kinds["line"] += 1
        elif t == "ARC":
            kinds["arc"] += 1
        elif t == "SPIRAL":
            kinds["spiral"] += 1
        else:
            kinds["other"] += 1
    curves = [e for e in entities if str(e.get("type", "")).upper() in ("ARC", "SPIRAL")]

    payload = {
        "alignment": info.get("name"),
        "description": info.get("description"),
        "style": info.get("style"),
        "site": info.get("site"),
        "length": info.get("length"),
        "startStation": info.get("startStation"),
        "endStation": info.get("endStation"),
        "elementCounts": kinds,
        "curveCount": len(curves),
        "shortestCurve": min((c.get("length", 0) for c in curves), default=0) if curves else 0,
        "profiles": _extract_list(info, "profiles"),
    }

    if params.format == "json":
        return as_json(payload)

    lines = [
        f"## Alignment overview — {payload['alignment']}",
        "",
        f"- Description: {payload['description'] or '(none)'}",
        f"- Site / style: {payload['site']} / {payload['style']}",
        f"- Length: {_f(payload['length'],3)}  "
        f"(stations {_f(payload['startStation'],2)} → {_f(payload['endStation'],2)})",
        f"- Elements: {kinds['line']} line, {kinds['arc']} arc, "
        f"{kinds['spiral']} spiral" + (f", {kinds['other']} other" if kinds['other'] else ""),
        f"- Curves: {payload['curveCount']}, shortest {_f(payload['shortestCurve'],2)}",
        f"- Profiles: {', '.join(str(p) for p in payload['profiles']) or '(none)'}",
    ]
    return "\n".join(lines)


# ─── Drawing inventory report ──────────────────────────────────────────────

@mcp.tool(name="civil3d_drawing_inventory_report",
          annotations={**READ_ANN, "title": "Drawing object inventory"})
async def civil3d_drawing_inventory_report(params: InventoryReport) -> str:
    """Whole-drawing dashboard: counts + names of alignments, profiles are
    omitted for brevity, surfaces, corridors, pipe networks and junctions
    present in the active Civil 3D drawing."""
    from civil3d_mcp.bridge import BridgeError
    sections: dict[str, dict] = {}
    errors: dict[str, str] = {}
    for key, tool, list_keys in [
        ("alignments", "civil3d_list_alignments", ("alignments",)),
        ("surfaces", "civil3d_list_surfaces", ("surfaces",)),
        ("corridors", "civil3d_list_corridors", ("corridors",)),
        ("pipeNetworks", "civil3d_list_pipe_networks", ("networks", "pipeNetworks", "pipe_networks")),
    ]:
        try:
            data = await invoke(tool, {})
        except BridgeError as exc:
            errors[key] = str(exc)
            continue
        items = _extract_list(data, *list_keys)
        sections[key] = {
            "count": data.get("count", len(items)),
            "names": [str(i.get("name", "?")) for i in items],
        }

    payload = {"sections": sections, "errors": errors}
    if params.format == "json":
        return as_json(payload)

    label = {"alignments": "Alignments", "surfaces": "Surfaces",
             "corridors": "Corridors", "pipeNetworks": "Pipe networks"}
    lines = ["## Drawing inventory", ""]
    rows = [[label.get(k, k), str(v["count"]),
             (", ".join(v["names"])[:120] + ("…" if len(", ".join(v["names"])) > 120 else "")) if v["names"] else "—"]
            for k, v in sections.items()]
    lines.append(_md_table(["Category", "Count", "Names"], rows))
    if errors:
        lines += ["", "### Errors", ""]
        lines += [f"- {k}: {msg}" for k, msg in errors.items()]
    return "\n".join(lines)


# ─── Quantity / volume report ──────────────────────────────────────────────

@mcp.tool(name="civil3d_quantity_report",
          annotations={**READ_ANN, "title": "Cut/fill volume report"})
async def civil3d_quantity_report(params: QuantityReport) -> str:
    """Compute and format the cut / fill / net volume between two surfaces
    (base vs comparison) using the bridge volume surface, as markdown or json."""
    from civil3d_mcp.bridge import BridgeError
    try:
        data = await invoke("civil3d_get_surface_volume",
                            {"base": params.base, "comparison": params.comparison})
    except BridgeError as exc:
        return as_json({"error": str(exc), "tool": "civil3d_quantity_report"})

    payload = {
        "base": data.get("baseSurface"),
        "comparison": data.get("comparisonSurface"),
        "cut": data.get("cutVolume"),
        "fill": data.get("fillVolume"),
        "net": data.get("netVolume"),
        "netGraph": data.get("netGraph"),
    }
    if params.format == "json":
        return as_json(payload)

    return "\n".join([
        f"## Quantity report — {payload['base']} vs {payload['comparison']}",
        "",
        _md_table(["Quantity", "Value"], [
            ["Cut", _f(payload["cut"], 2)],
            ["Fill", _f(payload["fill"], 2)],
            ["Net", _f(payload["net"], 2) + f" ({payload['netGraph']})"],
        ]),
    ])
