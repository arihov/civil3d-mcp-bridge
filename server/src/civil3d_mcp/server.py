"""civil3d_mcp.server - FastMCP entry point.

Imports the tools subpackage so every @mcp.tool decorator registers against
the shared mcp singleton, then runs FastMCP over stdio.
"""

from __future__ import annotations

import asyncio
import logging

from civil3d_mcp._instance import mcp
from civil3d_mcp.bridge import close as close_bridge
from civil3d_mcp import tools  # noqa: F401  side-effect import registers all tools


def main() -> None:
    """Entry point for the `civil3d-mcp` console script."""
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
    try:
        mcp.run()
    finally:
        try:
            asyncio.run(close_bridge())
        except RuntimeError:
            pass


if __name__ == "__main__":
    main()
