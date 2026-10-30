"""Pydantic input models for civil3d_mcp tools.

Field names are chosen to match the C# bridge's JSON arg expectations
exactly — model_dump() produces the dict that ships over the wire.
"""

from __future__ import annotations

from typing import Any, Literal, Optional

from pydantic import BaseModel, ConfigDict, Field


class _Strict(BaseModel):
    model_config = ConfigDict(
        str_strip_whitespace=True,
        extra="forbid",
        validate_assignment=True,
    )


# ─── Alignments ────────────────────────────────────────────────────────────

class AlignmentName(_Strict):
    name: str = Field(..., min_length=1, max_length=200)


class AlignmentByPoly(_Strict):
    name: str = Field(..., min_length=1, max_length=200)
    polyline_handle: str = Field(..., min_length=1, max_length=32)
    site: Optional[str] = Field(default=None, max_length=200)
    alignment_style: Optional[str] = Field(default=None, max_length=200)
    label_set_style: Optional[str] = Field(default=None, max_length=200)
    erase_polyline: bool = Field(default=False)


class OffsetAlignment(_Strict):
    source_alignment: str = Field(..., min_length=1, max_length=200)
    offset: float
    name: str = Field(..., min_length=1, max_length=200)


class DesignSpeed(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    station: float
    speed_kph: float = Field(..., gt=0)
    number: Optional[int] = Field(default=None, ge=1)


class PointAtChainage(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    station: float
    offset: float = Field(default=0.0)
    profile: Optional[str] = None


class XyAtStation(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    station: float
    offset: float = Field(default=0.0)


class StationAtXy(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    x: float
    y: float


class ExportGeometry(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    csv_path: str = Field(..., min_length=1)


# ─── Profiles ──────────────────────────────────────────────────────────────

class ProfilesByAlignment(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)


class ProfileInfo(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    profile: str = Field(..., min_length=1, max_length=200)


class AddVerticalCurve(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    profile: str = Field(..., min_length=1, max_length=200)
    pvi_station: float
    length: float = Field(..., gt=0)
    expected_curve_type: Optional[Literal["sag", "crest"]] = None
    search_tolerance: float = Field(default=0.5, gt=0)


class ProfileFromSurface(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    surface: str = Field(..., min_length=1, max_length=200)
    profile_name: str = Field(..., min_length=1, max_length=200)
    profile_style: Optional[str] = None
    label_set_style: Optional[str] = None


class LayoutProfile(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    profile_name: str = Field(..., min_length=1, max_length=200)
    profile_style: Optional[str] = None


class AddPvi(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    profile: str = Field(..., min_length=1, max_length=200)
    station: float
    elevation: float


class ElevationAtStation(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    profile: str = Field(..., min_length=1, max_length=200)
    stations: list[float] = Field(..., min_length=1, max_length=10_000)


class SampleSurfaceAlongAlignment(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    surface: str = Field(..., min_length=1, max_length=200)
    interval: float = Field(default=10.0, gt=0)
    offsets: list[float] = Field(default_factory=lambda: [0.0])


class CreateProfileView(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    name: str = Field(..., min_length=1, max_length=200)
    insertion_x: float
    insertion_y: float
    profile_view_style: Optional[str] = None
    band_set_style: Optional[str] = None


# ─── Corridors ─────────────────────────────────────────────────────────────

class CorridorName(_Strict):
    name: str = Field(..., min_length=1, max_length=200)


class CreateCorridor(_Strict):
    name: str = Field(..., min_length=1, max_length=200)
    baseline_alignment: str = Field(..., min_length=1, max_length=200)
    baseline_profile: str = Field(..., min_length=1, max_length=200)
    assembly: str = Field(..., min_length=1, max_length=200)
    start_station: Optional[float] = None
    end_station: Optional[float] = None
    target_surface: Optional[str] = None


class AddBaselineRegion(_Strict):
    corridor: str = Field(..., min_length=1, max_length=200)
    baseline_index: int = Field(..., ge=0)
    assembly: str = Field(..., min_length=1, max_length=200)
    start_station: float
    end_station: float
    region_name: Optional[str] = None


class SetRegionAssembly(_Strict):
    corridor: str = Field(..., min_length=1, max_length=200)
    baseline_index: int = Field(..., ge=0)
    region_index: int = Field(..., ge=0)
    assembly: str = Field(..., min_length=1, max_length=200)
    rebuild: bool = Field(default=True)


class SetRegionTargetSurface(_Strict):
    corridor: str = Field(..., min_length=1, max_length=200)
    baseline_index: int = Field(..., ge=0)
    region_index: int = Field(..., ge=0)
    target_name: str = Field(..., min_length=1, max_length=200)
    surface: str = Field(..., min_length=1, max_length=200)


class ExportCorridorSections(_Strict):
    corridor: str = Field(..., min_length=1, max_length=200)
    baseline_index: int = Field(default=0, ge=0)
    interval: float = Field(default=20.0, gt=0)
    output_path: str = Field(..., min_length=1)


# ─── Surfaces ──────────────────────────────────────────────────────────────

class SurfaceVolume(_Strict):
    base: str = Field(..., min_length=1, max_length=200)
    comparison: str = Field(..., min_length=1, max_length=200)
    keep: bool = Field(default=False)


class CreateTinSurface(_Strict):
    name: str = Field(..., min_length=1, max_length=200)
    description: Optional[str] = None


class SurfaceFromPoints(_Strict):
    name: str = Field(..., min_length=1, max_length=200)
    description: Optional[str] = None
    point_group: Optional[str] = None


class PersistentVolume(_Strict):
    name: str = Field(..., min_length=1, max_length=200)
    base: str = Field(..., min_length=1, max_length=200)
    comparison: str = Field(..., min_length=1, max_length=200)


class BreaklineFromPolyline(_Strict):
    surface: str = Field(..., min_length=1, max_length=200)
    polyline_handle: str = Field(..., min_length=1, max_length=32)
    description: Optional[str] = None


class AddPointsToSurface(_Strict):
    surface: str = Field(..., min_length=1, max_length=200)
    point_group: Optional[str] = None


class ElevationAtXy(_Strict):
    surface: str = Field(..., min_length=1, max_length=200)
    x: float
    y: float


# ─── COGO points ───────────────────────────────────────────────────────────

class ListPoints(_Strict):
    limit: int = Field(default=100, ge=1, le=1000)
    offset: int = Field(default=0, ge=0)


class CreatePoint(_Strict):
    northing: float
    easting: float
    elevation: float
    description: Optional[str] = None
    point_number: Optional[int] = Field(default=None, ge=1)


class PointsCsv(_Strict):
    csv_path: str = Field(..., min_length=1)


class PointsCsvIn(_Strict):
    csv_path: str = Field(..., min_length=1)
    skip_header: bool = Field(default=True)


class CreatePointGroup(_Strict):
    name: str = Field(..., min_length=1, max_length=200)
    description: Optional[str] = None
    raw_description_match: Optional[str] = None


# ─── Pipe networks ─────────────────────────────────────────────────────────

class PipeNetworkName(_Strict):
    name: str = Field(..., min_length=1, max_length=200)


class CulvertAtChainage(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    station: float
    diameter: float = Field(..., gt=0,
                            description="Inner diameter in mm (e.g. 900, 1200).")
    length: float = Field(..., gt=0)
    skew_degrees: float = Field(default=0.0)
    invert_in: float
    invert_out: float
    network_name: str = Field(..., min_length=1, max_length=200)
    pipe_family: Optional[str] = None
    structure_family: Optional[str] = None
    label_suffix: Optional[str] = None


# ─── Blocks / road furniture ───────────────────────────────────────────────

class ListBlocks(_Strict):
    name_contains: Optional[str] = None


class InsertBlock(_Strict):
    block: str = Field(..., min_length=1, max_length=200)
    alignment: str = Field(..., min_length=1, max_length=200)
    station: float
    offset: float = Field(default=0.0)
    rotation: Literal["along", "perpendicular", "absolute_degrees"] = Field(default="along")
    absolute_degrees: float = Field(default=0.0)
    elevation: float = Field(default=0.0)
    profile_for_elevation: Optional[str] = None
    scale: float = Field(default=1.0, gt=0)
    layer: str = Field(default="0", max_length=255)
    attributes: Optional[dict[str, str]] = None


class InsertBlocksBatch(_Strict):
    items: list[dict[str, Any]] = Field(..., min_length=1, max_length=10_000)
    block: Optional[str] = None
    alignment: Optional[str] = None
    layer: Optional[str] = None
    scale: Optional[float] = None


class Signpost(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    station: float
    block: str = Field(default="SIGN_GENERIC", max_length=200)
    side: Literal["left", "right"] = Field(default="right")
    offset: float = Field(default=3.0, gt=0)
    rotation: Literal["along", "perpendicular", "absolute_degrees"] = Field(default="perpendicular")
    profile: Optional[str] = None
    scale: float = Field(default=1.0, gt=0)
    layer: str = Field(default="C-ROAD-SIGN", max_length=255)
    legend: str = Field(default="", max_length=200)
    sign_code: str = Field(default="", max_length=64)


class KmPostSeries(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    block: str = Field(default="KM_POST", max_length=200)
    interval: float = Field(default=1000.0, gt=0)
    side: Literal["left", "right"] = Field(default="right")
    offset: float = Field(default=3.0, gt=0)
    layer: str = Field(default="C-ROAD-KMPOST", max_length=255)
    attribute_tag: str = Field(default="KM", max_length=64)
    start_station: Optional[float] = None
    end_station: Optional[float] = None


# ─── Road markings ─────────────────────────────────────────────────────────

class LaneMarking(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    start_station: float
    end_station: float
    offset: float
    type: Literal["solid", "dashed", "double_solid", "double_dashed", "solid_dashed"] = Field(default="solid")
    width: float = Field(default=0.15, gt=0)
    dash_length: float = Field(default=3.0, gt=0)
    gap_length: float = Field(default=6.0, gt=0)
    double_spacing: float = Field(default=0.10, gt=0)
    sampling_interval: float = Field(default=1.0, gt=0)
    layer: str = Field(default="C-ROAD-MARK", max_length=255)
    color_index: int = Field(default=7, ge=1, le=255)


class PedestrianCrossing(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    station: float
    width: float = Field(default=4.0, gt=0)
    length: float = Field(default=4.0, gt=0)
    stripe_width: float = Field(default=0.5, gt=0)
    stripe_gap: float = Field(default=0.5, gt=0)
    layer: str = Field(default="C-ROAD-MARK", max_length=255)
    color_index: int = Field(default=7, ge=1, le=255)


# ─── Sampling ──────────────────────────────────────────────────────────────

class SampleLineGroupCreate(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    group_name: str = Field(..., min_length=1, max_length=200)
    interval: float = Field(..., gt=0)
    swath_left: float = Field(default=15.0, gt=0)
    swath_right: float = Field(default=15.0, gt=0)
    start_station: Optional[float] = None
    end_station: Optional[float] = None


class SampleLineGroupsList(_Strict):
    alignment: Optional[str] = None


# ─── Mass haul ─────────────────────────────────────────────────────────────

class QuantityTakeoff(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    sample_line_group: str = Field(..., min_length=1, max_length=200)
    material_list_index: int = Field(default=0, ge=0)


class ExportMassHaul(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    sample_line_group: str = Field(..., min_length=1, max_length=200)
    csv_path: str = Field(..., min_length=1)
    material_list_index: int = Field(default=0, ge=0)


# ─── Drawing utils ─────────────────────────────────────────────────────────

class SaveDrawing(_Strict):
    path: Optional[str] = None


class ZoomToAlignment(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)


class CreateLayer(_Strict):
    name: str = Field(..., min_length=1, max_length=255)
    color_index: int = Field(default=7, ge=1, le=255)
    description: Optional[str] = None


class LayerName(_Strict):
    name: str = Field(..., min_length=1, max_length=255)


class RunCommand(_Strict):
    command: str = Field(..., min_length=1, max_length=2000)


# ─── Generic escape hatch ──────────────────────────────────────────────────

class GenericCall(_Strict):
    tool: str = Field(..., min_length=1, max_length=200)
    args: dict[str, Any] = Field(default_factory=dict)


# ─── Junctions / intersections ─────────────────────────────────────────────

class JunctionFillets(_Strict):
    center_x: float
    center_y: float
    bearing_deg: float = Field(default=0.0)
    half_width_a: float = Field(..., gt=0)
    half_width_b: float = Field(..., gt=0)
    corner_radius: float = Field(..., gt=0)
    draw_approach_lines: bool = True
    approach_length: float = Field(default=15.0, gt=0)
    island: bool = False
    layer: Optional[str] = Field(default=None, max_length=255)
    color_aci: int = Field(default=7, ge=1, le=255)


# ─── Roundabouts ───────────────────────────────────────────────────────────

class RoundaboutCenterline(_Strict):
    center_x: float
    center_y: float
    radius: float = Field(..., gt=0)
    name: str = Field(..., min_length=1, max_length=200)
    lane_width: float = Field(default=0.0, ge=0)
    create_offsets: bool = True
    description: Optional[str] = None


# ─── Vehicle tracking (proxies) ────────────────────────────────────────────

class SweptEnvelope(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    start_station: float
    end_station: float
    left_offset: float
    right_offset: float
    step: float = Field(default=0.5, gt=0)
    layer: Optional[str] = Field(default=None, max_length=255)
    color_aci: int = Field(default=2, ge=1, le=255)


class TurningPathArc(_Strict):
    center_x: float
    center_y: float
    radius: float = Field(..., gt=0)
    start_angle_deg: float
    end_angle_deg: float
    name: str = Field(..., min_length=1, max_length=200)
    description: Optional[str] = None


# ─── Reports (Python-side composition over read tools) ─────────────────────

class ProfileReport(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    profile: str = Field(..., min_length=1, max_length=200)
    format: Literal["json", "markdown", "csv"] = "markdown"


class AlignmentReport(_Strict):
    alignment: str = Field(..., min_length=1, max_length=200)
    format: Literal["json", "markdown"] = "markdown"


class InventoryReport(_Strict):
    format: Literal["json", "markdown"] = "markdown"


class QuantityReport(_Strict):
    base: str = Field(..., min_length=1, max_length=200)
    comparison: str = Field(..., min_length=1, max_length=200)
    format: Literal["json", "markdown"] = "markdown"


# ═══ v0.4.0 additions ══════════════════════════════════════════════════════

Name = Field(..., min_length=1, max_length=200)
XY = list[float]


# ─── Sessions / documents ─────────────────────────────────────────────────

class SelectInstance(_Strict):
    release: Optional[int] = Field(default=None, ge=2020, le=2040, description="Civil 3D year, e.g. 2027")
    pid: Optional[int] = Field(default=None, ge=1)
    port: Optional[int] = Field(default=None, ge=1025, le=65535)
    url: Optional[str] = Field(default=None, description="Explicit bridge URL, e.g. http://127.0.0.1:7827")
    clear: bool = Field(default=False, description="Drop the pin and return to automatic selection")


class SelectDocument(_Strict):
    document: Optional[str] = Field(default=None, max_length=400, description="File name or full path; omit for the active drawing")


class ActivateDocument(_Strict):
    document: str = Field(..., min_length=1, max_length=400)


class UndoSteps(_Strict):
    steps: int = Field(default=1, ge=1, le=50)


# ─── Alignment / profile design ───────────────────────────────────────────

class AlignmentOnly(_Strict):
    alignment: str = Name


class AlignmentByPis(_Strict):
    name: str = Name
    pis: list[XY] = Field(..., min_length=2, description="[[x, y], ...] PI coordinates in order")
    radii: list[float] = Field(default_factory=list, description="One radius per interior PI (n-2 values); 0 = angle point")
    spiral_lengths: list[float] = Field(default_factory=list, description="Optional symmetric clothoid length per interior PI")
    site: Optional[str] = None
    style: Optional[str] = None
    label_set: Optional[str] = None
    layer: Optional[str] = Field(default="C-ROAD-ALIGN")


class StationEquation(_Strict):
    alignment: str = Name
    raw_station_back: float
    station_ahead: float
    decreasing: bool = False


class ProfileRef(_Strict):
    alignment: str = Name
    profile: str = Name


class ProfileByPvis(_Strict):
    alignment: str = Name
    name: str = Name
    pvis: list[XY] = Field(..., min_length=2, description="[[station, elevation], ...]")
    curve_lengths: list[float] = Field(default_factory=list, description="Vertical curve length per interior PVI (n-2 values); 0 = none")
    style: Optional[str] = None
    label_set: Optional[str] = None
    layer: Optional[str] = Field(default="C-ROAD-PROF")


class DeletePvi(_Strict):
    alignment: str = Name
    profile: str = Name
    station: float
    tolerance: float = Field(default=0.5, gt=0)


class SampleProfile(_Strict):
    alignment: str = Name
    profile: str = Name
    interval: float = Field(default=20.0, gt=0)
    start_station: Optional[float] = None
    end_station: Optional[float] = None
    csv_path: Optional[str] = None


class AlignmentDesignCheck(_Strict):
    alignment: str = Name
    design_speed_kph: float = Field(..., gt=0, le=140)
    e_max_pct: float = Field(default=8.0, gt=0, le=12, description="Maximum superelevation rate, %")
    min_tangent_m: float = Field(default=0.0, ge=0, description="Flag tangents shorter than this between reverse curves")


class ProfileDesignCheck(_Strict):
    alignment: str = Name
    profile: str = Name
    design_speed_kph: float = Field(..., gt=0, le=140)
    max_grade_pct: float = Field(default=8.0, gt=0, le=20, description="Project maximum grade, % (from the design criteria)")
    min_grade_pct: float = Field(default=0.5, ge=0, le=5, description="Minimum grade for drainage in cut / kerbed sections, %")


# ─── Feature lines ────────────────────────────────────────────────────────

class FeatureLineRef(_Strict):
    name: Optional[str] = None
    handle: Optional[str] = None


class CreateFeatureLine(_Strict):
    name: Optional[str] = None
    polyline_handle: Optional[str] = None
    points: Optional[list[list[float]]] = Field(default=None, description="[[x, y, z], ...] when no polyline_handle")
    site: Optional[str] = None
    layer: Optional[str] = None


class FeatureLineFromSurface(_Strict):
    name: Optional[str] = None
    handle: Optional[str] = None
    surface: str = Name
    insert_grade_breaks: bool = True


class FeatureLinePoints(_Strict):
    name: Optional[str] = None
    handle: Optional[str] = None
    csv_path: Optional[str] = None


# ─── Earthworks / surfaces / sections ────────────────────────────────────

class EarthworkBySections(_Strict):
    alignment: str = Name
    existing_surface: str = Name
    design_surface: str = Name
    interval: float = Field(default=20.0, gt=0)
    left_width: float = Field(default=20.0, ge=0)
    right_width: float = Field(default=20.0, ge=0)
    sample_step: float = Field(default=0.5, gt=0)
    start_station: Optional[float] = None
    end_station: Optional[float] = None
    stations: Optional[list[float]] = None
    fill_factor: float = Field(default=1.0, gt=0, le=2, description="Applied to fill in the mass-haul ordinate (shrinkage/bulking)")
    strip_depth: float = Field(default=0.0, ge=0, le=2, description="Topsoil strip depth over the design footprint, m")
    csv_path: Optional[str] = None


class SurfaceOnly(_Strict):
    surface: str = Name


class SurfaceSlopes(_Strict):
    surface: str = Name
    bands_pct: Optional[list[float]] = None
    elevation_interval: float = Field(default=0.0, ge=0)


class SurfaceTransect(_Strict):
    surface: str = Name
    start: Optional[XY] = None
    end: Optional[XY] = None
    points: Optional[list[XY]] = None
    step: float = Field(default=1.0, gt=0)


class SurfaceBoundary(_Strict):
    surface: str = Name
    polyline_handle: str = Field(..., min_length=1, max_length=32)
    type: Literal["outer", "hide", "show", "data_clip"] = "outer"
    mid_ordinate: float = Field(default=0.1, gt=0)
    non_destructive: bool = True


class ExtractContours(_Strict):
    surface: str = Name
    interval: float = Field(default=1.0, gt=0)
    layer: Optional[str] = None


class SampleLineGroupRef(_Strict):
    alignment: str = Name
    group: str = Name


class ExportSections(_Strict):
    alignment: str = Name
    group: str = Name
    sources: Optional[list[str]] = None
    csv_path: Optional[str] = None


class CreateSectionViews(_Strict):
    alignment: str = Name
    group: str = Name
    origin: XY
    columns: int = Field(default=5, ge=1, le=50)
    spacing_x: float = Field(default=60.0, gt=0)
    spacing_y: float = Field(default=40.0, gt=0)
    start_station: Optional[float] = None
    end_station: Optional[float] = None
    max_views: int = Field(default=200, ge=1, le=2000)


# ─── Corridor surfaces ────────────────────────────────────────────────────

class CorridorOnly(_Strict):
    corridor: str = Name


class CreateCorridorSurface(_Strict):
    corridor: str = Name
    name: str = Name
    link_codes: list[str] = Field(default_factory=lambda: ["Top"])
    feature_line_codes: list[str] = Field(default_factory=list)
    overhang: Literal["none", "top", "bottom"] = "none"
    add_extents_boundary: bool = True
    rebuild: bool = True


# ─── Styles / geometry / parcels / pressure ──────────────────────────────

class ListStyles(_Strict):
    collection: Optional[str] = Field(default=None, description="e.g. alignment, profile, surface, profile_view, section_view, point, corridor; omit for all")


class SetObjectStyle(_Strict):
    object_type: str = Field(..., description="alignment | profile | surface | corridor | <collection> with handle")
    style: str = Name
    name: Optional[str] = None
    alignment: Optional[str] = None
    handle: Optional[str] = None


class CreatePolyline(_Strict):
    points: list[XY] = Field(..., min_length=2)
    closed: bool = False
    elevation: float = 0.0
    layer: Optional[str] = None


class Create3dPolyline(_Strict):
    points: list[list[float]] = Field(..., min_length=2, description="[[x, y, z], ...]")
    closed: bool = False
    layer: Optional[str] = None


class CreateText(_Strict):
    text: str = Field(..., min_length=1, max_length=2000)
    x: float
    y: float
    height: float = Field(default=2.5, gt=0)
    rotation_deg: float = 0.0
    layer: Optional[str] = None
    mtext: bool = False
    width: float = Field(default=0.0, ge=0)


class MeasurePolylines(_Strict):
    layers: Optional[list[str]] = Field(default=None, description="Layer wildcards, e.g. ['C-KERB*', 'C-DRAIN*']")
    closed_only: bool = False
    include_items: bool = False


class HandleRef(_Strict):
    handle: str = Field(..., min_length=1, max_length=32)


class EraseEntities(_Strict):
    handles: list[str] = Field(..., min_length=1, max_length=5000)


class CsvOnly(_Strict):
    csv_path: Optional[str] = None


class PressureNetworkRef(_Strict):
    network: str = Name
    csv_path: Optional[str] = None


# ─── Engineering calculators (pure Python) ───────────────────────────────

class SpeedInput(_Strict):
    design_speed_kph: float = Field(..., gt=0, le=140)
    grade_pct: float = Field(default=0.0, ge=-12, le=12, description="Approach grade for SSD (negative = downgrade)")
    reaction_time_s: float = Field(default=2.5, gt=0, le=5)
    e_max_pct: float = Field(default=8.0, gt=0, le=12)


class CogoInverse(_Strict):
    from_xy: XY
    to_xy: XY


class CogoForward(_Strict):
    from_xy: XY
    bearing_deg: float = Field(..., description="Whole-circle bearing from grid north, clockwise")
    distance: float = Field(..., gt=0)


class CurveSolve(_Strict):
    radius: float = Field(..., gt=0)
    deflection_deg: float = Field(..., gt=0, lt=360)
    pi_station: Optional[float] = None


class TraverseLeg(_Strict):
    bearing_deg: float
    distance: float = Field(..., gt=0)


class Traverse(_Strict):
    start_xy: XY
    legs: list[TraverseLeg] = Field(..., min_length=2)
    close_xy: Optional[XY] = Field(default=None, description="Known closing coordinate; omit for a loop back to start")


class SuperelevationRunoff(_Strict):
    design_speed_kph: float = Field(..., gt=0, le=130)
    e_design_pct: float = Field(..., gt=0, le=12)
    lane_width_m: float = Field(default=3.5, gt=0)
    lanes_rotated: float = Field(default=1.0, gt=0, le=3.5, description="n1, lanes rotated each side of the axis")
    normal_crossfall_pct: float = Field(default=2.5, gt=0, le=5)


class EndAreaRow(_Strict):
    station: float
    cut_area: float = Field(..., ge=0)
    fill_area: float = Field(..., ge=0)


class EndAreaTable(_Strict):
    rows: list[EndAreaRow] = Field(..., min_length=2)
    fill_factor: float = Field(default=1.0, gt=0, le=2)
