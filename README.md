# civil3d-mcp

A Model Context Protocol bridge that exposes the Autodesk **Civil 3D 2025 / 2026 / 2027**
.NET API to Claude (or any MCP client). Drive alignments, profiles, corridors,
surfaces, earthworks, sections, feature lines, points, pipe and pressure networks,
styles, road markings and drawing utilities in natural language, and check designs
against AASHTO criteria.

> **Status: 0.4.0** - 128 MCP tools (110 bridge tools + 18 Python-side tools).
> One bundle serves Civil 3D 2025 (.NET 8), 2026 (.NET 8) and **2027 (.NET 10)**,
> and several Civil 3D sessions can run side by side. See [CHANGELOG.md](CHANGELOG.md).

---

## Architecture

```
┌──────────────────┐   stdio   ┌──────────────────────┐   HTTP (127.0.0.1)   ┌──────────────────────────┐
│  Claude Desktop  │ ◄───────► │  Python FastMCP      │ ◄──────────────────► │  Civil3DMcpBridge.dll    │
│  (any MCP client)│           │  civil3d-mcp         │   discovery of all   │  Civil 3D 2025 :7825     │
└──────────────────┘           │  + AASHTO/COGO calcs │   live sessions      │  Civil 3D 2027 :7827 ... │
                               └──────────────────────┘                      └────────────┬─────────────┘
                                                                                          │ main thread,
                                                                              LockDocument + Transaction
                                                                                          ▼
                                                                              Civil 3D .NET API (AeccDb, AcDb)
```

* **C# add-in** (`plugin/`) - one ApplicationPlugins bundle with a DLL per release
  (`Contents/2025`, `Contents/2026`, `Contents/2027`). AutoCAD loads the one whose
  `SeriesMin/SeriesMax` matches (R25.0 / R25.1 / R26.0). Each session listens on
  `7800 + (year % 100)` (7825 / 7826 / 7827), overflowing to 7840-7859, and registers
  itself in `%LOCALAPPDATA%\Civil3DMcpBridge\instances\<pid>.json`.
* **Main-thread dispatch** - requests queue onto Civil 3D's UI thread; a posted
  message wakes Civil 3D immediately (no waiting for mouse movement), work runs
  only when AutoCAD is quiescent, and a caller that times out is cancelled rather
  than executed late. A command or dialog in progress returns a clear "busy" error.
* **Security** - loopback only; requests carrying a browser `Origin` header or a
  non-loopback `Host` are refused (blocks web pages and DNS rebinding from driving
  `run_command`).
* **Python MCP server** (`server/`) - FastMCP, Pydantic-validated wrappers, automatic
  session discovery, document targeting, design checks and engineering calculators.

---

## Quick start

On the Windows workstation with Civil 3D, from a PowerShell prompt in the repo root
(close Civil 3D first):

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The installer detects every installed release, builds each DLL with the .NET 10 SDK
(or deploys `dist\Civil3DMcpBridge-0.4.0.bundle.zip` if no SDK is present), deploys
the bundle, creates a private Python venv, and registers `civil3d` in Claude Desktop.
Then open Civil 3D, check the command line for
`[Civil3DMcpBridge] Civil 3D 2027 listening on http://127.0.0.1:7827`, restart
Claude Desktop, and ask *"list Civil 3D instances and run the bridge health check"*.

Details, flags and troubleshooting: [`docs/INSTALL.md`](docs/INSTALL.md).

---

## Tool inventory (128)

