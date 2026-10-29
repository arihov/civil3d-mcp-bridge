"""Alignment / profile design tools, design-criteria checks and engineering calculators."""

from __future__ import annotations

from civil3d_mcp import engineering as eng
from civil3d_mcp._instance import READ_ANN, WRITE_ANN, as_json, call, mcp
from civil3d_mcp.bridge import BridgeError, invoke
from civil3d_mcp.models import (
    AlignmentByPis,
    AlignmentDesignCheck,
    AlignmentOnly,
    CogoForward,
    CogoInverse,
    CurveSolve,
    DeletePvi,
    EndAreaTable,
    ProfileByPvis,
    ProfileDesignCheck,
    ProfileRef,
    SampleProfile,
    SpeedInput,
    StationEquation,
    SuperelevationRunoff,
    Traverse,
)

CALC_ANN = {"readOnlyHint": True, "destructiveHint": False, "idempotentHint": True, "openWorldHint": False}


# ─── Civil 3D: horizontal ─────────────────────────────────────────────────

@mcp.tool(name="civil3d_get_alignment_geometry_detail", annotations={**READ_ANN, "title": "Alignment geometry detail"})
async def civil3d_get_alignment_geometry_detail(params: AlignmentOnly) -> str:
    """Every tangent, arc and spiral with stations, lengths, radii, deflections,
    spiral A-values, coordinates, plus station equations."""
    return await call("civil3d_get_alignment_geometry_detail", params.model_dump())


