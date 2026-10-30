using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using static Civil3DMcpBridge.Common;

namespace Civil3DMcpBridge.Tools;

/// <summary>Pressure networks (water mains): inventory and pipe schedule.</summary>
internal static class PressureNetworkTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_list_pressure_networks", List);
        ToolRegistry.Register("civil3d_get_pressure_network_schedule", Schedule);
    }

    private static object List(JsonElement args)
    {
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var list = new List<object>();
        foreach (ObjectId id in Autodesk.Civil.ApplicationServices.CivilDocumentPressurePipesExtension.GetPressurePipeNetworkIds(civ))
        {
            var n = (PressurePipeNetwork)tr.GetObject(id, OpenMode.ForRead);
            list.Add(new
            {
                name = n.Name, handle = HandleOf(n),
                pipeCount = n.GetPipeIds().Count, fittingCount = n.GetFittingIds().Count, appurtenanceCount = n.GetAppurtenanceIds().Count,
            });
        }
        tr.Commit();
        return new { count = list.Count, networks = list };
    }

    private static object Schedule(JsonElement args)
    {
        var name = args.GetRequiredString("network");
        var csv = args.GetOptionalString("csv_path");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        PressurePipeNetwork? net = null;
        foreach (ObjectId id in Autodesk.Civil.ApplicationServices.CivilDocumentPressurePipesExtension.GetPressurePipeNetworkIds(civ))
        {
            var n = (PressurePipeNetwork)tr.GetObject(id, OpenMode.ForRead);
            if (string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)) net = n;
        }
        if (net is null) throw new ToolException($"pressure network '{name}' not found");
        var pipes = new List<(string name, string part, double dn, double len3d)>();
        foreach (ObjectId pid in net.GetPipeIds())
        {
            var p = (PressurePipe)tr.GetObject(pid, OpenMode.ForRead);
            double dn = 0;
            try { dn = p.NominalDiameter; } catch { }
            pipes.Add((p.Name, p.PartDescription, dn, p.Length3DCenterToCenter));
        }
        tr.Commit();
        var bySize = pipes.GroupBy(p => (p.part, p.dn)).Select(g => new { part = g.Key.part, nominalDiameter = R(g.Key.dn, 4), count = g.Count(), totalLength = R(g.Sum(x => x.len3d), 2) }).OrderBy(x => x.nominalDiameter).ToList();
        string? written = null;
        if (!string.IsNullOrWhiteSpace(csv))
        {
            written = Helpers.ResolvePath(doc, csv);
            WriteText(written, Csv(new[] { "pipe", "part", "nominal_diameter", "length_3d" }, pipes.Select(p => new object?[] { p.name, p.part, R(p.dn, 4), R(p.len3d, 3) })));
        }
        return new { network = name, pipeCount = pipes.Count, totalLength = R(pipes.Sum(p => p.len3d), 2), bySize, csvPath = written };
    }
}
