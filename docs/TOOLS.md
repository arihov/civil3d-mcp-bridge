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


---

## New in 0.4.0

Arguments are the fields of each tool's `params` object (see `server/src/civil3d_mcp/models.py`).

### `calc_average_end_area`
Volumes and mass-haul ordinates from a station / cut area / fill area table.

Args: `rows`*, `fill_factor`  (* required)

### `calc_cogo_forward`
Coordinates from a point, whole-circle bearing and distance.

Args: `from_xy`*, `bearing_deg`*, `distance`*  (* required)

### `calc_cogo_inverse`
Grid bearing (whole circle and DMS) and distance between two points.

Args: `from_xy`*, `to_xy`*  (* required)

### `calc_design_controls`
Stopping sight distance, minimum crest/sag K, minimum radius and f_max for a design speed (AASHTO 2018 metric), with the formulas and assumptions used.

Args: `design_speed_kph`*, `grade_pct`, `reaction_time_s`, `e_max_pct`  (* required)

### `calc_horizontal_curve`
Tangent length, arc length, long chord, external, middle ordinate (and TC/CT stations).

Args: `radius`*, `deflection_deg`*, `pi_station`  (* required)

### `calc_superelevation_runoff`
Superelevation runoff and tangent runout lengths (AASHTO 2018 eq. 3-23/3-24).

Args: `design_speed_kph`*, `e_design_pct`*, `lane_width_m`, `lanes_rotated`, `normal_crossfall_pct`  (* required)

### `calc_traverse_bowditch`
Compute a traverse from bearings and distances, report misclosure and precision, and adjust by the Bowditch (compass) rule.

Args: `start_xy`*, `legs`*, `close_xy`  (* required)

### `civil3d_activate_document`
Bring an open drawing to the front (makes it the active MDI document).

Args: `document`*  (* required)

### `civil3d_add_station_equation`
Add a station equation (e.g. to tie to an existing chainage at a section break).

Args: `alignment`*, `raw_station_back`*, `station_ahead`*, `decreasing`  (* required)

### `civil3d_add_surface_boundary`
Add an outer / hide / show / data-clip boundary from a closed polyline and rebuild.

Args: `surface`*, `polyline_handle`*, `type`, `mid_ordinate`, `non_destructive`  (* required)

### `civil3d_analyze_surface_slopes`
Area-weighted slope bands (%) and optional elevation bands from the TIN triangles.

Args: `surface`*, `bands_pct`, `elevation_interval`  (* required)

### `civil3d_check_alignment_design`
Check every horizontal curve against R_min for the design speed and e_max (AASHTO 2018 metric), and flag short tangents between reverse curves and broken-back curves. Returns pass/flag per element.

Args: `alignment`*, `design_speed_kph`*, `e_max_pct`, `min_tangent_m`  (* required)

### `civil3d_check_profile_design`
Check vertical curves for minimum K (crest stopping sight distance, sag headlight) and minimum length, and tangents for max/min grade. AASHTO 2018 metric basis.

Args: `alignment`*, `profile`*, `design_speed_kph`*, `max_grade_pct`, `min_grade_pct`  (* required)

### `civil3d_create_3d_polyline`
Create a 3D polyline from [x, y, z] points (e.g. for breaklines).

Args: `points`*, `closed`, `layer`  (* required)

### `civil3d_create_alignment_by_pis`
Create a horizontal alignment from PI coordinates with a radius per interior PI and optional symmetric clothoid spirals (spiral-curve-spiral).

Args: `name`*, `pis`*, `radii`, `spiral_lengths`, `site`, `style`, `label_set`, `layer`  (* required)

### `civil3d_create_corridor_surface`
Create a corridor surface (e.g. Top for finished grade, Datum for formation) from link codes, add a corridor-extents boundary and rebuild.

Args: `corridor`*, `name`*, `link_codes`, `feature_line_codes`, `overhang`, `add_extents_boundary`, `rebuild`  (* required)

### `civil3d_create_feature_line`
Create a feature line from an existing polyline (handle) or from [x, y, z] points.

Args: `name`, `polyline_handle`, `points`, `site`, `layer`  (* required)

### `civil3d_create_polyline`
Create a 2D polyline (optionally closed) from [x, y] points.

Args: `points`*, `closed`, `elevation`, `layer`  (* required)

### `civil3d_create_profile_by_pvis`
Build a layout (design) profile from [station, elevation] PVIs and a symmetric parabola length per interior PVI.

Args: `alignment`*, `name`*, `pvis`*, `curve_lengths`, `style`, `label_set`, `layer`  (* required)

### `civil3d_create_section_views`
Create one section view per sample line, laid out in a grid from an origin.

Args: `alignment`*, `group`*, `origin`*, `columns`, `spacing_x`, `spacing_y`, `start_station`, `end_station`, `max_views`  (* required)

### `civil3d_create_text`
Place single-line text or MText.

Args: `text`*, `x`*, `y`*, `height`, `rotation_deg`, `layer`, `mtext`, `width`  (* required)

### `civil3d_delete_pvi`
Remove the PVI nearest a station (within tolerance) from a layout profile.

Args: `alignment`*, `profile`*, `station`*, `tolerance`  (* required)

### `civil3d_earthwork_by_sections`
Cut/fill volumes between an existing and a design surface along an alignment by average end area, sampled straight from the surfaces (no sample lines or QTO setup). Returns per-station areas, volumes, mass-haul ordinate and totals; optional CSV. Independent check on Civil 3D's Compute Materials.

