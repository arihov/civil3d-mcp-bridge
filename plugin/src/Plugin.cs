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

        try { StartBridge(DefaultPort); }
        catch (System.Exception ex)
        {
            WriteLine($"[Civil3DMcpBridge] auto-start failed: {ex.Message}");
            WriteLine("[Civil3DMcpBridge] run MCPSTART once Civil 3D is fully loaded.");
        }
    }

    public void Terminate() => StopBridge();

    [CommandMethod("MCPSTART", CommandFlags.Modal)]
    public void StartCommand() => StartBridge(DefaultPort);

    [CommandMethod("MCPSTOP", CommandFlags.Modal)]
    public void StopCommand() => StopBridge();

    [CommandMethod("MCPSTATUS", CommandFlags.Modal)]
    public void StatusCommand()
    {
        if (_server is null) { WriteLine("[Civil3DMcpBridge] bridge is not running"); return; }
        WriteLine($"[Civil3DMcpBridge] bridge listening on {_server.UrlPrefix}");
        WriteLine($"[Civil3DMcpBridge] registered tools: {ToolRegistry.Count}");
    }

    [CommandMethod("MCPLIST", CommandFlags.Modal)]
    public void ListToolsCommand()
    {
        WriteLine($"[Civil3DMcpBridge] {ToolRegistry.Count} tools registered:");
        foreach (var name in ToolRegistry.Names) WriteLine($"  - {name}");
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
