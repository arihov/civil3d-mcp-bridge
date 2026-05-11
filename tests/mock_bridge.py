"""Mock Civil 3D bridge for offline testing.

Runs an HTTP server on 127.0.0.1:7800 that speaks the same /health +
/invoke protocol as the real C# bridge, but answers with plausible
canned data. Used to exercise the Python MCP server end-to-end without
needing Civil 3D installed.

Run:
    python tests/mock_bridge.py

Then in another shell:
    python tests/integration_test.py --against-mock

Or point Claude Desktop at this bridge by setting:
    CIVIL3D_BRIDGE_URL=http://127.0.0.1:7800
in the civil3d entry in claude_desktop_config.json.
"""

from __future__ import annotations

import json
import logging
from typing import Any

from aiohttp import web

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s: %(message)s")
log = logging.getLogger("mock-bridge")

VERSION = "0.2.0-mock"


# ---------------------------------------------------------------------------
# Canned responses, keyed by tool name. The shape of each response mirrors
# what the real C# bridge returns so the Python typed wrappers don't have
# to special-case mock vs real.
# ---------------------------------------------------------------------------

def _alignments_list(_: dict[str, Any]) -> dict[str, Any]:
    return {
        "count": 2,
        "alignments": [
            {
                "name": "CL-01",
                "site": "<None>",
                "style": "Existing",
                "length": 4523.512,
                "startStation": 0.0,
                "endStation": 4523.512,
                "stationEquationCount": 0,
                "designSpeedCount": 3,
            },
            {
                "name": "Service-Rd-A",
                "site": "<None>",
                "style": "Proposed",
                "length": 612.0,
                "startStation": 0.0,
                "endStation": 612.0,
                "stationEquationCount": 0,
                "designSpeedCount": 1,
            },
        ],
    }


def _alignment_info(args: dict[str, Any]) -> dict[str, Any]:
    return {
        "name": args.get("name", "CL-01"),
        "length": 4523.512,
        "startStation": 0.0,
        "endStation": 4523.512,
        "entities": [
            {"type": "Line", "subEntityType": "Line", "length": 1200.0},
            {"type": "Curve", "subEntityType": "Arc", "length": 215.4, "radius": 800.0},
            {"type": "Line", "subEntityType": "Line", "length": 950.0},
        ],
        "designSpeeds": [
            {"number": 1, "station": 0.0, "speed": 80.0},
            {"number": 2, "station": 1200.0, "speed": 60.0},
            {"number": 3, "station": 2500.0, "speed": 80.0},
        ],
        "stationEquations": [],
    }


def _point_at_chainage(args: dict[str, Any]) -> dict[str, Any]:
    s = float(args.get("station", 0.0))
    offset = float(args.get("offset", 0.0))
    # Pretend the alignment is a straight line N400000+s along easting E500000+offset.
    return {
        "alignment": args.get("alignment"),
        "station": s,
        "offset": offset,
        "easting": 500_000.0 + offset,
        "northing": 400_000.0 + s,
        "elevation": None if not args.get("profile") else 1234.5,
        "tangentDirection": 1.5707963,  # pi/2 → due north
    }


def _surfaces_list(_: dict[str, Any]) -> dict[str, Any]:
    return {
        "count": 2,
        "surfaces": [
            {"name": "EG", "type": "TinSurface", "minElevation": 1200.0,
             "maxElevation": 1450.5, "area2d": 1.2e6, "area3d": 1.21e6, "triangleCount": 45123},
            {"name": "FG-Final", "type": "TinSurface", "minElevation": 1205.0,
             "maxElevation": 1448.0, "area2d": 1.18e6, "area3d": 1.19e6, "triangleCount": 38201},
        ],
    }


def _corridors_list(_: dict[str, Any]) -> dict[str, Any]:
    return {
        "count": 1,
        "corridors": [
            {"name": "Corridor-CL-01", "isOutOfDate": False, "baselineCount": 1,
             "regionCount": 3},
        ],
    }


def _drawing_info(_: dict[str, Any]) -> dict[str, Any]:
    return {
        "name": "mock-drawing.dwg",
        "filename": "C:\\Mock\\mock-drawing.dwg",
        "isReadOnly": False,
        "isUnnamed": False,
        "dbVersion": "AC1032",
        "counts": {"alignments": 2, "corridors": 1, "surfaces": 2,
                   "cogoPoints": 1247, "pipeNetworks": 1, "assemblies": 2},
    }


def _list_pipe_parts(_: dict[str, Any]) -> dict[str, Any]:
    return {
        "partsListName": "Default",
        "pipeFamilies": [
            {"name": "Concrete Pipe", "shape": "Circular",
             "innerDiametersMm": [300, 450, 600, 900, 1200, 1500]},
            {"name": "PVC Pipe", "shape": "Circular",
             "innerDiametersMm": [100, 150, 200, 300]},
        ],
        "structureFamilies": [
            {"name": "Headwall Concrete", "shape": "Rectangular"},
            {"name": "Null Structure", "shape": "Null"},
        ],
    }


def _create_culvert(args: dict[str, Any]) -> dict[str, Any]:
    return {
        "alignment": args.get("alignment"),
        "station": args.get("station"),
        "diameter": args.get("diameter"),
        "length": args.get("length"),
        "network": args.get("network_name"),
        "skewDegrees": args.get("skew_degrees", 0.0),
        "slope": (args.get("invert_in", 0) - args.get("invert_out", 0)) / max(args.get("length", 1), 1e-6),
        "slopePercent": 100 * (args.get("invert_in", 0) - args.get("invert_out", 0)) / max(args.get("length", 1), 1e-6),
        "inlet": {"x": 500_010.0, "y": 400_500.0, "structureName": "STR-101"},
        "outlet": {"x": 499_990.0, "y": 400_500.0, "structureName": "STR-102"},
        "pipeName": "PIPE-101",
    }


