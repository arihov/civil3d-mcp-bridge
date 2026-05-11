"""Integration test runner.

Walks the registered Python MCP tools and calls each one. Useful both
against the real C# bridge (Civil 3D running) and against the mock bridge
in tests/mock_bridge.py.

Run:
    python tests/integration_test.py                  # real bridge
    python tests/integration_test.py --against-mock   # auto-launches the mock bridge

Exit code is 0 if every read-only tool returned without raising, 1 if any
failed. Write-tools are skipped by default — pass --include-writes if
you're running against a throwaway DWG you don't mind being modified.
"""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

# Make the server package importable without installing.
ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "server" / "src"))

# Tools we treat as safe (read-only or trivially write-safe).
READ_ONLY = {
    "civil3d_bridge_health": {},
    "civil3d_list_alignments": {},
    "civil3d_list_corridors": {},
    "civil3d_list_surfaces": {},
    "civil3d_list_blocks": {},
    "civil3d_list_layers": {},
    "civil3d_list_pipe_networks": {},
    "civil3d_list_pipe_parts": {},
    "civil3d_list_point_groups": {},
    "civil3d_list_assemblies": {},
    "civil3d_list_sample_line_groups": {},
    "civil3d_get_drawing_info": {},
    "civil3d_list_points": {"limit": 10, "offset": 0},
    # Parametric reads — need an alignment, supplied at runtime.
}

# Parametric tools that take an alignment. Argument scaffolding is filled
# in at runtime by reading the first alignment from civil3d_list_alignments.
# FastMCP binds a `params: SomeModel` argument to {"params": {...}} on the wire.
PARAMETRIC_READS = [
    ("civil3d_get_alignment_info", lambda al: {"params": {"name": al}}),
    ("civil3d_get_xy_at_station", lambda al: {"params": {"alignment": al, "station": 0.0, "offset": 0.0}}),
    ("civil3d_point_at_chainage", lambda al: {"params": {"alignment": al, "station": 0.0, "offset": 0.0}}),
    ("civil3d_list_profiles", lambda al: {"params": {"alignment": al}}),
]


def colour(text: str, code: int) -> str:
    if not sys.stdout.isatty():
        return text
    return f"\033[{code}m{text}\033[0m"


def green(s: str) -> str: return colour(s, 32)
def red(s: str)   -> str: return colour(s, 31)
def cyan(s: str)  -> str: return colour(s, 36)
def dim(s: str)   -> str: return colour(s, 90)


async def run_tool(mcp: Any, name: str, args: dict[str, Any]) -> tuple[bool, str]:
    """Invoke a registered tool through the FastMCP runtime and parse its
    JSON response. FastMCP returns (content_blocks, structured_result_dict)
    as a tuple — we use the structured dict if present, else the first text
    block."""
    try:
        result = await mcp.call_tool(name, args)
        # New FastMCP shape: (list[Content], dict[str, str])
        if isinstance(result, tuple) and len(result) == 2:
            blocks, structured = result
            if isinstance(structured, dict) and "result" in structured:
                text = structured["result"]
            elif isinstance(blocks, list) and blocks:
                text = blocks[0].text
            else:
                text = str(result)
        # Legacy: list[Content]
        elif isinstance(result, list) and result:
            text = result[0].text
        else:
            text = str(result)
        try:
            payload = json.loads(text)
        except json.JSONDecodeError:
            return False, f"non-JSON response: {text[:200]}"
        if isinstance(payload, dict) and "error" in payload:
            return False, payload["error"]
        return True, text
    except Exception as exc:  # noqa: BLE001
        return False, f"{type(exc).__name__}: {exc}"


async def main(args: argparse.Namespace) -> int:
    if args.against_mock:
        mock = launch_mock_bridge()
        # Give aiohttp a moment to bind.
        time.sleep(1.0)
    else:
        mock = None

    try:
        from civil3d_mcp._instance import mcp
        from civil3d_mcp import tools  # noqa: F401 — side-effect import

        print(cyan(f"civil3d-mcp integration test against {os.environ.get('CIVIL3D_BRIDGE_URL', 'http://127.0.0.1:7800')}"))
        print()

        passed: list[str] = []
        failed: list[tuple[str, str]] = []

        # 1. Static / no-arg read tools.
        for name, params in READ_ONLY.items():
            ok, msg = await run_tool(mcp, name, params)
            if ok:
                print(f"  {green('PASS')} {name}")
                passed.append(name)
            else:
                print(f"  {red('FAIL')} {name}: {msg}")
                failed.append((name, msg))

        # 2. Parametric reads — need a real alignment name.
        ok_list, msg = await run_tool(mcp, "civil3d_list_alignments", {})
        if ok_list:
            try:
                alignments = json.loads(msg).get("alignments", [])
                first = alignments[0]["name"] if alignments else None
            except Exception:
                first = None
        else:
            first = None

        if first is None:
            print(dim("  -- skipping parametric reads (no alignment available)"))
        else:
            print(dim(f"  -- using alignment '{first}' for parametric reads"))
            for tool_name, build_args in PARAMETRIC_READS:
                ok, msg = await run_tool(mcp, tool_name, build_args(first))
                if ok:
                    print(f"  {green('PASS')} {tool_name}")
                    passed.append(tool_name)
                else:
                    print(f"  {red('FAIL')} {tool_name}: {msg}")
                    failed.append((tool_name, msg))

        print()
        print(f"  {green(str(len(passed)))} passed   {red(str(len(failed))) if failed else dim('0')} failed")
        return 0 if not failed else 1
    finally:
        if mock is not None:
            mock.terminate()
            try:
                mock.wait(timeout=3.0)
            except subprocess.TimeoutExpired:
                mock.kill()


def launch_mock_bridge() -> subprocess.Popen[Any]:
    print(cyan("launching mock bridge..."))
    return subprocess.Popen(
        [sys.executable, str(Path(__file__).parent / "mock_bridge.py")],
        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--against-mock", action="store_true",
                        help="Auto-launch tests/mock_bridge.py for the duration of the test.")
    parser.add_argument("--include-writes", action="store_true",
                        help="(Reserved) also run write-tools — needs a throwaway DWG.")
    sys.exit(asyncio.run(main(parser.parse_args())))
