# Installation

## Prerequisites

* **Windows 10 / 11**.
* **Civil 3D 2025 or 2026**, with at least one valid licence — the
  bundle is `<Application Description="AutoCAD*">` so loads under both.
* **.NET 8 SDK** — <https://dotnet.microsoft.com/download/dotnet/8.0>.
  `dotnet --version` should print `8.0.x` or newer.
* **Python 3.11+** — `python --version` should print `3.11.x` or newer.
* **Claude Desktop** — current build, with developer mode enabled if
  you want to inspect tool calls.

---

## One-shot install

From the repo root, in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Useful flags:

| Flag | Meaning |
|------|---------|
| `-Civil3DPath "C:\Program Files\Autodesk\AutoCAD 2025"` | Use a different Civil 3D install |
| `-PerMachine` | Install bundle to `C:\ProgramData\Autodesk\ApplicationPlugins\` instead of `%APPDATA%` (needs an elevated shell) |
| `-SkipBuild` | Skip `dotnet build` (use a pre-built DLL) |
| `-SkipPython` | Skip pip install + Claude config (plugin-only deploy) |
| `-PythonExe "C:\Python311\python.exe"` | Use a specific Python interpreter |

The script prints a step-by-step trace. If any step fails it stops and
explains what to fix — re-run after fixing.

---

## What `install.ps1` does, in order

### 1. Verify prerequisites

* Looks for `acad.exe` under `-Civil3DPath`.
* Runs `dotnet --version` and parses major version `>= 8`.
* Runs `<PythonExe> --version` and parses major.minor `>= 3.11`.

### 2. Build the plugin

```
dotnet build -c Release plugin\Civil3DMcpBridge.csproj
```

Output goes to `plugin\bin\Release\net8.0\`.

### 3. Deploy the bundle

Creates (or refreshes) the folder:

```
%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle\
├── PackageContents.xml
└── Contents\
    ├── Civil3DMcpBridge.dll
    └── (any referenced runtime DLLs)
```

`%APPDATA%` resolves to `C:\Users\<you>\AppData\Roaming`. Civil 3D's
ApplicationPlugins auto-loader picks it up on next launch — no
NETLOAD command needed.

### 4. Install the Python server

```
pip install -e server\
```

`-e` means editable: changes to `.py` files take effect on the next
`civil3d-mcp` start without reinstalling. The console scripts
`civil3d-mcp` and `civil3d-mcp-mock` land on PATH.

### 5. Merge Claude Desktop config

Adds (or updates) this entry inside
`%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "civil3d": {
      "command": "civil3d-mcp"
    }
  }
}
```

If you already have other MCP servers configured, the installer merges
without clobbering them.

### 6. Smoke test

The installer **doesn't** call Civil 3D — that has to be running for the
bridge to respond. Manual finish:

1. Open Civil 3D, open any DWG.
2. At the command line type `MCPSTATUS`. You should see
   `[civil3d-mcp] listening on http://127.0.0.1:7800 (66 tools)`.
3. From any shell: `curl http://127.0.0.1:7800/health` should print
   `{"ok":true,"version":"0.2.0",...}`.
4. Restart Claude Desktop. The hammer icon → `civil3d` → 66 tools.

---

## Manual install (fallback)

If PowerShell can't run the script:

```bat
:: 1. Build
cd plugin
dotnet build -c Release

:: 2. Copy bundle
set DEST=%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle
mkdir "%DEST%\Contents"
copy PackageContents.xml "%DEST%\"
copy bin\Release\net8.0\Civil3DMcpBridge.dll "%DEST%\Contents\"

:: 3. Python
cd ..\server
pip install -e .

:: 4. Edit %APPDATA%\Claude\claude_desktop_config.json yourself.
```

---

## Uninstall

```powershell
Remove-Item -Recurse `
    "$env:APPDATA\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle"
pip uninstall civil3d-mcp
# Remove the "civil3d" entry from claude_desktop_config.json manually.
```

---

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `MCPSTATUS: Unknown command` after opening Civil 3D | Bundle didn't load. Open `%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle\Contents\` and confirm the DLL exists. Check `PackageContents.xml` is at bundle root, not inside `Contents\`. |
| `curl http://127.0.0.1:7800/health` → connection refused | Civil 3D not running, or another process is holding port 7800. Run `netstat -ano | findstr 7800` to find it. |
| Claude Desktop doesn't show the `civil3d` server | `civil3d-mcp` not on PATH — open a new shell after pip install. Otherwise, check `claude_desktop_config.json` is valid JSON. |
| `BridgeError: HTTP 400 ...` from a tool | Validation error — the message names the field. Civil 3D didn't run the operation. |
| `BridgeError: HTTP 500 ...` from a tool | Unexpected exception inside Civil 3D. Look at the Civil 3D command line for `[civil3d-mcp]` lines with the stack trace. |
| Slow first call after Civil 3D start | First-call JIT + bundle load — usually <2 s. Subsequent calls sub-100 ms. |

---

## Security note

* The bridge listens **only on `127.0.0.1:7800`**, not on any external
  interface.
* There is no auth. Don't run this on a shared workstation. If you must,
  add a token check in `BridgeServer.cs` and patch `bridge.py` to send it.
