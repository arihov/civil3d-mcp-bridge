"""Unit tests for civil3d_mcp.engineering and the v0.4 discovery logic.

Reference values: AASHTO Green Book 2018 metric design tables
(SSD Table 3-1, crest K Table 3-34, sag K Table 3-36, R_min Table 3-7).
"""

from __future__ import annotations

import json
import math
import os
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "server" / "src"))

from civil3d_mcp import bridge  # noqa: E402
from civil3d_mcp import engineering as eng  # noqa: E402


@pytest.mark.parametrize("v, table_ssd", [(50, 65), (60, 85), (80, 130), (100, 185), (120, 250)])
def test_ssd_matches_aashto_table(v, table_ssd):
    # design values are the computed SSD rounded UP to 5 m
    assert 0 <= table_ssd - eng.stopping_sight_distance(v) < 5.0 or abs(table_ssd - eng.stopping_sight_distance(v)) < 1.5


@pytest.mark.parametrize("v, k_crest, k_sag", [(60, 11, 18), (80, 26, 30), (100, 52, 45)])
def test_k_values_match_aashto_tables(v, k_crest, k_sag):
    s = eng.stopping_sight_distance(v)
    assert eng.k_crest(s) == pytest.approx(k_crest, rel=0.08)
    assert eng.k_sag(s) == pytest.approx(k_sag, rel=0.08)


@pytest.mark.parametrize("v, e, table_r", [(60, 8, 113), (60, 6, 123), (80, 8, 229), (100, 8, 394)])
def test_min_radius_matches_aashto(v, e, table_r):
    r, _ = eng.min_radius(v, e)
    assert r == pytest.approx(table_r, abs=2.0)


def test_downgrade_increases_ssd():
    assert eng.stopping_sight_distance(80, -6) > eng.stopping_sight_distance(80, 0) > eng.stopping_sight_distance(80, 6)


def test_inverse_and_forward_round_trip():
    a = [450000.0, 9970000.0]
    inv = eng.inverse(a, [450100.0, 9970100.0])
    assert inv["bearingDeg"] == pytest.approx(45.0)
    assert inv["distance"] == pytest.approx(141.4214, abs=1e-4)
    fwd = eng.forward(a, inv["bearingDeg"], inv["distance"])
    assert fwd["x"] == pytest.approx(450100.0, abs=1e-3)
    assert fwd["y"] == pytest.approx(9970100.0, abs=1e-3)


def test_dms_formatting():
    assert eng.dms(45.5) == "045°30'00.00\""


def test_curve_solution():
    c = eng.solve_curve(300, 30, pi_station=1000)
    assert c["tangentLength"] == pytest.approx(300 * math.tan(math.radians(15)), abs=1e-3)
    assert c["curveLength"] == pytest.approx(157.0796, abs=1e-3)
    assert c["ctStation"] == pytest.approx(1000 - c["tangentLength"] + c["curveLength"], abs=1e-3)


def test_bowditch_closes_loop():
    legs = [(0, 100.02), (90, 100.0), (180, 99.97), (270, 100.01)]
    res = eng.bowditch([0, 0], legs, None)
    assert res["adjusted"][-1] == pytest.approx([0, 0], abs=1e-4)
    assert res["linearMisclosure"] == pytest.approx(math.hypot(-0.01, 0.05), abs=1e-4)


def test_superelevation_runoff_example():
    # AASHTO-style example: 100 km/h, e=8%, one 3.6 m lane rotated -> 3.6*1*8*1.0/0.44 = 65.5 m
    r = eng.superelevation_runoff(100, 8, 3.6, 1, 2)
    assert r["runoffLength_m"] == pytest.approx(65.5, abs=0.2)
    assert r["tangentRunout_m"] == pytest.approx(65.5 * 2 / 8, abs=0.2)


def test_average_end_area():
    r = eng.average_end_area([(0, 10, 0), (20, 20, 5), (40, 0, 15)])
    assert r["totalCut"] == pytest.approx(20 * 15 + 20 * 10)
    assert r["totalFill"] == pytest.approx(20 * 2.5 + 20 * 10)


def test_discovery_prefers_newest_live_release(tmp_path, monkeypatch):
    inst = tmp_path / "Civil3DMcpBridge" / "instances"
    inst.mkdir(parents=True)
    me = os.getpid()
    (inst / "1.json").write_text(json.dumps({"pid": me, "port": 7825, "url": "http://127.0.0.1:7825", "release": 2025}))
    (inst / "2.json").write_text(json.dumps({"pid": me, "port": 7827, "url": "http://127.0.0.1:7827", "release": 2027}))
    (inst / "3.json").write_text(json.dumps({"pid": 999999, "port": 7840, "url": "http://127.0.0.1:7840", "release": 2028}))
    monkeypatch.setenv("LOCALAPPDATA", str(tmp_path))
    monkeypatch.delenv("CIVIL3D_BRIDGE_URL", raising=False)
    monkeypatch.delenv("CIVIL3D_RELEASE", raising=False)
    bridge.clear_selection()
    assert bridge.bridge_url() == "http://127.0.0.1:7827"
    assert not (inst / "3.json").exists(), "dead instance should be pruned"
    monkeypatch.setenv("CIVIL3D_RELEASE", "2025")
    assert bridge.bridge_url() == "http://127.0.0.1:7825"
    monkeypatch.delenv("CIVIL3D_RELEASE")
    bridge.select_instance(release=2025)
    assert bridge.bridge_url() == "http://127.0.0.1:7825"
    bridge.clear_selection()
    monkeypatch.setenv("CIVIL3D_BRIDGE_URL", "http://127.0.0.1:7999/")
    assert bridge.bridge_url() == "http://127.0.0.1:7999"
