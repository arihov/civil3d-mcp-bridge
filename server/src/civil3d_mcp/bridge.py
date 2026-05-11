"""HTTP client for the in-process Civil 3D bridge."""

from __future__ import annotations

import os
from typing import Any

import httpx


class BridgeError(Exception):
    """Raised when the bridge returns ok=false or the HTTP call fails."""


_client: httpx.AsyncClient | None = None


def bridge_url() -> str:
    return os.environ.get("CIVIL3D_BRIDGE_URL", "http://127.0.0.1:7800").rstrip("/")


async def _get_client() -> httpx.AsyncClient:
    global _client
    if _client is None or _client.is_closed:
        # 120s default — corridor rebuilds on long road projects (e.g. the
        # 89.5 km MKM corridor) can take 30-90s on typical hardware.
        _client = httpx.AsyncClient(
            base_url=bridge_url(),
            timeout=httpx.Timeout(120.0, connect=5.0),
        )
    return _client


async def invoke(tool: str, args: dict[str, Any] | None = None) -> Any:
    """POST /invoke and return the unwrapped `result` field.

    On bridge error (ok=false) raises BridgeError with a readable message.
    On transport error raises BridgeError with troubleshooting hint.
    """
    client = await _get_client()
    payload = {"tool": tool, "args": args or {}}
    try:
        response = await client.post("/invoke", json=payload)
    except httpx.ConnectError as exc:
        raise BridgeError(
            f"could not reach Civil 3D bridge at {bridge_url()} — "
            "is Civil 3D running with the add-in loaded? "
            "Run MCPSTATUS at the Civil 3D command line to check."
        ) from exc
    except httpx.TimeoutException as exc:
        raise BridgeError(
            f"bridge call '{tool}' timed out after 120s — "
            "operation is slower than expected, or Civil 3D is busy."
        ) from exc

    try:
        body = response.json()
    except ValueError as exc:
        raise BridgeError(
            f"bridge returned non-JSON (status {response.status_code}): "
            f"{response.text[:200]}"
        ) from exc

    if not body.get("ok"):
        msg = body.get("error") or f"unknown bridge error (status {response.status_code})"
        if hint := body.get("hint"):
            msg = f"{msg}\nhint: {hint}"
        raise BridgeError(msg)

    return body.get("result")


async def health() -> dict[str, Any]:
    """GET /health — returns ok status and the list of registered tools."""
    client = await _get_client()
    try:
        response = await client.get("/health")
        return response.json()
    except (httpx.ConnectError, httpx.TimeoutException) as exc:
        raise BridgeError(f"bridge unreachable at {bridge_url()}") from exc


async def close() -> None:
    global _client
    if _client is not None and not _client.is_closed:
        await _client.aclose()
    _client = None
