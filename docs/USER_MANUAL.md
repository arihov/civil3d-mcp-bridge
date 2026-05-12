---
title: "civil3d-mcp — User Manual"
subtitle: "Talk to Autodesk Civil 3D 2025/2026 in natural language via Claude (or any MCP client)"
author: "civil3d-mcp 0.2.0"
date: "2026"
geometry: margin=2.2cm
fontsize: 11pt
linkcolor: blue
toc: true
toc-depth: 3
---

\newpage

# 1. What is civil3d-mcp?

**civil3d-mcp** is a Model Context Protocol (MCP) bridge that exposes the
Autodesk Civil 3D 2025/2026 .NET API to Claude Desktop (or any MCP
client). Once installed, you can drive Civil 3D in plain English:

> *"List the alignments in the active drawing."*
> *"Sample the existing-ground surface elevation at station 1+250 on
> alignment R1."*
> *"Export all COGO points from the BENCHMARKS group to
> `C:\Projects\survey.csv`."*

The bridge translates your natural-language requests into typed,
schema-validated API calls. Civil 3D executes them on its main UI thread
inside a transaction, and the response comes back to Claude as JSON.

## Highlights

- **64 tools** across 14 domains: alignments, profiles, corridors,
  surfaces, points, pipe networks, blocks, road markings, mass haul,
  sample-line groups, drawing utilities.
- **Local-only** — the bridge listens on `127.0.0.1:7800`. Nothing leaves
  your machine.
- **Civil 3D 2026 ready** — built and tested against the C3D 2026 .NET API.
- **Schema-validated** — every tool argument is a Pydantic model, so
  Claude knows exactly what each tool accepts.

---

\newpage

# 2. Architecture

```
+------------------+   stdio   +----------------------+   HTTP   +----------------------+
|  Claude Desktop  | <------>  |  Python FastMCP      | <----->  |  C# in-process       |
|  (or any MCP     |           |  (civil3d_mcp)       |          |  bridge in Civil 3D  |
|   client)        |           |  console script:     |          |  Civil3DMcpBridge.dll|
+------------------+           |  civil3d-mcp         |          |  127.0.0.1:7800      |
                               +----------------------+          +-----------+----------+
                                                                             |
                                                                  +----------v-----------+
                                                                  |  Civil 3D .NET API   |
                                                                  |  (AeccDb, AcDb, ...) |
                                                                  +----------------------+
```

Three layers:

1. **C# add-in** (`Civil3DMcpBridge.dll`) — a NetLoadable bundle that
   boots an HTTP listener the moment Civil 3D loads it. Every tool call
   is marshalled to the Civil 3D main UI thread inside a
   `LockDocument()` + `Transaction` scope.
2. **Python MCP server** (`civil3d-mcp` console script) — FastMCP
   wrapper. Each tool is a thin Pydantic-validated proxy that POSTs JSON
   to the bridge.
3. **Claude Desktop** (or any MCP client) — runs the Python server over
   stdio and presents the tools as natural-language-callable functions.

---

\newpage

# 3. System requirements

