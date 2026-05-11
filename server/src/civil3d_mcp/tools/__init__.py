"""Import every tool submodule so their @mcp.tool decorators run.

Order doesn't matter — each module registers independently against the
shared `mcp` singleton in `_instance.py`.
"""

from civil3d_mcp.tools import (  # noqa: F401
    alignments,
    blocks,
    corridors,
    drawing,
    markings,
    masshaul,
    misc,
    pipes,
    points,
    profiles,
    sampling,
    surfaces,
)
