# Tests

Three layers of testing, designed to run without Civil 3D being installed
where possible:

| Test                  | What it exercises                                      | Civil 3D needed? |
|-----------------------|--------------------------------------------------------|------------------|
| `test_models.py`      | Pydantic model validation rules                        | No               |
| `mock_bridge.py`      | A standalone HTTP simulator of the C# bridge protocol  | No               |
| `integration_test.py` | The full Python MCP layer end-to-end                   | No (with mock)   |
| `smoke.ps1`           | The real C# bridge over HTTP                           | Yes              |

## 1. Unit tests — models only

Run pytest against the Pydantic schemas. No bridge needed.

```bash
cd server
pip install -e .[dev]
cd ..
PYTHONPATH=server/src pytest -q tests/test_models.py
```

Expected: ~18 tests pass in under a second.

## 2. Mock bridge

`mock_bridge.py` runs an aiohttp listener on `127.0.0.1:7800` that
speaks the same JSON protocol as the C# bridge but answers with canned
plausible data. Useful when you don't have Civil 3D handy.

```bash
# Terminal 1
python tests/mock_bridge.py
```

In another terminal:

```bash
curl http://127.0.0.1:7800/health
curl -X POST http://127.0.0.1:7800/invoke \
  -H "Content-Type: application/json" \
  -d '{"tool":"civil3d_list_alignments","args":{}}'
```

To point Claude Desktop at the mock bridge while you iterate on tool
descriptions or prompts, set the env in `claude_desktop_config.json`:

```json
"civil3d": {
  "command": "civil3d-mcp",
  "env": { "CIVIL3D_BRIDGE_URL": "http://127.0.0.1:7800" }
}
```

## 3. Integration test

`integration_test.py` walks every registered Python tool that's safe to
call (read-only) and reports pass/fail. Combine with `--against-mock` to
auto-launch the mock bridge.

```bash
PYTHONPATH=server/src python tests/integration_test.py --against-mock
```

Expected output:

```
civil3d-mcp integration test against http://127.0.0.1:7800

  PASS civil3d_bridge_health
  PASS civil3d_list_alignments
  PASS civil3d_list_corridors
  ...
  PASS civil3d_get_alignment_info
  ...

  16 passed   0 failed
```

To run against the real C# bridge instead, just drop `--against-mock`
(start Civil 3D + open a DWG with at least one alignment first).

## 4. Smoke test against real Civil 3D

After installing the add-in and opening Civil 3D:

```powershell
.\tests\smoke.ps1
```

Hits `/health` then makes two tool calls. Fails loudly if the bridge
isn't reachable — the most common cause is Civil 3D not being open or
the add-in not having been loaded by `MCPSTART`.

## Troubleshooting

**`mock_bridge.py` fails to start with "address already in use"**
The real C# bridge is probably running. Kill it with `MCPSTOP` at the
Civil 3D command line, or run the mock on a different port:

```bash
python tests/mock_bridge.py  # then edit script to change port
```

**`integration_test.py` reports `bridge unreachable`**
Either Civil 3D isn't running, or the add-in isn't loaded. Use
`MCPSTATUS` at the Civil 3D command line — it should report
"listening on http://127.0.0.1:7800". If it reports "stopped", run
`MCPSTART`.

**Models test fails after editing `models.py`**
The schema changed. Update `tests/test_models.py` to match — or delete
the failing case if the field it tested no longer exists.
