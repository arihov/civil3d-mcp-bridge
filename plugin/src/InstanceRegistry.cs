using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3DMcpBridge;

/// <summary>
/// Lets several Civil 3D sessions (e.g. 2025 and 2027 side by side) each run
/// their own bridge without fighting over one port.
///
///  * Port: CIVIL3D_BRIDGE_PORT env var if set, else 7800 + (release % 100),
///    i.e. 2025 -> 7825, 2026 -> 7826, 2027 -> 7827. If that port is taken
///    (a second session of the same release) the next free port in
///    7840-7859 is used.
///  * Discovery: every running bridge writes
///    %LOCALAPPDATA%\Civil3DMcpBridge\instances\&lt;pid&gt;.json, which the
///    Python MCP server reads to list and select sessions.
/// </summary>
internal static class InstanceRegistry
{
    public const string BridgeVersion = "0.4.0";

    private static string? _file;

    public static string InstancesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Civil3DMcpBridge", "instances");

    /// <summary>Product year (2025, 2026, 2027 ...) from the running host.</summary>
    public static int ReleaseYear
    {
        get
        {
            try
            {
                var v = AcadApp.Version; // R25.0 = 2025, R25.1 = 2026, R26.0 = 2027
                var map = new Dictionary<(int, int), int>
                {
                    { (24, 1), 2022 }, { (24, 2), 2023 }, { (24, 3), 2024 },
                    { (25, 0), 2025 }, { (25, 1), 2026 },
                    { (26, 0), 2027 }, { (26, 1), 2028 },
                };
                if (map.TryGetValue((v.Major, v.Minor), out var year)) return year;
            }
            catch { /* fall through to compile-time default */ }
#if C3D2025
            return 2025;
#elif C3D2026
            return 2026;
#else
            return 2027;
#endif
        }
    }

    public static string AcadVersion
    {
        get { try { return AcadApp.Version.ToString(); } catch { return "unknown"; } }
    }

    public static int PreferredPort
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("CIVIL3D_BRIDGE_PORT");
            if (int.TryParse(env, out var p) && p is > 1024 and < 65535) return p;
            return 7800 + ReleaseYear % 100;
        }
    }

    public static bool IsPortFree(int port)
    {
        try
        {
            var l = new TcpListener(IPAddress.Loopback, port);
            l.Start();
            l.Stop();
            return true;
        }
        catch (SocketException) { return false; }
    }

    /// <summary>Preferred port first, then the overflow range.</summary>
    public static IEnumerable<int> CandidatePorts()
    {
        yield return PreferredPort;
        for (var p = 7840; p < 7860; p++) yield return p;
    }

    public static void Write(int port)
    {
        try
        {
            Directory.CreateDirectory(InstancesDir);
            var pid = Environment.ProcessId;
            _file = Path.Combine(InstancesDir, $"{pid}.json");
            var payload = new
            {
                pid,
                port,
                url = $"http://127.0.0.1:{port}",
                release = ReleaseYear,
                acadVersion = AcadVersion,
                bridgeVersion = BridgeVersion,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                startedUtc = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(_file, JsonSerializer.Serialize(payload, Json.Options));
        }
        catch { /* discovery is best-effort */ }
    }

    public static void Remove()
    {
        try { if (_file != null && File.Exists(_file)) File.Delete(_file); }
        catch { }
        _file = null;
    }
}
