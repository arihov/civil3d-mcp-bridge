using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(Civil3DMcpBridge.Plugin))]
[assembly: CommandClass(typeof(Civil3DMcpBridge.Plugin))]

namespace Civil3DMcpBridge;

public sealed class Plugin : IExtensionApplication
{
    internal const int DefaultPort = 7800;
    private static BridgeServer? _server;
    private static MainThreadDispatcher? _dispatcher;

    /// <summary>True when the bridge HTTP listener is running.</summary>
    internal static bool IsBridgeRunning => _server != null;

    /// <summary>URL prefix the bridge is listening on, or empty when stopped.</summary>
    internal static string BridgeUrl => _server?.UrlPrefix ?? "";

    public void Initialize()
    {
        // Register tools. Order doesn't matter; just every family must register.
        Tools.AlignmentTools.Register();
        Tools.AlignmentEditTools.Register();
        Tools.ProfileTools.Register();
        Tools.ProfileEditTools.Register();
        Tools.CorridorTools.Register();
        Tools.SamplingTools.Register();
        Tools.SurfaceTools.Register();
        Tools.SurfaceEditTools.Register();
        Tools.PointTools.Register();
        Tools.PipeNetworkTools.Register();
        Tools.BlockTools.Register();
        Tools.MarkingTools.Register();
        Tools.MassHaulTools.Register();
        Tools.DrawingTools.Register();
        Tools.JunctionTools.Register();
        Tools.RoundaboutTools.Register();
        Tools.TrackingTools.Register();

        try { StartBridge(DefaultPort); }
        catch (System.Exception ex)
        {
            WriteLine($"[Civil3DMcpBridge] auto-start failed: {ex.Message}");
            WriteLine("[Civil3DMcpBridge] run MCPSTART once Civil 3D is fully loaded.");
        }

        // Defer ribbon registration until AutoCAD's ribbon control exists.
        try { UI.McpRibbon.RegisterDeferred(); }
        catch (System.Exception ex)
        {
            WriteLine($"[Civil3DMcpBridge] ribbon registration deferred: {ex.Message}");
        }
    }

    public void Terminate()
    {
        UI.McpRibbon.Unregister();
        UI.McpPalette.Dispose();
        StopBridge();
    }

    [CommandMethod("MCPSTART", CommandFlags.Modal)]
    public void StartCommand() => StartBridge(DefaultPort);

    [CommandMethod("MCPSTOP", CommandFlags.Modal)]
    public void StopCommand() => StopBridge();

    [CommandMethod("MCPRESTART", CommandFlags.Modal)]
    public void RestartCommand()
    {
        StopBridge();
        StartBridge(DefaultPort);
    }

    [CommandMethod("MCPSTATUS", CommandFlags.Modal)]
    public void StatusCommand()
    {
        if (_server is null) { WriteLine("[Civil3DMcpBridge] bridge is not running"); return; }
        WriteLine($"[Civil3DMcpBridge] bridge listening on {_server.UrlPrefix}");
        WriteLine($"[Civil3DMcpBridge] registered tools: {ToolRegistry.Count}");
        WriteLine($"[Civil3DMcpBridge] total calls: {InvocationLog.TotalCalls} ({InvocationLog.TotalErrors} errors)");
    }

    [CommandMethod("MCPLIST", CommandFlags.Modal)]
    public void ListToolsCommand()
    {
        WriteLine($"[Civil3DMcpBridge] {ToolRegistry.Count} tools registered:");
        foreach (var name in ToolRegistry.Names) WriteLine($"  - {name}");
    }

    [CommandMethod("MCPSHOW", CommandFlags.Modal)]
    public void ShowPaletteCommand() => UI.McpPalette.Toggle();

    [CommandMethod("MCPCOPYTOOLS", CommandFlags.Modal)]
    public void CopyToolsCommand()
    {
        try
        {
            var text = string.Join(Environment.NewLine, ToolRegistry.Names);
            System.Windows.Forms.Clipboard.SetText(text);
            WriteLine($"[Civil3DMcpBridge] copied {ToolRegistry.Count} tool names to clipboard");
        }
        catch (System.Exception ex)
        {
            WriteLine($"[Civil3DMcpBridge] clipboard copy failed: {ex.Message}");
        }
    }

    [CommandMethod("MCPMANUAL", CommandFlags.Modal)]
    public void OpenManualCommand()
    {
        // Look beside the bundled DLL first (Contents\USER_MANUAL.pdf) so
        // shipping the bundle drops the manual right next to the plugin.
        var asmDir = System.IO.Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        var local = System.IO.Path.Combine(asmDir, "USER_MANUAL.pdf");
        var candidates = new[]
        {
            local,
            System.IO.Path.Combine(asmDir, "..", "USER_MANUAL.pdf"),
            @"C:\Users\dtmengineering\desktop\civil3d-mcp\docs\USER_MANUAL.pdf",
        };
        foreach (var p in candidates)
        {
            try
            {
                var full = System.IO.Path.GetFullPath(p);
                if (System.IO.File.Exists(full))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(full) { UseShellExecute = true });
                    return;
                }
            }
            catch { /* try next */ }
        }
        WriteLine("[Civil3DMcpBridge] USER_MANUAL.pdf not found beside plugin or in repo docs/.");
    }

    private static void StartBridge(int port)
    {
        if (_server is not null)
        {
            WriteLine($"[Civil3DMcpBridge] already running on {_server.UrlPrefix}");
            return;
        }
        _dispatcher = new MainThreadDispatcher();
        _server = new BridgeServer(port, _dispatcher);
        _server.Start();
        WriteLine($"[Civil3DMcpBridge] listening on {_server.UrlPrefix} ({ToolRegistry.Count} tools)");
    }

    private static void StopBridge()
    {
        _server?.Stop();
        _server = null;
        _dispatcher?.Dispose();
        _dispatcher = null;
        WriteLine("[Civil3DMcpBridge] stopped");
    }

    private static void WriteLine(string msg)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        doc?.Editor.WriteMessage($"\n{msg}");
    }
}
