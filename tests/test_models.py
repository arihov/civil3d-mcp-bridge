"""Unit tests for civil3d_mcp.models — schema validation behaviour.

Run:
    cd server && pytest -q ../tests/test_models.py
"""

from __future__ import annotations

import sys
from pathlib import Path

import pytest
from pydantic import ValidationError

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "server" / "src"))

from civil3d_mcp.models import (  # noqa: E402
    AlignmentByPoly,
    AlignmentName,
    CreateLayer,
    CulvertAtChainage,
    DesignSpeed,
    GenericCall,
    KmPostSeries,
    LaneMarking,
    OffsetAlignment,
    Signpost,
)


# ---------------------------------------------------------------------------
# Strict mode — extras forbidden, validation enforced.
# ---------------------------------------------------------------------------

def test_alignment_name_rejects_empty():
    with pytest.raises(ValidationError):
        AlignmentName(name="")


def test_alignment_name_rejects_unknown_field():
    with pytest.raises(ValidationError):
        AlignmentName(name="CL-01", color="red")  # type: ignore[call-arg]


def test_alignment_name_strips_whitespace():
    m = AlignmentName(name="  CL-01  ")
    assert m.name == "CL-01"


# ---------------------------------------------------------------------------
# Numerics — positive-only constraints.
# ---------------------------------------------------------------------------

def test_design_speed_rejects_non_positive():
    with pytest.raises(ValidationError):
        DesignSpeed(alignment="A", station=0, speed_kph=0)
    with pytest.raises(ValidationError):
        DesignSpeed(alignment="A", station=0, speed_kph=-30)


def test_design_speed_accepts_valid():
    m = DesignSpeed(alignment="A", station=0, speed_kph=80)
    assert m.speed_kph == 80
    assert m.number is None  # optional


def test_lane_marking_validates_type_literal():
    with pytest.raises(ValidationError):
        LaneMarking(alignment="A", start_station=0, end_station=10,
                    offset=0, type="zigzag")  # type: ignore[arg-type]


def test_lane_marking_defaults():
    m = LaneMarking(alignment="A", start_station=0, end_station=10, offset=0)
    assert m.type == "solid"
    assert m.width == 0.15
    assert m.color_index == 7


# ---------------------------------------------------------------------------
# Culvert — required fields + numeric ranges.
# ---------------------------------------------------------------------------

def test_culvert_requires_inverts():
    with pytest.raises(ValidationError):
        CulvertAtChainage(  # type: ignore[call-arg]
            alignment="CL", station=4500, diameter=900, length=20,
            invert_in=1230,  # missing invert_out
            network_name="N",
        )


def test_culvert_rejects_zero_diameter():
    with pytest.raises(ValidationError):
        CulvertAtChainage(
            alignment="CL", station=4500, diameter=0, length=20,
            invert_in=1230, invert_out=1229, network_name="N",
        )


def test_culvert_valid():
    m = CulvertAtChainage(
        alignment="CL", station=4500, diameter=900, length=20,
        invert_in=1230, invert_out=1229.7, network_name="Drainage",
        skew_degrees=15.0,
    )
    assert m.diameter == 900
    assert m.skew_degrees == 15.0
    assert m.pipe_family is None  # optional


# ---------------------------------------------------------------------------
# Signpost / KM post — literal sides.
# ---------------------------------------------------------------------------

def test_signpost_validates_side():
    with pytest.raises(ValidationError):
        Signpost(alignment="A", station=100, side="middle")  # type: ignore[arg-type]


def test_signpost_defaults():
    m = Signpost(alignment="A", station=100)
    assert m.side == "right"
    assert m.offset == 3.0
    assert m.rotation == "perpendicular"


def test_km_post_series_defaults():
    m = KmPostSeries(alignment="A")
    assert m.interval == 1000.0
    assert m.layer == "C-ROAD-KMPOST"


# ---------------------------------------------------------------------------
# Offset alignment — distinguishes offset+name vs just offset.
# ---------------------------------------------------------------------------

def test_offset_alignment_requires_all():
    with pytest.raises(ValidationError):
        OffsetAlignment(source_alignment="A", offset=3.5)  # type: ignore[call-arg]
    m = OffsetAlignment(source_alignment="A", offset=3.5, name="A-Right")
    assert m.name == "A-Right"


# ---------------------------------------------------------------------------
# Layer creation — colour index bounds.
# ---------------------------------------------------------------------------

def test_create_layer_rejects_bad_colour():
    with pytest.raises(ValidationError):
        CreateLayer(name="X", color_index=0)
    with pytest.raises(ValidationError):
        CreateLayer(name="X", color_index=256)


# ---------------------------------------------------------------------------
# Generic call — args dict permitted, tool name required.
# ---------------------------------------------------------------------------

def test_generic_call_empty_args_ok():
    m = GenericCall(tool="civil3d_list_alignments")
    assert m.args == {}


def test_generic_call_passes_through_args():
    m = GenericCall(tool="civil3d_xyz", args={"x": 1, "y": "two"})
    assert m.args == {"x": 1, "y": "two"}


# ---------------------------------------------------------------------------
# Alignment from polyline — handle length sanity.
# ---------------------------------------------------------------------------

def test_alignment_by_poly_requires_handle():
    with pytest.raises(ValidationError):
        AlignmentByPoly(name="A", polyline_handle="")
    m = AlignmentByPoly(name="A", polyline_handle="2BA1F")
    assert m.erase_polyline is False
