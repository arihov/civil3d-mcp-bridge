# Testing

Three layers of test infrastructure ship with this repo:

| Layer | Tool | Needs Civil 3D? | What it proves |
|-------|------|-----------------|----------------|
| 1 | `pytest tests/test_models.py` | No | Pydantic models validate correctly — schema is stable. |
| 2 | `python tests/mock_bridge.py` + `python tests/integration_test.py --against-mock` | No | The Python side serialises requests correctly and parses responses without touching Civil 3D. |
| 3 | `python tests/integration_test.py` | Yes | End-to-end against a live Civil 3D + bridge. Only runs **read-only** tools by default. |
| 4 | `tests\smoke.ps1` | Yes | One-line PowerShell health check — for after install. |

---

## Layer 1 — model validation

Fast, runs offline, catches schema mistakes before they hit the bridge.

```powershell
cd server
pip install -e .[test]
cd ..
pytest tests\test_models.py -v
```

Expected: 54 model tests pass. If any fail, the bridge will reject the
corresponding tool calls.

---

## Layer 2 — Python ↔ mock bridge

Mock bridge is a tiny `aiohttp` server that pretends to be Civil 3D. It
answers `/health` and `/invoke` with canned data for every tool name.
Useful when:

* You're iterating on `models.py` or `tools/` without rebuilding the
  C# DLL.
* You want to demo the MCP integration to someone without Civil 3D
  installed.
* You're debugging the Python wire format.

In one shell:

```powershell
python tests\mock_bridge.py
# starts on http://127.0.0.1:7800
```

In another shell:

```powershell
python tests\integration_test.py --against-mock
```

This walks through every tool name with sensible default args, posts to
the mock, and asserts the response shape. Exit code 0 if all green.

You can also point Claude Desktop at the mock by leaving the
`claude_desktop_config.json` entry alone and just running
`mock_bridge.py` instead of Civil 3D — then ask Claude `Use the
civil3d_bridge_health tool` and watch the mock log the request.

---

## Layer 3 — Python ↔ real bridge ↔ Civil 3D

Run this on the Windows workstation after install:

```powershell
# Civil 3D is open with any DWG; MCPSTATUS shows the bridge is listening.
python tests\integration_test.py
```

The script:

1. Calls `/health`. Aborts if the bridge isn't up.
2. Runs every read-only tool with light args (e.g. `list_alignments`,
   `list_surfaces`, `list_points`).
3. Pretty-prints any non-empty results.
4. **Does not mutate** the drawing — safe to run against production
   files.

Add `--write` to also exercise a small set of write tools against a
disposable DWG (creates `TEST-PT-001`, `TEST-LAYER`, then cleans up).
Use only on test drawings.

---

## Layer 4 — PowerShell smoke

After install, drop into a fresh shell:

```powershell
.\tests\smoke.ps1
```

This:

1. `curl http://127.0.0.1:7800/health` and parses the JSON.
2. Confirms `toolCount >= 60`.
3. Tests that `civil3d-mcp --version` runs (proves console script is on PATH).
4. Exits 0 / 1 with a green / red banner.

---

## Adding a test

When you add a new tool, add three things:

1. **Pydantic model** test in `test_models.py` — a happy-path
   instantiation + one rejection (missing required field or wrong
   type).
2. **Mock response** in `mock_bridge.py` — a stanza in the `RESPONSES`
   dict.
3. **Integration step** in `integration_test.py` if it's read-only or
   safe-to-write.

Keep tests offline-runnable wherever possible. The bridge tests are
slow because they wait for Civil 3D's idle pump; the model tests
should stay sub-second total.

---

## CI considerations

There is no GitHub Actions config in this repo because:

* The C# build needs the .NET SDK (easy on a hosted runner).
* But the integration tests need Civil 3D, which is licence-bound and
  doesn't run in CI.

A minimal GH Actions pipeline that runs **layers 1 and 2** would
catch most regressions. Sketch:

```yaml
- uses: actions/setup-python@v5
  with: { python-version: "3.11" }
- run: pip install -e server[test]
- run: pytest tests/test_models.py
- run: python tests/mock_bridge.py &
- run: sleep 1 && python tests/integration_test.py --against-mock
```
