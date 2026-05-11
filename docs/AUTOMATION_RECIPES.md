# Automation recipes

This is a cookbook of prompt patterns that exercise the bridge well.
Each shows the user prompt to Claude, the tools Claude will call, and
the kind of arguments those calls carry. Use these as templates.

> **Convention.** "1+200" is shorthand for chainage 1200.000. The
> bridge takes the raw number — Claude does the parsing.

---

## 1. Drainage culvert from chainage + sizing

**Prompt to Claude**

> Place a 1200 mm concrete culvert across CL-01 at chainage 4+523,
> skewed 15° clockwise, 22 m long. Inlet invert 1234.8, outlet 1234.45.
> Add it to the "Drainage-Main" pipe network.

**What Claude does**

1. `civil3d_list_pipe_parts` — verifies a 1200 mm Concrete Pipe family
   exists.
2. `civil3d_create_culvert_at_chainage` with:
   ```json
   {
     "alignment": "CL-01",
     "station": 4523,
     "diameter": 1200,
     "length": 22.0,
     "skew_degrees": 15.0,
     "invert_in": 1234.80,
     "invert_out": 1234.45,
     "network_name": "Drainage-Main",
     "pipe_family": "Concrete Pipe"
   }
   ```
3. Bridge returns inlet/outlet world coordinates + slope (0.0159 m/m =
   1.59%). Both structures + pipe show in plan; profile view (if it
   crosses one) is updated automatically.

**If the parts list is missing the diameter** — Claude will see
`civil3d_list_pipe_parts` doesn't include 1200 mm and either ask, or
fall through to the closest available size. You should explicitly add
the 1200 mm pipe to your parts list in Civil 3D first.

---

## 2. KM post series

**Prompt**

> Place KM posts every 500 m along CL-01 right side, 3 m offset.

**Tools**

```text
civil3d_create_km_post_series
{
  "alignment": "CL-01",
  "block": "KM_POST",
  "interval": 500.0,
  "side": "right",
  "offset": 3.0,
  "layer": "C-ROAD-KMPOST",
  "attribute_tag": "KM"
}
```

Bridge places one block per station, perpendicular to alignment,
populates the KM attribute with the station value (5 → "5", 5.5 →
"5+500" depending on block attribute formatting).

---

## 3. Lane markings on a curved alignment

**Prompt**

> On CL-01, draw a double-solid lane edge line on the right side at
> offset +3.5 m from 0+000 to 1+200. Then a dashed centreline from
> 0+000 to 4+500 with 3 m dashes and 6 m gaps.

**Tools**

```text
civil3d_draw_lane_marking
{
  "alignment": "CL-01",
  "start_station": 0,
  "end_station": 1200,
  "offset": 3.5,
  "type": "double_solid",
  "width": 0.15,
  "double_spacing": 0.10
}

civil3d_draw_lane_marking
{
  "alignment": "CL-01",
  "start_station": 0,
  "end_station": 4500,
  "offset": 0,
  "type": "dashed",
  "dash_length": 3.0,
  "gap_length": 6.0
}
```

Geometry follows horizontal curvature via fixed-interval sampling
(default 1 m). The layer `C-ROAD-MARK` is auto-created if missing.

---

## 4. Signs at a junction

**Prompt**

> Place a "Stop" sign (code R-1) on the right of CL-01 at chainage
> 2+340, with the legend "STOP" and chainage attribute populated.

**Tools**

```text
civil3d_create_signpost_at_chainage
{
  "alignment": "CL-01",
  "station": 2340,
  "block": "SIGN_GENERIC",
  "side": "right",
  "offset": 3.0,
  "rotation": "perpendicular",
  "legend": "STOP",
  "sign_code": "R-1"
}
```

Block must exist in the drawing with attribute tags `LEGEND`, `CODE`,
`CHAINAGE` for the attribute population to take.

---

## 5. Field survey CSV → COGO points → TIN surface

**Prompt**

> Import the survey points from C:\Surveys\plot42.csv (PNEZD with header),
> then build a TIN surface called "EG-Plot42" from those points.

**Tools**

```text
civil3d_import_points_csv
{ "csv_path": "C:\\Surveys\\plot42.csv", "skip_header": true }

civil3d_create_point_group
{ "name": "EG-Plot42-Group", "raw_description_match": "*" }

civil3d_create_surface_from_points
{ "name": "EG-Plot42", "point_group": "EG-Plot42-Group" }
```

The bridge auto-detects PNEZD vs PENZD on first data row and skips bad
rows, returning a created/skipped count + first 10 errors.

---

## 6. Existing-ground profile from a TIN

**Prompt**

> Create EG profile on CL-01 by sampling the "EG" surface.

**Tools**

```text
civil3d_create_profile_from_surface
{
  "alignment": "CL-01",
  "surface": "EG",
  "profile_name": "EG"
}
```

Picks the drawing's default profile style if none given.

---

## 7. Design profile: blank layout + PVIs + vertical curves

**Prompt**

> Create a layout profile "FG" on CL-01. Add PVIs at (0, 1200.0),
> (500, 1206.5), (1200, 1198.0), (2000, 1212.4). Then put a 120 m
> sag vertical curve at the PVI at chainage 1200.

**Tools**

```text
civil3d_create_layout_profile     { "alignment": "CL-01", "profile_name": "FG" }
civil3d_add_pvi                   { "alignment": "CL-01", "profile": "FG", "station": 0,    "elevation": 1200.0 }
civil3d_add_pvi                   { ..., "station": 500,  "elevation": 1206.5 }
civil3d_add_pvi                   { ..., "station": 1200, "elevation": 1198.0 }
civil3d_add_pvi                   { ..., "station": 2000, "elevation": 1212.4 }
civil3d_add_vertical_curve        { "alignment": "CL-01", "profile": "FG", "pvi_station": 1200, "length": 120, "expected_curve_type": "sag" }
```

