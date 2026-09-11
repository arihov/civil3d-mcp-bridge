"""Vehicle tracking proxies — swept-path envelope + turning-path arc."""

from __future__ import annotations

from civil3d_mcp._instance import WRITE_ANN, call, mcp
from civil3d_mcp.models import SweptEnvelope, TurningPathArc


@mcp.tool(name="civil3d_swept_path_envelope",
          annotations={**WRITE_ANN, "title": "Swept-path envelope polylines"})
async def civil3d_swept_path_envelope(params: SweptEnvelope) -> str:
    """Draw the inner + outer boundary polylines of a swept path by walking an
    alignment at two lateral offsets between two chainages. A quick, Civil-3D-
    native proxy for tracking: shows the corridor a vehicle of a given half
    width carves through a curve. Not a substitute for Autodesk Vehicle Tracking
    per-template compliance (see docs)."""
    return await call("civil3d_swept_path_envelope", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_create_turning_path_arc",
          annotations={**WRITE_ANN, "title": "Single-arc turning-path alignment"})
async def civil3d_create_turning_path_arc(params: TurningPathArc) -> str:
    """Create a single-arc Civil 3D alignment from a centre, design radius and
    start/end bearings (degrees, CCW). Handy for quick turning templates and
    fillet centrelines. The arc is sampled to <=5 degrees per vertex before
    alignment creation."""
    return await call("civil3d_create_turning_path_arc", params.model_dump(exclude_none=True))