| Domain | Tools |
|---|---|
| **Sessions** (9) | `bridge_health`, `list_instances`, `select_instance`, `select_document`, `list_documents`, `activate_document`, `get_drawing_settings`, `undo`, `call` |
| **Alignments** (14) | list, info, **geometry_detail** (radii, spirals, deflections), xy↔station, point_at_chainage, export geometry, from polyline, **by PIs** (with S-C-S spirals), offset, design speed(s), **station equation**, **superelevation read-out** |
| **Profiles** (13) | list, info, **geometry_detail with K-values**, elevation at station, **sample profile (CSV)**, from surface, layout, **by PVI table**, add PVI, **delete PVI**, vertical curve, profile view |
| **Design checks** (2) | **`check_alignment_design`** (R_min, broken-back, reverse-curve tangents), **`check_profile_design`** (crest/sag K, curve length, max/min grade, un-curved grade breaks) |
| **Corridors** (13) | list, info, regions, codes, rebuild, create, add region, set assembly/target, export sections, **corridor surfaces: list / create (Top, Datum, extents boundary)** |
| **Earthworks** (4) | **`earthwork_by_sections`** (AEA cut/fill + mass ordinate straight from two surfaces), QTO, mass haul CSV, persistent volume surface |
| **Surfaces** (14) | list, volume, elevation at XY, create TIN / from points, breaklines, add points, **statistics, slope/elevation banding, transect, boundaries, contour extraction, rebuild** |
| **Sections** (6) | sample line groups (create/list), **list sample lines, export sampled sections (CSV), create section views grid**, sample along alignment |
| **Feature lines** (4) | **list, create (polyline or 3D points), drape on surface, export vertices** |
| **Points** (6) | list, create, import/export CSV, point groups |
| **Pipes** (6) | gravity: list, info, parts, culvert at chainage; **pressure: list, pipe schedule by DN** |
| **Drafting** (21) | layers, zoom, save, run_command, blocks, signposts, KM posts, lane markings, zebra, **polylines, 3D polylines, text, entity info, erase, polyline length/area schedule by layer, styles list/set** |
| **Layout** (4) | junction corner fillets, roundabout centreline, swept path envelope, turning-path arc |
| **Land** (1) | **sites and parcels schedule (m², ha, perimeter)** |
| **Reports** (4) | profile design, alignment overview, drawing inventory, quantity |
| **Calculators** (7, no Civil 3D needed) | **design controls (SSD, K, R_min), COGO inverse/forward, horizontal curve, Bowditch traverse, superelevation runoff, average end area** |

Bold = new in 0.4.0. Full reference: [`docs/TOOLS.md`](docs/TOOLS.md).
Design-check basis: AASHTO *Green Book* 2018 (metric); project criteria (e.g. MoWT
Road Design Manual) govern where they differ, and each result states its assumptions.

---

## Sources folded into 0.4.0

| Source | What was adopted |
|---|---|
| Autodesk AutoCAD 2027 SDK notes | .NET 10 target, R26.0 series, mandatory rebuild per release |
| [JhulVF/Civil3d-mcp-tcce](https://github.com/JhulVF/Civil3d-mcp-tcce) (MIT) | 2027 build notes; corridor surfaces, styles, feature line and earthwork tool ideas |
| [Joshua8-AI/Civil3D-mcp](https://github.com/Joshua8-AI/Civil3D-mcp) (MIT) | ApplicationPlugins bundle instead of NETLOAD (SECURELOAD-safe), per-release port |
| [amirclyde/Civil3D-mcp](https://github.com/amirclyde/Civil3D-mcp) / Sacred-G (MIT) | QC checks, COGO, superelevation, section views, parcels, pressure networks scope |
| [antonhofstader/Civil3D-mcp-python-COM](https://github.com/antonhofstader/Civil3D-mcp-python-COM) (MIT) | surface definition / statistics scope |

No code was copied; features were re-implemented against this bridge's architecture.

---

## Tests

```bash
pip install -e "server[test]"
pytest            # 44 tests: models, AASHTO/COGO calcs, discovery, MCP->HTTP->mock bridge
```

The C# add-in is compile-checked for all three releases against the official
`AutoCAD.NET` 25.0.0 / 25.1.0 / 26.0.0 reference packages and Civil 3D reference
assemblies; runtime verification needs Civil 3D (see [`docs/TESTING.md`](docs/TESTING.md)).

---

## License & support

Personal / AfroBIM / SANS LIMITE use. No warranty. The bridge listens on `127.0.0.1` only.
If a call fails: `MCPSTATUS` in Civil 3D, `civil3d_list_instances` in Claude, and the
error text (tool errors carry plain-language hints).