If the inferred curve type doesn't match `expected_curve_type`, the
call rolls back so Claude can adjust grades first.

---

## 8. Corridor build + daylight to EG

**Prompt**

> Build a corridor on CL-01 using profile FG and the "Urban-2L" assembly
> from 0+000 to 4+500. Daylight to the EG surface.

**Tools**

```text
civil3d_create_corridor
{
  "name": "Corridor-CL-01",
  "baseline_alignment": "CL-01",
  "baseline_profile": "FG",
  "assembly": "Urban-2L",
  "start_station": 0,
  "end_station": 4500,
  "target_surface": "EG"
}
```

For multi-zone (e.g. urban → rural assembly swap):

```text
civil3d_add_baseline_region   { corridor, baseline_index: 0, assembly: "Rural-2L", start_station: 1200, end_station: 4500 }
civil3d_set_region_target_surface  { corridor, baseline_index: 0, region_index: 1, target_name: "Daylight_Target", surface: "EG" }
civil3d_rebuild_corridor      { name: "Corridor-CL-01" }
```

---

## 9. Quantity takeoff → CSV

**Prompt**

> On CL-01, create sample lines every 20 m with 15 m swath either side.
> Run the takeoff and export to C:\Out\massHaul-CL01.csv.

**Tools**

```text
civil3d_create_sample_line_group
{
  "alignment": "CL-01",
  "group_name": "SLG-20m",
  "interval": 20.0,
  "swath_left": 15.0,
  "swath_right": 15.0
}
```

The user then runs **Compute Materials** in Civil 3D once (UI step —
this isn't yet automatable through the .NET API in a fully general way).
After that:

```text
civil3d_compute_quantity_takeoff   { alignment: "CL-01", sample_line_group: "SLG-20m" }
civil3d_export_mass_haul_csv       { alignment: "CL-01", sample_line_group: "SLG-20m", csv_path: "C:/Out/massHaul-CL01.csv" }
```

---

## 10. Reverse-geocode a survey point onto an alignment

**Prompt**

> A field point landed at E=500032.5, N=400418.2 — what's its chainage
> and offset on CL-01?

**Tools**

```text
civil3d_get_station_at_xy
{ "alignment": "CL-01", "x": 500032.5, "y": 400418.2 }
```

Returns `{station, offset}`. Useful for matching field photos / GPS
points to a design alignment.

---

## 11. Cross-section sampler (without committing sample lines)

**Prompt**

> Sample EG along CL-01 every 25 m at offsets -7.5, 0, +7.5 and tell
> me where the cross-fall reverses sign.

**Tools**

```text
civil3d_sample_surface_along_alignment
{
  "alignment": "CL-01",
  "surface": "EG",
  "interval": 25.0,
  "offsets": [-7.5, 0.0, 7.5]
}
```

Returns the sample matrix. Claude then computes cross-falls in its
head ((z_right - z_left) / 15) and reports sign changes.

---

## 12. Pedestrian crossing

**Prompt**

> Add a zebra crossing on CL-01 at chainage 2+450, 4 m wide along road,
> 4 m perpendicular extent each side of centreline.

**Tools**

```text
civil3d_draw_pedestrian_crossing
{
  "alignment": "CL-01",
  "station": 2450,
  "width": 4.0,
  "length": 4.0,
  "stripe_width": 0.5,
  "stripe_gap": 0.5
}
```

Eight stripes of 0.5 m × 4 m on layer `C-ROAD-MARK`.

---

## 13. Bulk furniture placement from a list

**Prompt**

> Place 12 signs along CL-01 — I'll give you the chainages and codes.

Claude then sends:

```text
civil3d_insert_blocks_batch
{
  "block": "SIGN_GENERIC",
  "alignment": "CL-01",
  "layer": "C-ROAD-SIGN",
  "items": [
    { "station": 250,  "side": "right", "attributes": { "CODE": "W-1", "LEGEND": "Bend ahead" } },
    { "station": 600,  "side": "left",  "attributes": { "CODE": "R-2", "LEGEND": "No overtaking" } },
    ... 10 more
  ]
}
```

One transaction, one call, returns inserted count + first 20 errors.

---

## 14. Save the drawing under a new name

**Prompt**

> Save as C:\Projects\NyendoSiti\07-FG-Revised.dwg.

**Tools**

```text
civil3d_save_drawing  { "path": "C:/Projects/NyendoSiti/07-FG-Revised.dwg" }
```

Always saves in the AutoCAD 2018 format (compatible with Civil 3D
2018+).

---

## Escape hatches

Two tools cover anything not directly wrapped:

- **`civil3d_call(tool, args)`** — call any name-registered C# tool
  directly. Use this if you see a tool in `civil3d_bridge_health`
  output that doesn't have a typed Python wrapper yet.
- **`civil3d_run_command(command)`** — send an arbitrary AutoCAD/C3D
  command-line string to the document, exactly as if typed. Use for
  AutoLISP routines, SCRIPT execution, etc. Remember: command must end
  with a space (Enter).

Example with the escape hatch:

> Run the AutoLISP routine `(MY-DRAW-CHAINAGE-LABELS "CL-01" 100)`.

```text
civil3d_run_command  { "command": "(MY-DRAW-CHAINAGE-LABELS \"CL-01\" 100) " }
```
