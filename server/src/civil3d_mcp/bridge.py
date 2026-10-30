"""HTTP client for the in-process Civil 3D bridge, with multi-session discovery.

Resolution order for the bridge URL:
  1. A session picked at runtime with civil3d_select_instance.
  2. CIVIL3D_BRIDGE_URL (explicit pin, e.g. http://127.0.0.1:7827).
  3. Discovery: live bridges registered in
     %LOCALAPPDATA%/Civil3DMcpBridge/instances/*.json. CIVIL3D_RELEASE
     (e.g. 2027) narrows the choice; otherwise the newest release wins.
  4. Legacy fallback http://127.0.0.1:7800 (bridges older than v0.4).
"""

from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Any

import httpx


class BridgeError(Exception):
    """Raised when the bridge returns ok=false or the HTTP call fails."""


LEGACY_URL = "http://127.0.0.1:7800"
_client: httpx.AsyncClient | None = None
_client_url: str | None = None
_selected_url: str | None = None
_target_document: str | None = None


def _timeout_s() -> float:
    try:
        return float(os.environ.get("CIVIL3D_TIMEOUT_S", "300"))
    except ValueError:
        return 300.0


# ─── discovery ─────────────────────────────────────────────────────────────

def instances_dir() -> Path:
    base = os.environ.get("LOCALAPPDATA") or str(Path.home() / "AppData" / "Local")
    return Path(base) / "Civil3DMcpBridge" / "instances"


def _pid_alive(pid: int) -> bool:
    if pid <= 0:
        return False
    if os.name == "nt":
        import ctypes

        PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
        h = ctypes.windll.kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
        if not h:
            return False
        code = ctypes.c_ulong()
        ok = ctypes.windll.kernel32.GetExitCodeProcess(h, ctypes.byref(code))
        ctypes.windll.kernel32.CloseHandle(h)
        return bool(ok) and code.value == 259  # STILL_ACTIVE
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


def discover_instances(prune: bool = True) -> list[dict[str, Any]]:
    """Registered bridges whose Civil 3D process is still alive, newest release first."""
    found: list[dict[str, Any]] = []
    d = instances_dir()
    if not d.is_dir():
        return found
    for f in d.glob("*.json"):
        try:
            info = json.loads(f.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        if not _pid_alive(int(info.get("pid", 0))):
            if prune:
                try:
                    f.unlink()
                except OSError:
                    pass
            continue
        found.append(info)
    found.sort(key=lambda i: (int(i.get("release", 0)), i.get("startedUtc", "")), reverse=True)
    return found


def bridge_url() -> str:
    if _selected_url:
        return _selected_url
    if env := os.environ.get("CIVIL3D_BRIDGE_URL"):
        return env.rstrip("/")
    live = discover_instances()
    want = os.environ.get("CIVIL3D_RELEASE")
    if want:
        live = [i for i in live if str(i.get("release")) == want.strip()] or live
    if live:
        return str(live[0]["url"]).rstrip("/")
    return LEGACY_URL


def select_instance(*, release: int | None = None, pid: int | None = None,
                    port: int | None = None, url: str | None = None) -> dict[str, Any]:
    """Pin subsequent calls to one Civil 3D session. Returns the chosen instance."""
    global _selected_url
    if url:
        _selected_url = url.rstrip("/")
        return {"url": _selected_url, "pinned": True}
    live = discover_instances()
    for i in live:
        if ((release is None or int(i.get("release", 0)) == release)
                and (pid is None or int(i.get("pid", 0)) == pid)
                and (port is None or int(i.get("port", 0)) == port)):
            _selected_url = str(i["url"]).rstrip("/")
            return {**i, "pinned": True}
    raise BridgeError(
        "no running Civil 3D bridge matches "
        f"release={release} pid={pid} port={port}. Live sessions: "
        + (", ".join(f"{i.get('release')} (pid {i.get('pid')}, port {i.get('port')})" for i in live) or "none")
    )


def clear_selection() -> None:
    global _selected_url
    _selected_url = None


def set_target_document(name: str | None) -> None:
    """Route subsequent calls to a named open drawing (None = the active one)."""
    global _target_document
    _target_document = name or None


def target_document() -> str | None:
    return _target_document


# ─── transport ─────────────────────────────────────────────────────────────

async def _get_client() -> httpx.AsyncClient:
    global _client, _client_url
    url = bridge_url()
    if _client is None or _client.is_closed or _client_url != url:
        if _client is not None and not _client.is_closed:
            await _client.aclose()
        _client = httpx.AsyncClient(base_url=url, timeout=httpx.Timeout(_timeout_s() + 15, connect=5.0))
        _client_url = url
    return _client


async def invoke(tool: str, args: dict[str, Any] | None = None, *,
                 document: str | None = None, timeout_s: float | None = None) -> Any:
    """POST /invoke and return the unwrapped `result` field."""
    client = await _get_client()
    payload: dict[str, Any] = {"tool": tool, "args": args or {}}
    doc = document or _target_document
    if doc:
        payload["document"] = doc
    t = timeout_s or _timeout_s()
    payload["timeoutMs"] = int(t * 1000)
    try:
        response = await client.post("/invoke", json=payload, timeout=t + 15)
    except httpx.ConnectError as exc:
        raise BridgeError(
            f"could not reach Civil 3D bridge at {bridge_url()}. Is Civil 3D running with the add-in loaded? "
            "Run MCPSTATUS at the Civil 3D command line, or civil3d_list_instances here."
        ) from exc
    except httpx.TimeoutException as exc:
        raise BridgeError(f"bridge call '{tool}' timed out after {t:.0f}s. Civil 3D may be busy.") from exc

    try:
        body = response.json()
    except ValueError as exc:
        raise BridgeError(f"bridge returned non-JSON (status {response.status_code}): {response.text[:200]}") from exc

    if not body.get("ok"):
        msg = body.get("error") or f"unknown bridge error (status {response.status_code})"
        if hint := body.get("hint"):
            msg = f"{msg}\nhint: {hint}"
        raise BridgeError(msg)
    return body.get("result")


async def health() -> dict[str, Any]:
    """GET /health on the resolved bridge."""
    client = await _get_client()
    try:
        response = await client.get("/health", timeout=10)
        data = response.json()
        data.setdefault("url", bridge_url())
        return data
    except (httpx.ConnectError, httpx.TimeoutException) as exc:
        raise BridgeError(f"bridge unreachable at {bridge_url()}") from exc


async def close() -> None:
    global _client
    if _client is not None and not _client.is_closed:
        await _client.aclose()
    _client = None
