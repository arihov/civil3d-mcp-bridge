# Architecture

## The two halves

### C# in-process bridge (`plugin/`)

The C# side is an **in-process AutoCAD/Civil 3D add-in** built against
.NET 8 (Civil 3D 2025-2026 use this runtime). On `Initialize()`,
`Plugin.cs` does three things:

1. Registers every tool handler into `ToolRegistry` (a static
   `Dictionary<string, ToolHandler>`).
2. Wires `MainThreadDispatcher` into `Application.Idle` — this gives us
   a one-line `RunOnMainThreadAsync(Func<T>)` API.
3. Starts `BridgeServer`, an `HttpListener` bound to
   `http://127.0.0.1:7800/`.

Each HTTP request to `/invoke`:

```
POST /invoke
Content-Type: application/json
{ "tool": "civil3d_get_xy_at_station",
  "args": { "alignment": "AL-01", "station": 4500 } }
```

is parsed on a worker thread, dispatched onto the Civil 3D UI thread,
where the handler runs inside:

```csharp
using (DocumentLock doclock = doc.LockDocument())
using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
{
    // ... read or write the database ...
    tr.Commit();
    return result;
}
```

The result is JSON-serialised by a hand-written `Json.cs` (no Newtonsoft
dependency — keeps the DLL small and Civil-3D-version-stable) and
written back on the worker thread.

#### Error model

* `ToolException` → HTTP 400 with `{"error": "..."}` (a human-readable
  message that the user is meant to read).
* Any other exception → HTTP 500 with `{"error": "<type>: <message>"}`.
  These are bugs — the stack trace also goes to Civil 3D's editor with
  the prefix `[civil3d-mcp]`.

#### Marshalling

Civil 3D's API is **not thread-safe**. The bridge runs on a `ThreadPool`
but every database touch must happen on the UI thread. The flow:

```
ThreadPool worker:                Civil 3D UI thread (Idle event):
  receive HTTP POST                 ─► dequeue Action
  parse JSON                         ─► run handler in transaction
  Dispatcher.RunOnMainThread(...)    ─► SetResult on TCS
  await TCS                          
  write HTTP response
```

The dispatcher uses a `ConcurrentQueue<Action>` and the dispatched
`Action` resolves a `TaskCompletionSource<T>`. Latency is bounded by
how often Civil 3D pumps `Idle` — typically sub-50 ms when the editor
is responsive.

### Python FastMCP server (`server/`)

The Python side is a **thin MCP wrapper**. Every tool is one function:

```python
@mcp.tool(annotations=READ_ANN, title="Get XY at chainage")
async def civil3d_get_xy_at_station(args: GetXyAtStationArgs) -> dict:
    return await call("civil3d_get_xy_at_station", args.model_dump(exclude_none=True))
```

`call()` (in `_instance.py`) just `POST`s to the bridge and returns the
parsed JSON. There is no business logic on the Python side — keep it
that way.

#### Why Pydantic?

Two reasons:

1. **MCP schema**: FastMCP introspects the model to publish a JSON
   schema to the client. This gets you autocomplete + validation in
   Claude Desktop.
2. **Validation before HTTP**: catches typos and out-of-range values
   without a round-trip into Civil 3D.

All models use `model_config = ConfigDict(extra="forbid")` — unknown
fields raise immediately rather than being silently dropped.

#### Annotations

`READ_ANN` and `WRITE_ANN` in `_instance.py` advertise read-only vs
mutating tools to MCP clients so the UI can warn the user before a
destructive call.

---

## Adding a new tool

Concrete example: add `civil3d_get_alignment_length`.

### 1. C# handler

In `plugin/src/Tools/AlignmentTools.cs`:

```csharp
public static object GetAlignmentLength(Dictionary<string, object> args)
{
    string alignmentName = Tools.GetString(args, "alignment");
    Document doc = Application.DocumentManager.MdiActiveDocument;
    using (DocumentLock doclock = doc.LockDocument())
    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
    {
        Alignment al = Helpers.FindAlignment(tr, doc, alignmentName)
            ?? throw new ToolException($"alignment '{alignmentName}' not found");
        double length = al.Length;
        tr.Commit();
        return new Dictionary<string, object>
        {
            ["alignment"] = alignmentName,
            ["length"] = length
        };
    }
}
```

### 2. Register it

In `plugin/src/Plugin.cs`, inside `RegisterTools()`:

```csharp
ToolRegistry.Register("civil3d_get_alignment_length", AlignmentTools.GetAlignmentLength);
```

### 3. Pydantic model

In `server/src/civil3d_mcp/models.py`:

```python
class GetAlignmentLengthArgs(BaseModel):
    model_config = ConfigDict(extra="forbid")
    alignment: str = Field(description="Alignment name")
```

### 4. Python tool

In `server/src/civil3d_mcp/tools/alignments.py`:

```python
from ..models import GetAlignmentLengthArgs

@mcp.tool(annotations=READ_ANN, title="Get alignment length")
async def civil3d_get_alignment_length(args: GetAlignmentLengthArgs) -> dict:
    """Return total length of an alignment in drawing units."""
    return await call("civil3d_get_alignment_length", args.model_dump(exclude_none=True))
```

### 5. Rebuild + retest

```powershell
dotnet build -c Release plugin\
Copy-Item plugin\bin\Release\net8.0\*.dll `
    $env:APPDATA\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle\Contents\ -Force
# Restart Civil 3D, then restart Claude Desktop.
```

Python changes don't need a rebuild — `pip install -e server\` keeps
them live.

---

## Versioning

| Component | Version |
|-----------|---------|
| Python package | `0.2.0` (in `server/pyproject.toml` + `__init__.py`) |
| C# bridge | reports `0.2.0` from `/health` (in `BridgeServer.cs`) |
| MCP protocol | follows `mcp>=1.0` |

Bump both versions in lockstep so `/health` mismatch warnings remain
useful.

---

## Civil 3D API quirks to remember

* **Grades are decimal** (`0.05` = 5%) — no auto-conversion. K-value
  formula: `L = K × |G1 − G2| × 100`.
* **Alignment offsets**: `Alignment.PointLocation(station, offset, …)`
  treats **positive offset = LEFT** of direction of travel. The
  Pydantic descriptions match this; do not invert.
* **Parabolic vertical curves**: `AddFixedParabolaByLength(int e1, int e2, double length)`
  is stable from Civil 3D 2024 onward. Entity IDs are `int` because the
  underlying `VerticalEntityId` is. Don't switch to `long`.
* **Pipe networks**: `Network.Create(civDoc, ref name, partsListId)`.
  Add structures first, then add pipes referencing those structure IDs.
* **Layers auto-create**: `Helpers.EnsureLayer(tr, db, name, colorIndex)`
  before any draw so the bridge never throws "layer doesn't exist".
* **BlockReference + attributes**: append the `BlockReference` to model
  space, then walk the source `BlockTableRecord` for
  `AttributeDefinition` entries and append `AttributeReference`s for
  each — do this in the same transaction.
