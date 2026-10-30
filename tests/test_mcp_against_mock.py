"""End-to-end: MCP tools -> HTTP -> mock bridge (no Civil 3D needed).

Exercises the v0.4 design checks, session tools and the generic fallback.
"""

from __future__ import annotations

import json
import socket
import sys
from pathlib import Path

import pytest
from aiohttp.test_utils import TestServer

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "server" / "src"))
sys.path.insert(0, str(ROOT / "tests"))

import mock_bridge  # noqa: E402
from civil3d_mcp import bridge  # noqa: E402
from civil3d_mcp.server import mcp  # noqa: E402


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


@pytest.fixture
async def mock_url(monkeypatch):
    port = _free_port()
    server = TestServer(mock_bridge.make_app(), host="127.0.0.1", port=port)
    await server.start_server()
    url = f"http://127.0.0.1:{port}"
    monkeypatch.setenv("CIVIL3D_BRIDGE_URL", url)
    bridge.clear_selection()
    yield url
    await bridge.close()
    await server.close()


async def _call(name: str, args: dict | None = None) -> dict:
    res = await mcp.call_tool(name, {"params": args} if args is not None else {})
    content = res[0] if isinstance(res, tuple) else res
    return json.loads(content[0].text)


async def test_health_reports_release(mock_url):
    data = await _call("civil3d_bridge_health")
    assert data["ok"] and data["release"] == 2027
    assert data["url"] == mock_url


async def test_alignment_check_flags_small_radius_and_broken_back(mock_url):
    data = await _call("civil3d_check_alignment_design",
                       {"alignment": "CL-01", "design_speed_kph": 80, "e_max_pct": 8})
    issues = " | ".join(i["issue"] for i in data["issues"])
    assert data["failCount"] == 1, issues          # R=180 < 229
    assert "broken-back" in issues                  # 30 m between same-direction curves
    assert data["controls"]["minRadius_m"] == pytest.approx(229.1, abs=0.2)


async def test_profile_check(mock_url):
    data = await _call("civil3d_check_profile_design",
                       {"alignment": "CL-01", "profile": "FG", "design_speed_kph": 80, "max_grade_pct": 8})
    text = " | ".join(i["issue"] for i in data["issues"])
    assert "K=13.3 below minimum" in text
    assert "exceeds max" in text
    assert "below drainage minimum" in text
    assert "grade break" in text


async def test_document_routing_is_sent(mock_url):
    await _call("civil3d_select_document", {"document": "MKM_Corridor.dwg"})
    try:
        assert bridge.target_document() == "MKM_Corridor.dwg"
    finally:
        await _call("civil3d_select_document", {})
    assert bridge.target_document() is None


async def test_generic_new_tool_round_trip(mock_url):
    data = await _call("civil3d_earthwork_by_sections",
                       {"alignment": "CL-01", "existing_surface": "EG", "design_surface": "FG"})
    assert "error" not in data


async def test_calculators_need_no_bridge():
    data = await _call("calc_design_controls", {"design_speed_kph": 100})
    assert data["kCrestMin"] == pytest.approx(52, abs=1.5)
    assert data["minRadius_m"] == pytest.approx(393.7, abs=0.5)
