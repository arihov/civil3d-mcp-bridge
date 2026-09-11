# Tool reference

Every tool is prefixed `civil3d_`. Read-only (R) tools just query the
drawing; write (W) tools modify it inside a transaction. The MCP client
sees this distinction as the `readOnlyHint` annotation.

Arg shapes are defined in [`server/src/civil3d_mcp/models.py`](../server/src/civil3d_mcp/models.py)
— every field there has a `description` that surfaces in the MCP
schema. Below is the operational summary.

---

## Meta (2)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_bridge_health` | R | Check the C# bridge is reachable. Returns `{"ok": true, "version": "0.2.0", "toolCount": 66}` if Civil 3D is running with the add-in loaded. |
| `civil3d_call` | W | **Escape hatch.** Invoke an arbitrary tool name with a raw JSON arg dict. Use when a tool isn't exposed as a typed wrapper, or for one-off debugging. |

---

## Alignments (10)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_alignments` | R | Names + handles of every alignment in the active drawing. |
| `civil3d_get_alignment_info` | R | Start / end station, length, layer, style. |
| `civil3d_get_xy_at_station` | R | `(station, offset) → (x, y)` plus tangent direction. Offsets follow Civil 3D convention: **+offset = LEFT** of direction of travel. |
| `civil3d_point_at_chainage` | R | Alias of `get_xy_at_station` — kept because "chainage" is the Ugandan term. |
| `civil3d_get_station_at_xy` | R | `(x, y) → (station, offset)`. |
| `civil3d_export_alignment_geometry` | R | Dump every alignment entity (line / curve / spiral) with PI coordinates, radii, lengths. CSV-ready. |
| `civil3d_create_alignment_from_polyline` | W | Promote a 2D polyline into an alignment in a named site. |
| `civil3d_create_offset_alignment` | W | Parallel offset of an existing alignment. |
| `civil3d_set_design_speed` | W | Apply a design speed to a station range. |

---

## Profiles (8)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_profiles` | R | Names + parent alignments. |
| `civil3d_get_profile_info` | R | Min/max station, min/max elevation, entity count. |
| `civil3d_get_elevation_at_station` | R | Sample elevation at a chainage. |
| `civil3d_create_profile_from_surface` | W | Sample a TIN surface along an alignment → existing-ground profile. |
| `civil3d_create_layout_profile` | W | Empty FG profile bound to an alignment, ready to receive PVIs. |
| `civil3d_add_pvi` | W | Insert a point of vertical intersection (no curve). |
| `civil3d_add_vertical_curve` | W | Insert a parabolic VC between two PVI elevations, by length or K. K-value formula `L = K × |G1−G2| × 100` applied internally. |
| `civil3d_create_profile_view` | W | Profile-view (band set, scaling) at an insertion point. |

---

## Corridors + sampling (13)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_corridors` | R | All corridors in the drawing. |
| `civil3d_get_corridor_info` | R | Baselines, regions, station ranges, target surfaces. |
| `civil3d_list_assemblies` | R | All assemblies (templates of sub-assemblies). |
| `civil3d_rebuild_corridor` | W | Force a rebuild — useful after target updates. |
| `civil3d_set_region_assembly` | W | Swap a region's assembly. |
| `civil3d_create_corridor` | W | Skeleton corridor: alignment + profile + assembly + start/end stations. |
| `civil3d_add_baseline_region` | W | Append a region (assembly slice) to an existing baseline. |
| `civil3d_set_region_target_surface` | W | Wire a target surface (typically EG) onto a daylight subassembly. |
| `civil3d_export_corridor_sections` | W | CSV of corridor sections at a given interval — for QC / pavement design. |
| `civil3d_create_sample_line_group` | W | Create an SLG along an alignment. |
| `civil3d_list_sample_line_groups` | R | SLGs with their member alignment. |
| `civil3d_sample_surface_along_alignment` | R | Returns `[{station, elev, slope}]` along an alignment from a surface — no DWG mutation. |

---

## Surfaces (8)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_surfaces` | R | TIN, grid, volume, persistent-volume surfaces with stats. |
| `civil3d_get_surface_volume` | R | Cut / fill / net against a base elevation. |
| `civil3d_get_elevation_at_xy` | R | Sample one point. |
| `civil3d_create_tin_surface` | W | Empty TIN named under a named style. |
| `civil3d_create_surface_from_points` | W | TIN from a point group. |
| `civil3d_create_persistent_volume_surface` | W | Comparison surface (top − bottom) that updates with edits. |
| `civil3d_add_breakline_from_polyline` | W | Add a polyline as a non-destructive breakline. |
| `civil3d_add_points_to_surface` | W | Drop point objects into a TIN as definition points. |

---

## Points (6)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_points` | R | COGO points in the active DB. |
| `civil3d_create_point` | W | Single COGO point with optional description / raw description. |
| `civil3d_import_points_csv` | W | PNEZD / PENZD / NEZD / ENZD formats. |
| `civil3d_export_points_csv` | W | Same format options, all or filtered by group. |
| `civil3d_list_point_groups` | R | Names + point counts. |
| `civil3d_create_point_group` | W | New point group with optional raw-desc filter. |

---

## Pipe networks (4)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_pipe_networks` | R | All networks (storm / sanitary / generic). |
| `civil3d_get_pipe_network_info` | R | Structures + pipes with end points, lengths, materials. |
| `civil3d_list_pipe_parts` | R | Browse parts lists / part families / part sizes. |
| **`civil3d_create_culvert_at_chainage`** ⭐ | W | Place a circular pipe culvert with two headwalls perpendicular to an alignment at a station. Optional skew, inverts (derived from FG when omitted), label. |