| Component | Minimum |
|-----------|---------|
| OS | Windows 10/11 x64 |
| Civil 3D | 2025 or 2026 (with the Civil 3D vertical, not just AutoCAD) |
| .NET SDK | .NET 8 (only needed at install time, to build the C# add-in) |
| Python | 3.11+ |
| Claude Desktop | Latest |
| Disk | < 50 MB |
| Network | None — `127.0.0.1` only |

**Important**: You need a *real* Civil 3D install. The bridge depends on
`AeccDbMgd.dll` from `C:\Program Files\Autodesk\AutoCAD 2026\C3D\`,
which only ships with Civil 3D, not plain AutoCAD.

---

\newpage

# 4. Installation

## 4.1 One-shot installer (recommended)

From a PowerShell prompt in the cloned repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The installer will:

1. Verify prerequisites (Civil 3D path, .NET 8 SDK, Python 3.11+).
2. `dotnet build -c Release` the plugin.
3. Deploy the bundle to
   `%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle\Contents\`.
4. `pip install -e server\` — creates the `civil3d-mcp` console script.
5. Merge a `civil3d` entry into
   `%APPDATA%\Claude\claude_desktop_config.json`.

Available switches:

- `-Civil3DPath "C:\Program Files\Autodesk\AutoCAD 2025"` — point at a
  different Civil 3D install.
- `-PerMachine` — install the bundle under `C:\ProgramData` instead of
  `%APPDATA%` (needs admin).
- `-SkipBuild` — reuse a previously-built DLL.
- `-SkipPython` — plugin-only deployment (you'll wire up the Python
  server manually).
- `-PythonExe python3.11` — pin to a specific interpreter.

## 4.2 Manual install

If the script can't run for some reason:

```powershell
# 1. Build the add-in
cd plugin
dotnet build -c Release

# 2. Deploy the bundle
$bundle = "$env:APPDATA\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle"
$contents = "$bundle\Contents"
mkdir $contents -Force
copy PackageContents.xml "$bundle\PackageContents.xml"
copy bin\Release\net8.0-windows\Civil3DMcpBridge.* $contents

# 3. Install Python package
cd ..\server
pip install -e .

# 4. Wire up Claude Desktop config (see section 5 below)
```

\newpage

# 5. Configuring Claude Desktop

Open `%APPDATA%\Claude\claude_desktop_config.json` (full path:
`C:\Users\<you>\AppData\Roaming\Claude\claude_desktop_config.json`).
The file must contain **one** top-level JSON object. Add a `civil3d`
entry under `mcpServers`:

```json
{
  "mcpServers": {
    "civil3d": {
      "command": "C:\\Users\\<you>\\AppData\\Local\\Packages\\PythonSoftwareFoundation.Python.3.13_qbz5n2kfra8p0\\LocalCache\\local-packages\\Python313\\Scripts\\civil3d-mcp.exe",
      "env": {
        "CIVIL3D_BRIDGE_URL": "http://127.0.0.1:7800"
      }
    }
  }
}
```

If you already have other MCP servers configured (e.g. filesystem,
github, gmail), put the `"civil3d": {...}` entry as a sibling inside
the same `mcpServers` object — separated by a comma — not in a second
top-level object.

Common JSON pitfalls:

- **Backslashes must be doubled** (`\\`) inside JSON strings.
- **Two top-level objects** glued back-to-back (`{...}{...}`) is invalid
  — it triggers Claude's "Could not load app settings — Unexpected
  token" error. Wrap everything in one outer `{...}`.
- The path to `civil3d-mcp.exe` differs depending on whether you used
  the Microsoft Store Python (path above) or system Python
  (`C:\Python313\Scripts\civil3d-mcp.exe`). Find yours with:
  ```powershell
  (Get-Command civil3d-mcp).Path
  ```

After saving, **fully quit** Claude Desktop (system tray → Quit) and
reopen. The hammer icon in the bottom-right of the chat input should
now show a `civil3d` server with ~64 tools.

---

\newpage

# 6. Verifying the install

## 6.1 Check the bridge is loaded in Civil 3D

1. Start Civil 3D and open any DWG.
2. At the C3D command line, type `MCPSTATUS` and press Enter.
3. You should see:
   `civil3d-mcp bridge: listening on http://127.0.0.1:7800`

If you don't see that:

- Check `%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle\`
  exists and contains `Contents\Civil3DMcpBridge.dll`.
- Look in C3D's command-line history for a load error after startup.
- Try `NETLOAD` and pick the DLL manually to surface the error.

## 6.2 Hit the health endpoint

In a *separate* PowerShell window (Civil 3D must be running):

```powershell
curl http://127.0.0.1:7800/health
```

Expected:

```json
{
  "ok": true,
  "version": "0.2.0",
  "toolCount": 64,
  "tools": ["civil3d_add_baseline_region", "...", "civil3d_zoom_to_alignment"]
}
```

## 6.3 First conversation with Claude

In Claude Desktop:

> **You:** *Run the civil3d bridge health check.*
>
> **Claude:** Calls `civil3d_bridge_health` -> returns `{"ok": true,
> "version": "0.2.0", "toolCount": 64}`. "The bridge is healthy and
> exposing 64 tools."

Then try a real query:

> **You:** *List the layers in the active drawing.*
>
> **Claude:** Calls `civil3d_list_layers` -> returns a list of all
> layers with their state.

---

\newpage

# 7. Tool reference

Every tool is prefixed `civil3d_`. Read-only (R) tools just query the
drawing; write (W) tools modify it inside a transaction.

## 7.1 Meta (2)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_bridge_health` | R | Check the C# bridge is reachable. |
| `civil3d_call` | W | Escape hatch — invoke any tool with a raw JSON arg dict. |

## 7.2 Alignments (9)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_alignments` | R | Names + handles of every alignment. |
| `civil3d_get_alignment_info` | R | Start / end station, length, layer, style, entity list. |
| `civil3d_get_xy_at_station` | R | `(station, offset) -> (x, y)` plus tangent direction. |
| `civil3d_point_at_chainage` | R | Alias of `get_xy_at_station`. |
| `civil3d_get_station_at_xy` | R | `(x, y) -> (station, offset)`. |
| `civil3d_export_alignment_geometry` | R | Dump every alignment entity (line / curve / spiral) sampled at an interval. |
| `civil3d_create_alignment_from_polyline` *(stubbed in 0.2.0)* | W | Promote a 2D polyline into an alignment. |
| `civil3d_create_offset_alignment` | W | Parallel offset of an existing alignment. |
| `civil3d_set_design_speed` | W | Apply a design speed to a station range. |

## 7.3 Profiles (8)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_profiles` | R | Names + parent alignments. |
| `civil3d_get_profile_info` | R | Min/max station, min/max elevation, entities, PVIs. |
| `civil3d_get_elevation_at_station` | R | Sample profile elevation at a chainage. |
| `civil3d_create_profile_from_surface` | W | Sample a TIN along an alignment -> EG profile. |
| `civil3d_create_layout_profile` | W | Empty FG profile bound to an alignment. |
| `civil3d_add_pvi` | W | Insert a point of vertical intersection. |
| `civil3d_add_vertical_curve` *(stubbed in 0.2.0)* | W | Insert a parabolic VC between two PVIs. |
| `civil3d_create_profile_view` | W | Profile-view (band set, scaling) at an insertion point. |

## 7.4 Corridors + sampling (12)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_corridors` | R | All corridors. |
| `civil3d_get_corridor_info` | R | Baselines, regions, station ranges, target surfaces. |
| `civil3d_list_assemblies` | R | All assemblies. |
| `civil3d_rebuild_corridor` | W | Force a rebuild. |
| `civil3d_set_region_assembly` | W | Swap a region's assembly. |
| `civil3d_create_corridor` | W | Skeleton corridor: alignment + profile + assembly + station range. |
| `civil3d_add_baseline_region` | W | Append a region to an existing baseline. |
| `civil3d_set_region_target_surface` | W | Wire a target surface (typically EG) onto a daylight subassembly. |
| `civil3d_export_corridor_sections` *(stubbed in 0.2.0)* | W | CSV of corridor cross-sections at an interval. |
| `civil3d_create_sample_line_group` | W | SLG along an alignment at fixed interval. |
| `civil3d_list_sample_line_groups` | R | SLGs with their member alignment. |
| `civil3d_sample_surface_along_alignment` | R | `[{station, elev}]` along an alignment from a surface. |

## 7.5 Surfaces (8)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_surfaces` | R | All TIN / volume surfaces with stats. |
| `civil3d_get_surface_volume` | R | Cut / fill / net between two surfaces. |
| `civil3d_get_elevation_at_xy` | R | Sample one point. |
| `civil3d_create_tin_surface` | W | Empty TIN. |
| `civil3d_create_surface_from_points` | W | TIN from a point group. |
| `civil3d_create_persistent_volume_surface` | W | Comparison surface that updates with edits. |
| `civil3d_add_breakline_from_polyline` | W | Add a polyline as a breakline. |
| `civil3d_add_points_to_surface` | W | Drop point objects into a TIN as definition points. |

## 7.6 Points (6)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_points` | R | COGO points (paginated). |
| `civil3d_create_point` | W | Single COGO point. |
| `civil3d_import_points_csv` | W | PNEZD / PENZD / NEZD / ENZD formats. |
| `civil3d_export_points_csv` | W | Same format options, all or filtered by group. |
| `civil3d_list_point_groups` | R | Names + counts. |
| `civil3d_create_point_group` | W | New group with optional include filters. |

## 7.7 Pipe networks (4)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_pipe_networks` | R | All networks. |
| `civil3d_get_pipe_network_info` | R | Structures + pipes with end points, diameters. |
| `civil3d_list_pipe_parts` | R | Browse parts list / families / sizes. |
| `civil3d_create_culvert_at_chainage` *(stubbed in 0.2.0)* | W | Place a culvert at a station. |

## 7.8 Blocks (5)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_blocks` | R | Available block definitions (filterable). |
| `civil3d_insert_block_at_chainage` | W | Generic block insertion at `(alignment, station, offset)`. |
| `civil3d_insert_blocks_batch` | W | Bulk insertion. |
| `civil3d_create_signpost_at_chainage` | W | Opinionated wrapper for road signs. |
| `civil3d_create_km_post_series` | W | Walk alignment, drop KM post block at interval. |

## 7.9 Road markings (2)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_draw_lane_marking` | W | Solid / dashed / double polylines following alignment curvature. |
| `civil3d_draw_pedestrian_crossing` | W | Zebra crossing perpendicular to alignment. |

## 7.10 Mass haul (2)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_compute_quantity_takeoff` *(stubbed in 0.2.0)* | R | Run a QTO criteria set. |
| `civil3d_export_mass_haul_csv` | W | Dump mass-haul data to CSV. |

## 7.11 Drawing utilities (8)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_get_drawing_info` | R | Filename, units, extents, modified flag. |
| `civil3d_save_drawing` | W | Save (or save-as if a path is given). |
| `civil3d_zoom_to_alignment` | W | Zoom + centre on a named alignment. |
| `civil3d_zoom_extents` | W | Self-explanatory. |
| `civil3d_list_layers` | R | All layers with state. |
| `civil3d_create_layer` | W | Layer + ACI color + optional linetype. |
| `civil3d_set_current_layer` | W | Make a layer current. |
| `civil3d_run_command` | W | Send a literal AutoCAD command — escape hatch. |

---

\newpage

# 8. Argument conventions

## 8.1 Stations and offsets

- **Stations** are in drawing units (metres for the Ugandan / metric
  case).
- **Offsets** are **+ left, − right** of the direction of travel.
- **Skew** for the culvert tool is measured in **degrees from
  perpendicular**; +ve rotates the pipe CCW looking down.

## 8.2 Names vs handles

Keys like `alignment`, `surface`, `network` accept either:

- A **name** (case-insensitive but otherwise exact match), or
- An AutoCAD **handle** (`<a8f3>` style).

If the same name exists in multiple sites, use the handle.

## 8.3 File paths

CSV input/output paths must be **absolute Windows paths** (`C:\...`).
The bridge runs inside Civil 3D's process — relative paths resolve
against Civil 3D's working directory, which is rarely what you want.

## 8.4 Layer naming

The bridge follows AIA / NCS-ish conventions for any layers it
auto-creates:

- `C-ROAD-*` — road geometry, marking
- `C-SIGN-*` — signage
- `C-DRAIN-*` — drainage / culverts
- `C-TOPO-*` — topographic data

If a target layer doesn't exist when a tool needs it, the bridge
creates it with a sensible ACI colour.

---

\newpage

# 9. Stubbed tools in 0.2.0

These tools exist in the registry and Claude can attempt to call them,
but the underlying Civil 3D 2026 .NET API entry points were renamed or
restructured enough that a faithful port is deferred. Calling them
returns a clear `ToolException` with a "not yet ported" message.

| Tool | What changed in C3D 2026 |
|------|--------------------------|
| `create_culvert_at_chainage` | `Network.AddNetworkPart()` + `NetworkPartType` enum gone; replaced with typed `AddLinePipe` / `AddStructure`. |
| `create_alignment_from_polyline` | The 8-arg `Alignment.Create(...)` overload gone; the new signature requires a different style/site resolution. |
| `add_vertical_curve` | `ProfileEntityCollection.AddFixedParabolaByLength` gone; replaced with `AddFreeSymmetricParabolaByPVIAndCurveLength` and friends. |
| `export_corridor_sections` | `Baseline.CalculatedStationList` gone; replaced with `GetAppliedAssemblyAtStation` per-station calls. |
| `compute_quantity_takeoff` | `Section.SampleSectionPoints` removed; only the `SectionPoints.Location` Point3d is exposed. |

All 59 other tools work end-to-end against the live drawing.

---

\newpage

# 10. Troubleshooting

## "The hammer icon doesn't show civil3d"

Most likely cause: the JSON config is malformed. Validate it at
[jsonlint.com](https://jsonlint.com) (paste in the file contents). The
two most common errors:

- Backslashes not doubled in the `command` path.
- Two top-level objects glued together — Claude shows
  *"Could not load app settings — Unexpected token"*.

Then fully quit Claude Desktop and reopen.

## "Bridge call failed: Connection refused"

The C# add-in isn't loaded in Civil 3D. Check:

1. Civil 3D is actually running.
2. A DWG is open.
3. `MCPSTATUS` at the C3D command line confirms the bridge is listening.
4. The bundle deployed cleanly to
   `%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle\Contents\`.

If the bundle is there but the bridge isn't loading, try `NETLOAD` on
the DLL manually — the load failure will surface in Civil 3D's command
line.

## "ToolException: not yet ported to C3D 2026 API"

You hit one of the 5 stubbed tools — see section 9. Use the underlying
Civil 3D UI for now or open an issue in the repo.

## "active drawing is not a Civil 3D document"

You opened a plain AutoCAD DWG instead of a Civil 3D one (different
template). Open or create a drawing from a Civil 3D template (the file
opens with a "Civil 3D" ribbon).

## Logs

- **Claude Desktop MCP log**: `%APPDATA%\Claude\logs\mcp.log`
- **Civil 3D command line history**: `Ctrl-F2` opens the AutoCAD text
  window; the bridge writes its load message at startup.
- **Bridge HTTP log**: stdout of the Civil 3D process — visible if you
  launched Civil 3D from a terminal.

## Reset

To uninstall:

```powershell
# 1. Remove the bundle
Remove-Item -Recurse "$env:APPDATA\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle"

# 2. Remove the Python package
pip uninstall civil3d-mcp

# 3. Edit %APPDATA%\Claude\claude_desktop_config.json and delete
#    the "civil3d" entry under mcpServers.

# 4. Restart Civil 3D and Claude Desktop.
```

---

\newpage

# Appendix A: File locations

| Path | Purpose |
|------|---------|
| `%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle\` | Deployed C# add-in bundle |
| `%APPDATA%\Claude\claude_desktop_config.json` | Claude Desktop MCP server registry |
| `%APPDATA%\Claude\logs\mcp.log` | Claude Desktop MCP runtime log |
| `<Python>\Scripts\civil3d-mcp.exe` | Python MCP server entry point |
| `C:\Program Files\Autodesk\AutoCAD 2026\C3D\AeccDbMgd.dll` | Civil 3D 2026 managed API |

# Appendix B: Versioning

This manual covers **civil3d-mcp 0.2.0**. The version is reported by
both `civil3d_bridge_health` and the deployed DLL's metadata.

# Appendix C: License & support

Internal / personal-consultancy use. No warranty. The bridge runs only
on `127.0.0.1` and does not listen on any external interface.

For deeper architecture and "how to add a tool" notes, see
`docs/ARCHITECTURE.md` in the repo.
