"""Block-insertion tool wrappers — signs, KM posts, batch furniture."""

from __future__ import annotations

from civil3d_mcp._instance import READ_ANN, WRITE_ANN, call, mcp
from civil3d_mcp.models import (
    InsertBlock,
    InsertBlocksBatch,
    KmPostSeries,
    ListBlocks,
    Signpost,
)


@mcp.tool(name="civil3d_list_blocks",
          annotations={**READ_ANN, "title": "List block definitions"})
async def civil3d_list_blocks(params: ListBlocks = ListBlocks()) -> str:
    """List block definitions in the drawing, optionally filtered by name substring.
    Skips anonymous and layout blocks."""
    return await call("civil3d_list_blocks", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_insert_block_at_chainage",
          annotations={**WRITE_ANN, "title": "Insert block at chainage"})
async def civil3d_insert_block_at_chainage(params: InsertBlock) -> str:
    """Insert a block reference at (alignment, station, offset) with rotation
    matching alignment direction (along/perpendicular/absolute). Optional
    attributes dict fills in block attribute tags. Auto-creates the layer
    if missing."""
    return await call("civil3d_insert_block_at_chainage", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_insert_blocks_batch",
          annotations={**WRITE_ANN, "title": "Bulk block insertion"})
async def civil3d_insert_blocks_batch(params: InsertBlocksBatch) -> str:
    """Bulk insert blocks. Per-item dicts override top-level defaults
    (block/alignment/layer/scale). Returns inserted count + first 20 errors."""
    return await call("civil3d_insert_blocks_batch", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_create_signpost_at_chainage",
          annotations={**WRITE_ANN, "title": "Place road sign"})
async def civil3d_create_signpost_at_chainage(params: Signpost) -> str:
    """Opinionated wrapper for road sign placement. Side+offset (default
    3 m right of CL), perpendicular rotation, layer C-ROAD-SIGN, fills
    LEGEND / CODE / CHAINAGE attributes."""
    return await call("civil3d_create_signpost_at_chainage", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_create_km_post_series",
          annotations={**WRITE_ANN, "title": "Auto-place KM post series"})
async def civil3d_create_km_post_series(params: KmPostSeries) -> str:
    """Place KM (or hectometre) posts at fixed interval along an alignment.
    Default 1000 m spacing, right side, KM attribute populated with km value."""
    return await call("civil3d_create_km_post_series", params.model_dump(exclude_none=True))