Args: `alignment`*, `existing_surface`*, `design_surface`*, `interval`, `left_width`, `right_width`, `sample_step`, `start_station`, `end_station`, `stations`, `fill_factor`, `strip_depth`, `csv_path`  (* required)

### `civil3d_erase_entities`
Erase objects by handle. Reversible with civil3d_undo until the drawing is closed.

Args: `handles`*  (* required)

### `civil3d_export_sampled_sections`
Offset/elevation points of every sampled section (EG, FG, corridor surfaces) per station; write a CSV for cross-section QA or third-party volume checks.

Args: `alignment`*, `group`*, `sources`, `csv_path`  (* required)

### `civil3d_extract_surface_contours`
Extract contour polylines at an interval, optionally onto a layer.

Args: `surface`*, `interval`, `layer`  (* required)

### `civil3d_feature_line_elevations_from_surface`
Assign feature line elevations from a surface, optionally inserting grade breaks.

Args: `name`, `handle`, `surface`*, `insert_grade_breaks`  (* required)

### `civil3d_get_alignment_geometry_detail`
Every tangent, arc and spiral with stations, lengths, radii, deflections, spiral A-values, coordinates, plus station equations.

Args: `alignment`*  (* required)

### `civil3d_get_corridor_codes`
Link, point and shape codes present in a corridor (needed to build surfaces).

Args: `corridor`*  (* required)

### `civil3d_get_corridor_regions`
Baselines (alignment/profile) and regions with station ranges and assemblies.

Args: `corridor`*  (* required)

### `civil3d_get_design_speeds`
Design speed table assigned to an alignment.

Args: `alignment`*  (* required)

### `civil3d_get_drawing_settings`
Drawing units, coordinate system code (e.g. UTM-36N / Arc 1960), angular units, scale and extents. Check this before trusting coordinates or volumes.

Args: none  (* required)

### `civil3d_get_entity_info`
Type, layer, name, extents, length and area for any object handle.

Args: `handle`*  (* required)

### `civil3d_get_feature_line_points`
All PI and elevation points with running 2D chainage; optional CSV.

Args: `name`, `handle`, `csv_path`  (* required)

### `civil3d_get_pressure_network_schedule`
Pipe lengths grouped by part and nominal diameter; optional CSV.

Args: `network`*, `csv_path`  (* required)

### `civil3d_get_profile_geometry_detail`
Tangents (grades) and vertical curves (type, length, K, A) plus the PVI table.

Args: `alignment`*, `profile`*  (* required)

### `civil3d_get_superelevation`
Superelevation curves and critical stations with lane cross-slopes (%).

Args: `alignment`*  (* required)

### `civil3d_get_surface_statistics`
Point count, min/max/mean elevation, extents, 2D/3D area and grade statistics.

Args: `surface`*  (* required)

### `civil3d_list_corridor_surfaces`
Corridor surfaces with their link / feature line codes and boundaries.

Args: `corridor`*  (* required)

### `civil3d_list_documents`
Open drawings in the selected Civil 3D session, the active one, and any command in progress.

Args: none  (* required)

### `civil3d_list_feature_lines`
Feature lines in all sites and site-less, with 2D/3D length and elevation range.

Args: none  (* required)

### `civil3d_list_instances`
List every running Civil 3D session with the bridge loaded (release, pid, port), and which one calls currently go to. Use when 2025 and 2027 are open side by side.

Args: none  (* required)

### `civil3d_list_pressure_networks`
Pressure (water) networks with pipe, fitting and appurtenance counts.

Args: none  (* required)

### `civil3d_list_sample_lines`
Sample lines in a group with station, swath offsets and section count.

Args: `alignment`*, `group`*  (* required)

### `civil3d_list_sites_parcels`
Sites with parcel number, area (m2 and ha) and perimeter; optional CSV for land schedules.

Args: `csv_path`  (* required)

### `civil3d_list_styles`
Style names per collection (alignment, profile, surface, profile_view, section_view, point, corridor, label sets ...). Omit `collection` for a summary of all.

Args: `collection`  (* required)

### `civil3d_measure_polylines`
Total length and area of polylines grouped by layer, with wildcard layer filters. Quick BOQ take-off for kerbs, drains, edge lines and closed pavement areas.

Args: `layers`, `closed_only`, `include_items`  (* required)

### `civil3d_rebuild_surface`
Rebuild an out-of-date surface.

Args: `surface`*  (* required)

### `civil3d_sample_profile`
Elevation and grade at a fixed interval along a profile; optional CSV for long-section tables.

Args: `alignment`*, `profile`*, `interval`, `start_station`, `end_station`, `csv_path`  (* required)

### `civil3d_sample_surface_transect`
Elevations along a line (start/end/step) or at a list of points.

Args: `surface`*, `start`, `end`, `points`, `step`  (* required)

### `civil3d_select_document`
Route following calls to a specific open drawing (file name or full path) without switching windows. Omit `document` to go back to the active drawing.

Args: `document`  (* required)

### `civil3d_select_instance`
Pin all following calls to one Civil 3D session, by release (e.g. 2027), process id, port or URL. Pass clear=true to return to automatic selection.

Args: `release`, `pid`, `port`, `url`, `clear`  (* required)

### `civil3d_set_object_style`
Apply a named style to an alignment, profile, surface, corridor, or any styled object by handle.

Args: `object_type`*, `style`*, `name`, `alignment`, `handle`  (* required)

### `civil3d_undo`
Queue UNDO for N steps in the target drawing (each MCP write is normally one step).

Args: `steps`  (* required)
