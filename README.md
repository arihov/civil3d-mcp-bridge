<div align="center">

# 🛣️ Civil 3D MCP Bridge

### Talk to Civil 3D. Design faster. Check every curve.

**Drive Autodesk Civil 3D 2025, 2026 and 2027 in plain English through Claude or any MCP-compatible AI assistant.**

Built by **[AfroBIM Studios](https://afrobim.com)**, led by **Venture Kubariho**, a highway design engineer, from Kampala, Uganda 🇺🇬

[![Civil 3D](https://img.shields.io/badge/Civil%203D-2025%20%7C%202026%20%7C%202027-0696D7?logo=autodesk&logoColor=white)](#-install-in-5-minutes)
[![.NET](https://img.shields.io/badge/.NET-8%20%26%2010-512BD4?logo=dotnet&logoColor=white)](plugin/)
[![Python](https://img.shields.io/badge/Python-3.11%2B-3776AB?logo=python&logoColor=white)](server/)
[![MCP](https://img.shields.io/badge/Model%20Context%20Protocol-128%20tools-111111)](docs/TOOLS.md)
[![Stars](https://img.shields.io/github/stars/arihov/civil3d-mcp-bridge?style=social)](https://github.com/arihov/civil3d-mcp-bridge/stargazers)

**⭐ If this saves you even one hour of clicking, [star the repo](https://github.com/arihov/civil3d-mcp-bridge). It helps other civil engineers find it.**

</div>

---

## Why this exists

Civil engineers spend too many hours on repetitive Civil 3D work: placing hundreds of chainage markers, checking every curve radius against design speed, pulling cross-section data for quantities, and rebuilding the same surface again and again. The engineering judgement is ours. The clicking shouldn't be.

**Civil 3D MCP Bridge connects your live Civil 3D session to an AI assistant.** You describe what you want and it runs on your drawing through the official Civil 3D .NET API:

> *"Check alignment CL-01 against 80 km/h with 8% max superelevation and list any curve that fails."*
>
> *"Compute cut and fill between EG and FG along the main road every 20 m, with a 0.15 m topsoil strip, and save the table to CSV."*
>
> *"Create a Top corridor surface from the MKM corridor and give me its slope distribution."*
>
> *"Build an alignment through these five PIs with 300 m radii and 60 m spirals, then a profile from this PVI table."*
>
> *"Place a culvert at chainage 12+450 with a 900 mm pipe, 14 m long, skewed 15°."*

It was built by a working highway engineer for real road projects in East Africa, and it's free for every civil engineer to use.

---

## ✨ What you get

| | |
|---|---|
| 🧭 **128 tools** | Alignments, profiles, corridors, surfaces, earthworks, sections, feature lines, points, gravity and pressure pipes, styles, layers, blocks, markings, parcels and reports |
| ✅ **Design checks** | Minimum radius, crest and sag K values, maximum and minimum grade, broken-back curves and grade breaks without a vertical curve (AASHTO 2018 metric; the inputs follow your project criteria) |
| 🚜 **Earthworks** | Average-end-area cut and fill with mass-haul ordinates, read directly from two surfaces. No sample-line setup needed. Gives an independent check on QTO |
| 📐 **Engineer's calculators** | Stopping sight distance, K values and R<sub>min</sub>; COGO inverse and forward; curve solution; Bowditch traverse; superelevation runoff. These run without Civil 3D |
| 🧩 **Civil 3D 2025 + 2026 + 2027** | One installer; .NET 8 and .NET 10 builds in one bundle |
| 🪟 **Several sessions at once** | Run 2025 and 2027 side by side. The assistant finds each session and can target any open drawing |
| 🔒 **Local and safe** | Listens on `127.0.0.1` only. Your drawings never leave your machine through the bridge, and browser-originated requests are refused |
| 🛠️ **Escape hatch** | `civil3d_call` and `run_command` for anything not wrapped yet |

Full tool reference: **[docs/TOOLS.md](docs/TOOLS.md)** · What's new: **[CHANGELOG.md](CHANGELOG.md)**

---

## 🚀 Install in 5 minutes

**You need:** Windows, Civil 3D 2025/2026/2027, Python 3.11+ (`winget install Python.Python.3.12`) and [Claude Desktop](https://claude.ai/download) or another MCP client. Git is optional. Windows on ARM (for example under Parallels) works too.

Close Civil 3D, open **PowerShell**, and paste:

```powershell
$dst = "$env:USERPROFILE\civil3d-mcp-bridge"
Invoke-WebRequest https://github.com/arihov/civil3d-mcp-bridge/archive/refs/heads/main.zip -OutFile "$env:TEMP\c3dmcp.zip"
Expand-Archive "$env:TEMP\c3dmcp.zip" -DestinationPath $env:TEMP -Force
if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
Move-Item "$env:TEMP\civil3d-mcp-bridge-main" $dst
cd $dst; Get-ChildItem *.ps1 | Unblock-File
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Then:
1. Open Civil 3D. The command line should show `[Civil3DMcpBridge] Civil 3D 2027 listening on http://127.0.0.1:7827`.
2. Fully quit Claude Desktop (tray › Quit) and reopen it.
3. Ask: **"List my Civil 3D instances and run the bridge health check."**

The installer detects your Civil 3D versions, deploys the add-in (it compiles if a .NET 10 SDK is present, otherwise it uses the prebuilt bundle), creates a private Python environment and registers the bridge with Claude Desktop, backing up your config first. Details and troubleshooting: **[docs/INSTALL.md](docs/INSTALL.md)**.

---

## 🏗️ How it works

```
 Claude / any MCP client ──stdio──► Python MCP server ──HTTP 127.0.0.1──► Civil3DMcpBridge.dll (inside Civil 3D)
                                    • validation                          • runs on Civil 3D's main thread
                                    • session discovery                   • LockDocument + Transaction
                                    • design checks & calculators         • official Civil 3D .NET API
```

Each Civil 3D release gets its own port (2025 → 7825, 2026 → 7826, 2027 → 7827). Every write is a normal Civil 3D transaction, so **UNDO works as usual**. Architecture notes: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## ⚠️ Engineering responsibility

This is a productivity tool, not a substitute for engineering judgement. The design checks follow AASHTO *A Policy on Geometric Design of Highways and Streets* (2018, metric) and state their assumptions. Where your project's design manual (for example the Uganda MoWT Road Design Manual) differs, your project criteria govern. Always review results before issuing drawings or quantities.

---

## 🤝 Join in

- ⭐ **Star the repo** so more engineers can find it.
- 👀 **Watch → All activity** to get notified of new tools and releases. New features land regularly.
- 📣 **Share it** with your design team, your firm, your classmates and your WhatsApp engineering groups.
- 🐞 **Report bugs and request tools** in [Issues](https://github.com/arihov/civil3d-mcp-bridge/issues). Tell us what you wish Civil 3D could do in one sentence.
- 🔧 **Contribute.** Pull requests are welcome. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how to add a tool (one C# handler plus one Python wrapper).

---

## 🙏 Credits

Created and maintained by **[AfroBIM Studios](https://afrobim.com)**, a digital engineering studio, led by **Venture Kubariho** (Highway Design Engineer, PMP), Kampala, Uganda.

Ideas and Civil 3D 2027 build notes were informed by the open-source community, including [JhulVF/Civil3d-mcp-tcce](https://github.com/JhulVF/Civil3d-mcp-tcce), [Joshua8-AI/Civil3D-mcp](https://github.com/Joshua8-AI/Civil3D-mcp), [amirclyde/Civil3D-mcp](https://github.com/amirclyde/Civil3D-mcp) and [antonhofstader/Civil3D-mcp-python-COM](https://github.com/antonhofstader/Civil3D-mcp-python-COM). No code was copied; features were re-implemented for this architecture.

*Autodesk and Civil 3D are trademarks of Autodesk, Inc. This project is independent and not affiliated with or endorsed by Autodesk.*

<div align="center">

**Built in Africa, for civil engineers everywhere.**

[afrobim.com](https://afrobim.com) · ⭐ [Star](https://github.com/arihov/civil3d-mcp-bridge/stargazers) · 👀 [Watch](https://github.com/arihov/civil3d-mcp-bridge/subscription) · 🍴 [Fork](https://github.com/arihov/civil3d-mcp-bridge/fork)

</div>
