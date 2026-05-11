"""Pipe network + culvert tool wrappers.

The headline tool here is civil3d_create_culvert_at_chainage: given an
alignment, station, diameter, length, skew, and inverts, it builds the
complete pipe + inlet/outlet structures across the road and shows in
both plan and (any referencing) profile view.
"""

from __future__ import annotations

from civil3d_mcp._instance import READ_ANN, WRITE_ANN, call, mcp
from civil3d_mcp.models import CulvertAtChainage, PipeNetworkName


@mcp.tool(name="civil3d_list_pipe_networks",
          annotations={**READ_ANN, "title": "List pipe networks"})
async def civil3d_list_pipe_networks() -> str:
    """List pipe networks with pipe + structure counts."""
    return await call("civil3d_list_pipe_networks")


@mcp.tool(name="civil3d_get_pipe_network_info",
          annotations={**READ_ANN, "title": "Pipe network detail"})
async def civil3d_get_pipe_network_info(params: PipeNetworkName) -> str:
    """Full structure and pipe table for a named network."""
    return await call("civil3d_get_pipe_network_info", params.model_dump())


@mcp.tool(name="civil3d_list_pipe_parts",
          annotations={**READ_ANN, "title": "List parts list contents"})
async def civil3d_list_pipe_parts() -> str:
    """List pipe + structure families and sizes available in the drawing's
    parts list. Call this before civil3d_create_culvert_at_chainage to confirm
    the diameters / families you can ask for."""
    return await call("civil3d_list_pipe_parts")


@mcp.tool(name="civil3d_create_culvert_at_chainage",
          annotations={**WRITE_ANN, "title": "Place culvert at chainage"})
async def civil3d_create_culvert_at_chainage(params: CulvertAtChainage) -> str:
    """Place a complete culvert across an alignment from chainage + sizing.

    Behaviour:
      1. Finds (or creates) the named pipe network.
      2. Resolves the pipe family/size by inner diameter (mm).
      3. Resolves a structure family for inlet/outlet headwalls.
      4. Computes inlet/outlet world coordinates from alignment + skew + length.
      5. Inserts both structures, then the pipe between them, with explicit
         start/end points so the inverts hit the requested elevations.
      6. Returns the computed slope (m/m and %) plus inlet/outlet coordinates.

    Requires a parts list with a circular pipe size matching the diameter
    and at least one structure family. Use civil3d_list_pipe_parts to verify."""
    return await call("civil3d_create_culvert_at_chainage", params.model_dump(exclude_none=True))
