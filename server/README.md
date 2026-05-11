# civil3d-mcp (Python server)

Python FastMCP wrapper for the civil3d-mcp C# bridge.

See the [top-level README](../README.md) for the full architecture
overview, install instructions, and tool reference. This package is the
Python half — it doesn't do anything useful unless the C# plugin is
loaded inside a running Civil 3D session.

## Install

From the parent repo:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Or just the Python piece:

```bash
pip install -e .
```

## Entry points

* `civil3d-mcp` — start the FastMCP server over stdio (used by Claude
  Desktop and other MCP clients).
* `civil3d-mcp-mock` — start the mock bridge for offline testing
  (see `../tests/mock_bridge.py`).

## Layout

```
civil3d_mcp/
├── __init__.py         # version
├── _instance.py        # FastMCP singleton + READ/WRITE annotations + call()
├── bridge.py           # httpx async client, BridgeError
├── server.py           # main() entry + meta tools (bridge_health, call)
├── models.py           # 54 Pydantic input models
└── tools/              # 12 modules, 64 tools
    ├── alignments.py
    ├── profiles.py
    ├── corridors.py
    ├── surfaces.py
    ├── points.py
    ├── pipes.py
    ├── blocks.py
    ├── markings.py
    ├── masshaul.py
    ├── sampling.py
    ├── drawing.py
    └── misc.py
```