@mcp.tool(name="civil3d_create_alignment_by_pis", annotations={**WRITE_ANN, "title": "Alignment from PIs"})
async def civil3d_create_alignment_by_pis(params: AlignmentByPis) -> str:
    """Create a horizontal alignment from PI coordinates with a radius per interior PI
    and optional symmetric clothoid spirals (spiral-curve-spiral)."""
    return await call("civil3d_create_alignment_by_pis", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_add_station_equation", annotations={**WRITE_ANN, "title": "Add station equation"})
async def civil3d_add_station_equation(params: StationEquation) -> str:
    """Add a station equation (e.g. to tie to an existing chainage at a section break)."""
    return await call("civil3d_add_station_equation", params.model_dump())


@mcp.tool(name="civil3d_get_superelevation", annotations={**READ_ANN, "title": "Superelevation critical stations"})
async def civil3d_get_superelevation(params: AlignmentOnly) -> str:
    """Superelevation curves and critical stations with lane cross-slopes (%)."""
    return await call("civil3d_get_superelevation", params.model_dump())


@mcp.tool(name="civil3d_get_design_speeds", annotations={**READ_ANN, "title": "Design speeds"})
async def civil3d_get_design_speeds(params: AlignmentOnly) -> str:
    """Design speed table assigned to an alignment."""
    return await call("civil3d_get_design_speeds", params.model_dump())


# ─── Civil 3D: vertical ───────────────────────────────────────────────────

@mcp.tool(name="civil3d_get_profile_geometry_detail", annotations={**READ_ANN, "title": "Profile geometry with K-values"})
async def civil3d_get_profile_geometry_detail(params: ProfileRef) -> str:
    """Tangents (grades) and vertical curves (type, length, K, A) plus the PVI table."""
    return await call("civil3d_get_profile_geometry_detail", params.model_dump())


@mcp.tool(name="civil3d_create_profile_by_pvis", annotations={**WRITE_ANN, "title": "Profile from PVI table"})
async def civil3d_create_profile_by_pvis(params: ProfileByPvis) -> str:
    """Build a layout (design) profile from [station, elevation] PVIs and a symmetric
    parabola length per interior PVI."""
    return await call("civil3d_create_profile_by_pvis", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_delete_pvi", annotations={**WRITE_ANN, "title": "Delete PVI"})
async def civil3d_delete_pvi(params: DeletePvi) -> str:
    """Remove the PVI nearest a station (within tolerance) from a layout profile."""
    return await call("civil3d_delete_pvi", params.model_dump())


@mcp.tool(name="civil3d_sample_profile", annotations={**READ_ANN, "title": "Sample profile elevations"})
async def civil3d_sample_profile(params: SampleProfile) -> str:
    """Elevation and grade at a fixed interval along a profile; optional CSV for long-section tables."""
    return await call("civil3d_sample_profile", params.model_dump(exclude_none=True))


# ─── design-criteria checks ───────────────────────────────────────────────

@mcp.tool(name="civil3d_check_alignment_design", annotations={**READ_ANN, "title": "Check alignment against design criteria"})
async def civil3d_check_alignment_design(params: AlignmentDesignCheck) -> str:
    """Check every horizontal curve against R_min for the design speed and e_max
    (AASHTO 2018 metric), and flag short tangents between reverse curves and
    broken-back curves. Returns pass/flag per element."""
    try:
        geo = await invoke("civil3d_get_alignment_geometry_detail", {"alignment": params.alignment})
    except BridgeError as exc:
        return as_json({"error": str(exc)})
    ctrl = eng.design_controls(params.design_speed_kph, e_max_pct=params.e_max_pct)
    rmin = ctrl["minRadius_m"]
    issues, curves = [], []
    els = geo.get("elements", [])
    for el in els:
        if el.get("kind") == "arc":
            ok = el["radius"] >= rmin
            curves.append({"startStation": el["startStation"], "radius": el["radius"], "ok": ok})
            if not ok:
                issues.append({"severity": "fail", "station": el["startStation"],
                               "issue": f"radius {el['radius']} m below R_min {rmin} m for {params.design_speed_kph:g} km/h at e_max {params.e_max_pct:g}%"})
    # tangents between two arcs
    for i in range(1, len(els) - 1):
        prev, cur, nxt = els[i - 1], els[i], els[i + 1]
        if cur.get("kind") == "tangent" and prev.get("kind") in ("arc", "spiral") and nxt.get("kind") in ("arc", "spiral"):
            pa = next((e for e in reversed(els[:i]) if e.get("kind") == "arc"), None)
            na = next((e for e in els[i + 1:] if e.get("kind") == "arc"), None)
            if not pa or not na:
                continue
            same = pa["clockwise"] == na["clockwise"]
            bb_limit = max(params.min_tangent_m, 6 * params.design_speed_kph / 3.6)
            if same and cur["length"] < bb_limit:
                issues.append({"severity": "flag", "station": cur["startStation"],
                               "issue": f"broken-back: {cur['length']} m tangent between same-direction curves "
                                        f"(< {bb_limit:.0f} m, about 6 s travel; guideline, not a hard limit)"})
            if not same and params.min_tangent_m and cur["length"] < params.min_tangent_m:
                issues.append({"severity": "flag", "station": cur["startStation"],
                               "issue": f"reverse curves separated by {cur['length']} m tangent (< {params.min_tangent_m} m)"})
    return as_json({
        "alignment": params.alignment, "controls": ctrl,
        "curveCount": len(curves), "failCount": sum(1 for i in issues if i["severity"] == "fail"),
        "issues": issues, "curves": curves,
        "note": "Project design criteria (e.g. MoWT Road Design Manual) govern where they differ from AASHTO.",
    })


@mcp.tool(name="civil3d_check_profile_design", annotations={**READ_ANN, "title": "Check profile against design criteria"})
async def civil3d_check_profile_design(params: ProfileDesignCheck) -> str:
    """Check vertical curves for minimum K (crest stopping sight distance, sag headlight)
    and minimum length, and tangents for max/min grade. AASHTO 2018 metric basis."""
    try:
        geo = await invoke("civil3d_get_profile_geometry_detail", {"alignment": params.alignment, "profile": params.profile})
    except BridgeError as exc:
        return as_json({"error": str(exc)})
    ctrl = eng.design_controls(params.design_speed_kph)
    issues = []
    for e in geo.get("entities", []):
        kind = e.get("kind", "")
        if kind == "tangent":
            g = abs(e.get("gradePct", 0))
            if g > params.max_grade_pct:
                issues.append({"severity": "fail", "station": e["startStation"], "issue": f"grade {g:.2f}% exceeds max {params.max_grade_pct}%"})
            if g < params.min_grade_pct:
                issues.append({"severity": "flag", "station": e["startStation"], "issue": f"grade {g:.2f}% below drainage minimum {params.min_grade_pct}%"})
        elif "parabola" in kind or kind == "circular":
            ctype = str(e.get("curveType", "")).lower()
            kmin = ctrl["kCrestMin"] if "crest" in ctype else ctrl["kSagMin"]
            k = e.get("kValue")
            if k is not None and k < kmin:
                issues.append({"severity": "fail", "station": e["startStation"],
                               "issue": f"{ctype} K={k} below minimum {kmin} for {params.design_speed_kph:g} km/h"})
            if e.get("length", 0) < ctrl["minVerticalCurveLength_m"]:
                issues.append({"severity": "flag", "station": e["startStation"],
                               "issue": f"curve length {e['length']} m below 0.6V = {ctrl['minVerticalCurveLength_m']} m"})
    for p in geo.get("pvis", [])[1:-1]:
        a = abs(p.get("gradeChangePct", 0))
        if a > 0.5 and str(p.get("type", "")).lower().endswith("tangent"):
            issues.append({"severity": "flag", "station": p["station"], "issue": f"grade break of {a:.2f}% with no vertical curve"})
    return as_json({
        "alignment": params.alignment, "profile": params.profile, "controls": ctrl,
        "failCount": sum(1 for i in issues if i["severity"] == "fail"),
        "issues": sorted(issues, key=lambda i: i["station"]),
        "note": "Max/min grade come from your inputs; confirm against the project design criteria.",
    })


# ─── calculators (no Civil 3D needed) ────────────────────────────────────

@mcp.tool(name="calc_design_controls", annotations={**CALC_ANN, "title": "Design controls for a speed"})
async def calc_design_controls(params: SpeedInput) -> str:
    """Stopping sight distance, minimum crest/sag K, minimum radius and f_max for a design
    speed (AASHTO 2018 metric), with the formulas and assumptions used."""
    return as_json(eng.design_controls(params.design_speed_kph, params.grade_pct, params.reaction_time_s, params.e_max_pct))


@mcp.tool(name="calc_cogo_inverse", annotations={**CALC_ANN, "title": "COGO inverse"})
async def calc_cogo_inverse(params: CogoInverse) -> str:
    """Grid bearing (whole circle and DMS) and distance between two points."""
    return as_json(eng.inverse(params.from_xy, params.to_xy))


@mcp.tool(name="calc_cogo_forward", annotations={**CALC_ANN, "title": "COGO forward"})
async def calc_cogo_forward(params: CogoForward) -> str:
    """Coordinates from a point, whole-circle bearing and distance."""
    return as_json(eng.forward(params.from_xy, params.bearing_deg, params.distance))


@mcp.tool(name="calc_horizontal_curve", annotations={**CALC_ANN, "title": "Solve horizontal curve"})
async def calc_horizontal_curve(params: CurveSolve) -> str:
    """Tangent length, arc length, long chord, external, middle ordinate (and TC/CT stations)."""
    return as_json(eng.solve_curve(params.radius, params.deflection_deg, params.pi_station))


@mcp.tool(name="calc_traverse_bowditch", annotations={**CALC_ANN, "title": "Traverse adjustment"})
async def calc_traverse_bowditch(params: Traverse) -> str:
    """Compute a traverse from bearings and distances, report misclosure and precision,
    and adjust by the Bowditch (compass) rule."""
    return as_json(eng.bowditch(params.start_xy, [(l.bearing_deg, l.distance) for l in params.legs], params.close_xy))


@mcp.tool(name="calc_superelevation_runoff", annotations={**CALC_ANN, "title": "Superelevation runoff"})
async def calc_superelevation_runoff(params: SuperelevationRunoff) -> str:
    """Superelevation runoff and tangent runout lengths (AASHTO 2018 eq. 3-23/3-24)."""
    return as_json(eng.superelevation_runoff(params.design_speed_kph, params.e_design_pct, params.lane_width_m,
                                             params.lanes_rotated, params.normal_crossfall_pct))


@mcp.tool(name="calc_average_end_area", annotations={**CALC_ANN, "title": "Average end area volumes"})
async def calc_average_end_area(params: EndAreaTable) -> str:
    """Volumes and mass-haul ordinates from a station / cut area / fill area table."""
    return as_json(eng.average_end_area([(r.station, r.cut_area, r.fill_area) for r in params.rows], params.fill_factor))
