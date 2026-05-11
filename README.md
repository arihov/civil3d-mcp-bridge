# civil3d-mcp

A Model Context Protocol bridge that exposes the Autodesk **Civil 3D 2025/2026** .NET
API to Claude (or any MCP client). Talk to Civil 3D in natural language — drive
alignments, profiles, corridors, surfaces, points, pipe networks, blocks,
road markings, mass haul and drawing utilities.

> **Status**: 0.2.0 — 66 tools across 14 domains. Built for personal /
> consultancy use on Windows with Civil 3D 2025 or 2026.

---

## Architecture

```
┌──────────────────┐   stdio    ┌──────────────────────┐   HTTP   ┌──────────────────────┐
│  Claude Desktop  │ ◄──────►  │  Python FastMCP      │ ◄──────► │  C# in-process       │
│  (or any MCP     │            │  (civil3d_mcp)       │          │  bridge in Civil 3D  │
│   client)        │            │  console script:     │          │  Civil3DMcpBridge.dll│
└──────────────────┘            │  civil3d-mcp         │          │  127.0.0.1:7800      │
                                └──────────────────────┘          └──────────┬───────────┘
                                                                             │
                                                                  ┌──────────▼───────────┐
                                                                  │  Civil 3D .NET API   │
                                                                  │  (AeccDb, AcDb, …)   │
                                                                  └──────────────────────┘
```

* **C# add-in** (`plugin/`) — a NetLoadable bundle that boots an HTTP listener
  the moment Civil 3D loads it. Worker threads marshal back to the Civil 3D
  main UI thread via `Application.Idle` + `ConcurrentQueue<Action>` so every
  database write happens on the correct thread inside a `LockDocument()` +
  `Transaction` scope.
* **Python MCP server** (`server/`) — FastMCP wrapper. Each tool is a thin
  Pydantic-validated proxy that POSTs JSON to the bridge and returns the
  response verbatim. Two meta-tools (`civil3d_bridge_health`,
  `civil3d_call`) round out the surface.

---

## Quick start

From a PowerShell prompt in the repo root, on the Windows workstation that
has Civil 3D installed:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The installer will:

1. Verify prerequisites (Civil 3D install path, .NET 8 SDK, Python 3.11+).
2. `dotnet build -c Release` the plugin.
3. Deploy the bundle to
   `%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle\Contents\`.
4. `pip install -e server\` — creates the `civil3d-mcp` console script.
5. Merge a `civil3d` entry into
   `%APPDATA%\Claude\claude_desktop_config.json`.

Then:

1. Start Civil 3D and open any DWG.
2. At the command line, type `MCPSTATUS` — should show
   `listening on http://127.0.0.1:7800`.
3. Restart Claude Desktop. The hammer icon should now list ~66 `civil3d_*`
   tools.

See [`docs/INSTALL.md`](docs/INSTALL.md) for manual install + troubleshooting.

---

## Tool inventory (66)

| Domain | Tools |
|--------|-------|
| **Meta** (2) | `civil3d_bridge_health`, `civil3d_call` |
| **Alignments** (10) | `list_alignments`, `get_alignment_info`, `get_xy_at_station`, `get_station_at_xy`, `point_at_chainage`, `export_alignment_geometry`, `create_alignment_from_polyline`, `create_offset_alignment`, `set_design_speed` |
| **Profiles** (8) | `list_profiles`, `get_profile_info`, `get_elevation_at_station`, `add_vertical_curve`, `create_profile_from_surface`, `create_layout_profile`, `add_pvi`, `create_profile_view` |
| **Corridors** (10) | `list_corridors`, `get_corridor_info`, `list_assemblies`, `rebuild_corridor`, `set_region_assembly`, `create_corridor`, `add_baseline_region`, `set_region_target_surface`, `export_corridor_sections` |
| **Sampling** (3) | `create_sample_line_group`, `list_sample_line_groups`, `sample_surface_along_alignment` |
| **Surfaces** (8) | `list_surfaces`, `get_surface_volume`, `get_elevation_at_xy`, `create_tin_surface`, `create_surface_from_points`, `create_persistent_volume_surface`, `add_breakline_from_polyline`, `add_points_to_surface` |
| **Points** (6) | `list_points`, `create_point`, `import_points_csv`, `export_points_csv`, `list_point_groups`, `create_point_group` |
| **Pipe networks** (4) | `list_pipe_networks`, `get_pipe_network_info`, `list_pipe_parts`, **`create_culvert_at_chainage`** ⭐ |
| **Blocks** (5) | `list_blocks`, `insert_block_at_chainage`, `insert_blocks_batch`, **`create_signpost_at_chainage`** ⭐, **`create_km_post_series`** ⭐ |
| **Road markings** (2) | `draw_lane_marking`, `draw_pedestrian_crossing` |
| **Mass haul** (2) | `compute_quantity_takeoff`, `export_mass_haul_csv` |
| **Drawing utils** (8) | `get_drawing_info`, `save_drawing`, `zoom_to_alignment`, `zoom_extents`, `list_layers`, `create_layer`, `set_current_layer`, `run_command` |

