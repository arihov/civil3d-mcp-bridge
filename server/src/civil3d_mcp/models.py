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
