# Changelog

## 0.4.0 - 2026-10-30 - Civil 3D 2027 support

### Platform
- **Civil 3D 2027 (.NET 10, R26.0)** build target; 2025 and 2026 stay on .NET 8.
  `plugin/Civil3DMcpBridge.csproj` takes `-p:C3DVersion=2025|2026|2027` (per-release
  `obj/` and `bin/` trees); `-p:C3DNet=net10.0-windows` covers 2026 after its .NET 10 update.
- **Multi-release bundle**: `Contents/<year>/Civil3DMcpBridge.dll` with one
  `RuntimeRequirements` block per release, deployed to the trusted
  `%APPDATA%\Autodesk\ApplicationPlugins` (works with SECURELOAD=1, no NETLOAD).
- **Side-by-side sessions**: per-release ports (7825/7826/7827, overflow 7840-7859),
  instance registry in `%LOCALAPPDATA%\Civil3DMcpBridge\instances`, Python auto-discovery,
  `civil3d_list_instances` / `civil3d_select_instance`, `CIVIL3D_RELEASE` env pin.
- **Document targeting**: any call can act on a named open drawing
  (`civil3d_select_document`, or `document` in the `/invoke` body).
- **Dispatcher hardening**: immediate wake-up via posted message, runs only when
  AutoCAD is quiescent, per-call timeout (default 300 s, `timeoutMs`), cancelled calls
  are dropped instead of running late, clear "busy" error when a command is active.
- **Security**: refuses browser-origin requests and non-loopback Host headers.
- `/health` now reports release, AutoCAD version, .NET runtime, pid, port, open drawings and busy state.
- Fixed C# 14 / .NET 10 SDK build break (`array.Reverse()` binding to the span overload).
- Replaced obsolete `ProfilePVI.Station` with `RawStation`.
- Python: pinned `mcp<2` (mcp 2.x renamed FastMCP and would break a fresh install);
  removed duplicate registration of `civil3d_bridge_health` / `civil3d_call`.
- Installer rewritten: release detection, SDK-or-prebuilt, private venv, Claude config
  backup + update for both classic and Store installs, legacy layout clean-up.
  Added `uninstall.ps1`; `redeploy.ps1` now wraps the installer.

### New bridge tools (42)
Sessions: `list_documents`, `activate_document`, `get_drawing_settings`, `undo`.
Alignment: `get_alignment_geometry_detail`, `create_alignment_by_pis`, `add_station_equation`,
`get_superelevation`, `get_design_speeds`.
Profile: `get_profile_geometry_detail`, `create_profile_by_pvis`, `delete_pvi`, `sample_profile`.
Feature lines: `list_feature_lines`, `create_feature_line`, `feature_line_elevations_from_surface`,
`get_feature_line_points`.
Earthworks: `earthwork_by_sections`.
Surfaces: `get_surface_statistics`, `analyze_surface_slopes`, `sample_surface_transect`,
`add_surface_boundary`, `extract_surface_contours`, `rebuild_surface`.
Sections: `list_sample_lines`, `export_sampled_sections`, `create_section_views`.
Corridors: `get_corridor_codes`, `list_corridor_surfaces`, `create_corridor_surface`, `get_corridor_regions`.
Styles: `list_styles`, `set_object_style`.
Geometry: `create_polyline`, `create_3d_polyline`, `create_text`, `measure_polylines`,
`get_entity_info`, `erase_entities`.
Land / utilities: `list_sites_parcels`, `list_pressure_networks`, `get_pressure_network_schedule`.

### New Python-side tools (12)
`civil3d_list_instances`, `civil3d_select_instance`, `civil3d_select_document`,
`civil3d_check_alignment_design`, `civil3d_check_profile_design`,
`calc_design_controls`, `calc_cogo_inverse`, `calc_cogo_forward`, `calc_horizontal_curve`,
`calc_traverse_bowditch`, `calc_superelevation_runoff`, `calc_average_end_area`.

### Tests
44 pytest tests: AASHTO table cross-checks, COGO/traverse, AEA, discovery and pruning,
and MCP -> HTTP -> mock bridge end-to-end for the design checks.

## 0.3.0
Junction fillets, roundabout centreline, vehicle tracking proxies, reports (74 tools).