Full per-tool reference: [`docs/TOOLS.md`](docs/TOOLS.md).

---

## Headline automations

These three pack the most operational value — see
[`docs/AUTOMATION_RECIPES.md`](docs/AUTOMATION_RECIPES.md) for prompt
patterns:

* ⭐ **Culvert from chainage + sizing** — give an alignment, station, pipe
  diameter and length; the bridge places a circular pipe with two
  headwall structures perpendicular to the alignment (with optional
  skew), creates the `DRAINAGE` network if it doesn't exist, derives
  inverts from the FG profile when not supplied, and labels the
  centreline at the invert.
* ⭐ **Signpost / km-post placement at chainage** — opinionated wrappers
  around block insertion. `create_signpost_at_chainage` takes a sign
  code (R1-1, W1-1, GS-1 …), legend text, station and side;
  `create_km_post_series` walks the full alignment and drops a KM post
  every N metres.
* ⭐ **Road markings that follow geometry** — `draw_lane_marking` produces
  solid / dashed / double polylines that follow alignment curvature
  with configurable dash + gap; `draw_pedestrian_crossing` renders a
  zebra perpendicular to the alignment.

---

## Layout

```
civil3d-mcp/
├── install.ps1                  # one-shot Windows installer
├── plugin/                      # C# in-process add-in
│   ├── Civil3DMcpBridge.csproj
│   ├── PackageContents.xml
│   └── src/
│       ├── Plugin.cs            # IExtensionApplication, command handlers
│       ├── BridgeServer.cs      # HttpListener at 127.0.0.1:7800
│       ├── MainThreadDispatcher.cs
│       ├── ToolRegistry.cs
│       ├── Json.cs              # tiny non-Newtonsoft JSON helper
│       ├── GeometryHelpers.cs
│       └── Tools/               # one file per domain
│           ├── AlignmentTools.cs
│           ├── AlignmentEditTools.cs
│           ├── ProfileTools.cs
│           ├── ProfileEditTools.cs
│           ├── CorridorTools.cs
│           ├── SamplingTools.cs
│           ├── SurfaceTools.cs
│           ├── SurfaceEditTools.cs
│           ├── PointTools.cs
│           ├── PipeNetworkTools.cs
│           ├── BlockTools.cs
│           ├── MarkingTools.cs
│           ├── MassHaulTools.cs
│           └── DrawingTools.cs
├── server/                      # Python FastMCP wrapper
│   ├── pyproject.toml
│   └── src/civil3d_mcp/
│       ├── __init__.py
│       ├── _instance.py         # FastMCP singleton + READ/WRITE annotations + call()
│       ├── bridge.py            # httpx async client, BridgeError
│       ├── server.py            # main() entry point, meta tools
│       ├── models.py            # 54 Pydantic input models
│       └── tools/
│           ├── __init__.py
│           ├── alignments.py
│           ├── profiles.py
│           ├── corridors.py
│           ├── surfaces.py
│           ├── points.py
│           ├── pipes.py
│           ├── blocks.py
│           ├── markings.py
│           ├── masshaul.py
│           ├── sampling.py
│           ├── drawing.py
│           └── misc.py
├── tests/
│   ├── mock_bridge.py           # aiohttp simulator; canned responses for every tool
│   ├── test_models.py           # validates all 54 Pydantic models
│   ├── integration_test.py      # read-only smoke against a real bridge
│   ├── smoke.ps1                # PowerShell smoke test
│   └── README.md
└── docs/
    ├── ARCHITECTURE.md          # how it's wired, how to add a tool
    ├── INSTALL.md               # what install.ps1 does, manual fallback
    ├── TOOLS.md                 # per-tool reference
    ├── AUTOMATION_RECIPES.md    # prompt patterns for headline automations
    └── TESTING.md
```

---

## License & support

Internal SANS LIMITE / personal use. No warranty. The bridge runs only
on `127.0.0.1` and does **not** listen on any external interface.

If a tool call fails, check:

1. Civil 3D is open and the add-in loaded (`MCPSTATUS` at command line).
2. The Python server is wired into Claude Desktop config.
3. Read the error in Civil 3D's command line — `ToolException` paths
   give human-readable hints.

For deeper diagnostics see [`docs/TESTING.md`](docs/TESTING.md).
