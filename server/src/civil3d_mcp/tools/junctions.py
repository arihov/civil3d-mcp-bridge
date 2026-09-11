"""Junction / intersection design tools."""

from __future__ import annotations

from civil3d_mcp._instance import WRITE_ANN, call, mcp
from civil3d_mcp.models import JunctionFillets


@mcp.tool(name="civil3d_create_junction_corner_fillets",
          annotations={**WRITE_ANN, "title": "Lay out junction corner fillets"})
async def civil3d_create_junction_corner_fillets(params: JunctionFillets) -> str:
    """Draw a right-angle junction layout at a crossing point: four filleted
    kerb corners (tangent arcs) of the given radius, plus optional approach
    kerb lines and a central channelizing island. Roads are modelled as two
    virtual centrelines (A along bearing_deg, B perpendicular) with the given
    carriageway half-widths. Emits plain AutoCAD geometry — a design-assist
    starting layout, not a native Civil 3D Intersection object."""
    return await call("civil3d_create_junction_corner_fillets", params.model_dump(exclude_none=True))
