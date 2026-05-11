"""Sample line group tool wrappers."""

from __future__ import annotations

from civil3d_mcp._instance import READ_ANN, WRITE_ANN, call, mcp
from civil3d_mcp.models import SampleLineGroupCreate, SampleLineGroupsList


@mcp.tool(name="civil3d_create_sample_line_group",
          annotations={**WRITE_ANN, "title": "Create sample line group"})
async def civil3d_create_sample_line_group(params: SampleLineGroupCreate) -> str:
    """Create a sample line group on an alignment, populated by station
    range at a fixed interval. Attaches every available surface as a
    section data source so section views can be produced immediately."""
    return await call("civil3d_create_sample_line_group", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_list_sample_line_groups",
          annotations={**READ_ANN, "title": "List sample line groups"})
async def civil3d_list_sample_line_groups(params: SampleLineGroupsList = SampleLineGroupsList()) -> str:
    """List sample line groups, optionally filtered to one alignment."""
    return await call("civil3d_list_sample_line_groups", params.model_dump(exclude_none=True))
