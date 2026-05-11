"""Mass haul / earthwork quantity tool wrappers."""

from __future__ import annotations

from civil3d_mcp._instance import READ_ANN, WRITE_ANN, call, mcp
from civil3d_mcp.models import ExportMassHaul, QuantityTakeoff


@mcp.tool(name="civil3d_compute_quantity_takeoff",
          annotations={**READ_ANN, "title": "Quantity takeoff summary"})
async def civil3d_compute_quantity_takeoff(params: QuantityTakeoff) -> str:
    """Read per-section cut/fill/net from a computed material list on a
    sample line group, plus cumulative totals. Requires materials to have
    been computed already (Analyze → Volumes Dashboard in Civil 3D)."""
    return await call("civil3d_compute_quantity_takeoff", params.model_dump())


@mcp.tool(name="civil3d_export_mass_haul_csv",
          annotations={**WRITE_ANN, "title": "Export mass-haul to CSV",
                       "destructiveHint": False})
async def civil3d_export_mass_haul_csv(params: ExportMassHaul) -> str:
    """Dump the mass-haul table (station, cut, fill, net, cumulative) to CSV."""
    return await call("civil3d_export_mass_haul_csv", params.model_dump())