def _generic_ok(tool: str, args: dict[str, Any]) -> dict[str, Any]:
    """Fallback for tools without a more specific canned response."""
    return {
        "_mock": True,
        "_note": "mock bridge: generic ok response — see tests/mock_bridge.py to add detail",
        "tool": tool,
        "argsReceived": args,
    }


HANDLERS: dict[str, Any] = {
    "civil3d_list_alignments": _alignments_list,
    "civil3d_get_alignment_info": _alignment_info,
    "civil3d_point_at_chainage": _point_at_chainage,
    "civil3d_get_xy_at_station": lambda a: {
        "alignment": a.get("alignment"), "station": a.get("station"),
        "easting": 500000.0 + a.get("offset", 0), "northing": 400000.0 + a.get("station", 0),
    },
    "civil3d_list_surfaces": _surfaces_list,
    "civil3d_list_corridors": _corridors_list,
    "civil3d_list_pipe_parts": _list_pipe_parts,
    "civil3d_get_drawing_info": _drawing_info,
    "civil3d_create_culvert_at_chainage": _create_culvert,
    "civil3d_list_layers": lambda a: {
        "count": 4,
        "layers": [
            {"name": "0", "isFrozen": False, "isOff": False, "isLocked": False,
             "isCurrent": True, "colorIndex": 7, "lineweight": "Default"},
            {"name": "C-ROAD-MARK", "isFrozen": False, "isOff": False, "isLocked": False,
             "isCurrent": False, "colorIndex": 7, "lineweight": "Default"},
            {"name": "C-ROAD-SIGN", "isFrozen": False, "isOff": False, "isLocked": False,
             "isCurrent": False, "colorIndex": 1, "lineweight": "Default"},
            {"name": "C-ROAD-KMPOST", "isFrozen": False, "isOff": False, "isLocked": False,
             "isCurrent": False, "colorIndex": 3, "lineweight": "Default"},
        ],
    },
    "civil3d_list_blocks": lambda a: {
        "count": 3,
        "blocks": [
            {"name": "SIGN_GENERIC", "hasAttributes": True,
             "attributeTags": ["LEGEND", "CODE", "CHAINAGE"]},
            {"name": "KM_POST", "hasAttributes": True, "attributeTags": ["KM"]},
            {"name": "TREE_LARGE", "hasAttributes": False, "attributeTags": []},
        ],
    },
    "civil3d_list_pipe_networks": lambda a: {
        "count": 1,
        "networks": [{"name": "Drainage-Main", "pipeCount": 12, "structureCount": 8}],
    },
    "civil3d_list_point_groups": lambda a: {
        "count": 2,
        "groups": [{"name": "_All Points", "pointCount": 1247},
                   {"name": "IP", "pointCount": 23}],
    },
    "civil3d_list_assemblies": lambda a: {
        "count": 2,
        "assemblies": [{"name": "Urban-2L", "groupCount": 4},
                       {"name": "Rural-2L", "groupCount": 4}],
    },
    "civil3d_list_sample_line_groups": lambda a: {
        "count": 1,
        "groups": [{"name": "SLG-20m", "alignment": "CL-01",
                    "sampleLineCount": 226, "materialListCount": 1}],
    },
    "civil3d_list_profiles": lambda a: {
        "count": 2,
        "profiles": [{"name": "EG", "type": "Existing Ground", "length": 4523.512},
                     {"name": "FG", "type": "Design", "length": 4523.512}],
    },
    "civil3d_list_points": lambda a: {
        "count": 1247, "returned": min(int(a.get("limit", 100)), 1247),
        "offset": int(a.get("offset", 0)),
        "nextOffset": int(a.get("offset", 0)) + min(int(a.get("limit", 100)), 1247),
        "points": [{"number": i + 1, "northing": 400000 + i, "easting": 500000 + i,
                    "elevation": 1200 + i * 0.1, "description": f"GP-{i+1}"}
                   for i in range(min(int(a.get("limit", 100)), 5))],
    },
}


async def health(_request: web.Request) -> web.Response:
    return web.json_response({
        "ok": True,
        "version": VERSION,
        "toolCount": len(HANDLERS),
        "tools": sorted(HANDLERS.keys()),
        "mock": True,
    })


async def invoke(request: web.Request) -> web.Response:
    try:
        body = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"ok": False, "error": "invalid JSON"}, status=400)

    tool = body.get("tool")
    args = body.get("args") or {}
    if not tool:
        return web.json_response({"ok": False, "error": "missing 'tool'"}, status=400)

    log.info(f"invoke {tool}({args!r})")
    handler = HANDLERS.get(tool)
    if handler is None:
        # Fall through to a generic ok response — lets tests cover tools
        # without us having to hand-write canned data for every one.
        result = _generic_ok(tool, args)
    else:
        try:
            result = handler(args)
        except Exception as exc:  # noqa: BLE001
            return web.json_response({"ok": False, "error": f"mock handler exception: {exc}"},
                                     status=500)
    return web.json_response({"ok": True, "result": result})


def make_app() -> web.Application:
    app = web.Application()
    app.router.add_get("/health", health)
    app.router.add_post("/invoke", invoke)
    return app


if __name__ == "__main__":
    log.info(f"mock bridge starting on http://127.0.0.1:7800 — {len(HANDLERS)} handlers")
    web.run_app(make_app(), host="127.0.0.1", port=7800, access_log=None)
