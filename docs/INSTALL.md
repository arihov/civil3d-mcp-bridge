# Installing civil3d-mcp 0.4.0

## Requirements

| Item | Needed for | Notes |
|---|---|---|
| Civil 3D 2025, 2026 and/or 2027 | the add-in | x64 build; also runs under Windows on ARM x64 emulation |
| .NET 10 SDK | compiling (optional) | `winget install Microsoft.DotNet.SDK.10`. Builds the .NET 8 (2025/2026) and .NET 10 (2027) DLLs. Without it the installer deploys `dist\Civil3DMcpBridge-0.4.0.bundle.zip`. Visual Studio is not required. |
| Python 3.11-3.14 | the MCP server | Installed automatically (3.12) if missing. On Windows on ARM an **x64** Python is used, because `cryptography` (needed by `mcp`) ships no win_arm64 wheels. Python 3.15+ is skipped (no prebuilt wheels yet). |
| Claude Desktop | the MCP client | any MCP client works with `civil3d-mcp` over stdio |

## One-shot install

Close all Civil 3D windows, then in the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Flags: `-Releases 2027` (limit releases), `-Prebuilt` (skip compiling),
`-Net10For2026` (only if your 2026 has the August-2026 .NET 10 update and you
want a .NET 10 build), `-SkipPython` (add-in only), `-PythonExe <path>`.

What it writes:

| Path | Content |
|---|---|
| `%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle\` | `PackageContents.xml` + `Contents\<year>\Civil3DMcpBridge.dll` |
| `%LOCALAPPDATA%\Civil3DMcpBridge\venv\` | private Python environment with `civil3d-mcp.exe` |
| `%LOCALAPPDATA%\Civil3DMcpBridge\instances\` | one JSON per running Civil 3D session (written by the add-in) |
| Claude config: `%LOCALAPPDATA%\Claude-Data\<id>\claude_desktop_config.json` (current app), plus the Store and classic `%APPDATA%\Claude` copies if present | `mcpServers.civil3d` entry (timestamped backup alongside; written UTF-8 without BOM) |

## Verify

1. Start Civil 3D, open a drawing. Command line:
   `[Civil3DMcpBridge] Civil 3D 2027 listening on http://127.0.0.1:7827 (110 tools)`.
2. `MCPSTATUS` prints release, AutoCAD version, .NET runtime and call counts.
3. Browser or PowerShell: `irm http://127.0.0.1:7827/health` (a browser will be
   refused with 403 by design; use PowerShell or curl).
4. Restart Claude Desktop fully (tray > Quit). Ask: *"list Civil 3D instances"*.

## Several Civil 3D sessions

| Release | Default port |
|---|---|
| 2025 | 7825 |
| 2026 | 7826 |
| 2027 | 7827 |
| second session of the same release | first free port in 7840-7859 |

The MCP server picks the newest live release automatically. To steer it:
`civil3d_select_instance` (`release`, `pid`, `port` or `url`), the env var
`CIVIL3D_RELEASE=2025`, or a hard pin `CIVIL3D_BRIDGE_URL=http://127.0.0.1:7825` in the
Claude config. `CIVIL3D_BRIDGE_PORT` set before starting Civil 3D overrides the add-in port.

## Manual install (no script)

```powershell
dotnet build plugin\Civil3DMcpBridge.csproj -c Release -p:C3DVersion=2027
# copy plugin\bin\Release\2027\Civil3DMcpBridge.* to
#   %APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle\Contents\2027\
# copy plugin\PackageContents.xml to the .bundle root (delete blocks for releases you did not build)
py -3 -m venv %LOCALAPPDATA%\Civil3DMcpBridge\venv
%LOCALAPPDATA%\Civil3DMcpBridge\venv\Scripts\pip install -e server
```

Claude config entry:

```json
"civil3d": { "command": "C:\\Users\\<you>\\AppData\\Local\\Civil3DMcpBridge\\venv\\Scripts\\civil3d-mcp.exe",
             "env": { "CIVIL3D_TIMEOUT_S": "300" } }
```

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Nothing printed at Civil 3D start | Bundle not found or wrong series. Check `PackageContents.xml` has a block for your release; run `APPAUTOLOADER` / restart. Check file is not blocked (installer runs `Unblock-File`). |
| `FileLoadException` / `BadImageFormat` in 2027 | A .NET 8 DLL was loaded into 2027. Redeploy so `Contents\2027` holds the net10.0 build. |
| "preferred port busy" | Another session or app holds the port; the bridge used 7840+. Discovery handles it. |
| Calls time out with "busy" | A command, dialog or grip edit is active. Press Esc in Civil 3D and retry. |
| Copy fails during install | Civil 3D still running (DLL locked). Close all windows. |
| `Building wheel for cryptography ... link.exe not found` | Python too new or ARM64 build. Rerun `install.ps1`; it now picks or installs x64 Python 3.12 and rebuilds the venv. |
| `ModuleNotFoundError: mcp.server.fastmcp` | An `mcp` 2.x package was installed outside the venv. Use the venv executable; the package pins `mcp<2`. |
| 2026 build errors after the Aug-2026 update | Its assemblies now need .NET 10 to compile: `install.ps1 -Net10For2026`. |

Uninstall: `powershell -ExecutionPolicy Bypass -File .\uninstall.ps1`.
