"""Road marking tool wrappers."""

from __future__ import annotations

from civil3d_mcp._instance import WRITE_ANN, call, mcp
from civil3d_mcp.models import LaneMarking, PedestrianCrossing


@mcp.tool(name="civil3d_draw_lane_marking",
          annotations={**WRITE_ANN, "title": "Draw lane marking"})
async def civil3d_draw_lane_marking(params: LaneMarking) -> str:
    """Draw a longitudinal lane marking polyline parallel to an alignment.
    Solid / dashed / double styles; geometry follows horizontal curvature
    via fixed-interval sampling. Layer auto-created."""
    return await call("civil3d_draw_lane_marking", params.model_dump())


@mcp.tool(name="civil3d_draw_pedestrian_crossing",
          annotations={**WRITE_ANN, "title": "Draw zebra crossing"})
async def civil3d_draw_pedestrian_crossing(params: PedestrianCrossing) -> str:
    """Draw a zebra crossing as perpendicular stripes centred on a station."""
    return await call("civil3d_draw_pedestrian_crossing", params.model_dump())