---

## Blocks (5)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_list_blocks` | R | Available block definitions (filter by name pattern). |
| `civil3d_insert_block_at_chainage` | W | Generic block insertion at `(alignment, station, offset)` with rotation = alignment tangent + override. Sets attributes. |
| `civil3d_insert_blocks_batch` | W | Bulk insertion with shared defaults — for placing many signs in one call. |
| **`civil3d_create_signpost_at_chainage`** ⭐ | W | Opinionated wrapper: pick a sign code (R1-1, W1-1, GS-1 …), legend, station, side. Looks up block, picks correct layer (`C-SIGN-REG`, `C-SIGN-WARN`, `C-SIGN-GUIDE`), sets `LEGEND` + `STATION` attributes. |
| **`civil3d_create_km_post_series`** ⭐ | W | Walks alignment start→end at interval, drops KM post block with chainage in the `KM` attribute. |

---

## Road markings (2)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_draw_lane_marking` | W | Polyline that follows alignment curvature — `solid`, `dashed`, `double_solid`, `double_dashed`. Configurable dash length, gap, double-line offset. Auto-creates the `C-ROAD-MARK` layer. |
| `civil3d_draw_pedestrian_crossing` | W | Zebra crossing perpendicular to alignment, configurable stripe width / spacing. |

---

## Mass haul (2)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_compute_quantity_takeoff` | R | Run a QTO criteria set on a corridor with a sample line group — returns cut / fill volumes per region. |
| `civil3d_export_mass_haul_csv` | W | Dump mass-haul data (station, cumulative volume) to CSV. |

---

## Drawing utilities (8)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_get_drawing_info` | R | Filename, units, drawing extents, modified flag. |
| `civil3d_save_drawing` | W | Save (or save-as if a path is given). |
| `civil3d_zoom_to_alignment` | W | Zoom + centre the editor on a named alignment. |
| `civil3d_zoom_extents` | W | Self-explanatory. |
| `civil3d_list_layers` | R | All layers with on/frozen/locked/color state. |
| `civil3d_create_layer` | W | Layer + ACI color + optional linetype. |
| `civil3d_set_current_layer` | W | Make a layer current. |
| `civil3d_run_command` | W | Send a literal AutoCAD command string to the editor — use sparingly, for things not covered by typed tools. |

---

## Junctions (1)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_create_junction_corner_fillets` | W | Right-angle junction layout at an X-crossing: computes the four tangent corner fillet arcs (radius = kerb return radius) where the approach kerb lines meet, draws the approach kerb polylines, and optionally adds a central island circle. Auto-creates the `C-ROAD-JCT` layer. |

---

## Roundabouts (1)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_create_roundabout_centerline` | W | Builds an exact circular centreline (closed bulged polyline) at a given centre + inscribed radius and converts it into a Civil 3D alignment. Optionally adds IN and OUT kerb offset alignments at ±lane_width/2. Auto-creates the `C-ROAD-RDB` layer. |

---

## Vehicle tracking (2)

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_swept_path_envelope` | W | Samples an alignment between two stations and draws the swept-path envelope as two side polylines at ±offset (e.g. offtracking for a design vehicle). Read-only geometry output onto `C-ROAD-TRACK`. |
| `civil3d_create_turning_path_arc` | W | Draws a fillet/turning path as a sampled arc polyline (≤5° per vertex) and creates a circular or arc alignment from it — useful for truck turning templates. |

---

## Reports (4)

Python-only composition tools — they call existing read tools and format the result; they never write to the drawing.

| Tool | R/W | Purpose |
|------|-----|---------|
| `civil3d_profile_design_report` | R | Vertical-profile design summary from a named alignment's profile: PVI/element tables (station, elevation, grade, curve length, K-value) plus stats (max abs grade, min curve length, critical length). `format`: `markdown` (default), `json`, or `csv`. |
| `civil3d_alignment_overview_report` | R | Horizontal-alignment overview: start/end stations, length, design-speed segments, curve/spiral counts, and a station-range element table. `format`: `markdown` / `json` / `csv`. |
| `civil3d_drawing_inventory_report` | R | Whole-drawing inventory aggregating alignments, surfaces, corridors, and pipe networks with counts and key properties per object. `format`: `markdown` / `json` / `csv`. |
| `civil3d_quantity_report` | R | Cut / fill / net volumes between two surfaces (or a volume offset surface) via `get_surface_volume`, formatted as a quantity table. `format`: `markdown` / `json` / `csv`. |

---

## Argument conventions

* **Stations** are in drawing units (metres in Uganda's case).
* **Offsets** are **+ left, − right** of direction of travel.
* **Skew** for the culvert is measured in **degrees from perpendicular**;
  +ve rotates the pipe CCW looking down.
* **Layer names** follow AIA/NCS-ish conventions: `C-ROAD-*`,
  `C-SIGN-*`, `C-DRAIN-*`, `C-TOPO-*`. The bridge auto-creates them
  with sensible ACI colors if absent.
* **CSV paths** must be absolute Windows paths (`C:\...`). The bridge
  runs under the Civil 3D process so relative paths resolve relative to
  the Civil 3D working directory — not what most users expect.
* **Strings vs numbers**: keys like `alignment`, `surface`, `network` accept
  either a name or a handle (`<...>`). Names are matched
  case-insensitively but exactly otherwise.
