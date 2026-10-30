using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using static Civil3DMcpBridge.Common;

namespace Civil3DMcpBridge.Tools;

/// <summary>Corridor codes, corridor surfaces (Top / Datum) and extents boundaries.</summary>
internal static class CorridorSurfaceTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_get_corridor_codes", Codes);
        ToolRegistry.Register("civil3d_list_corridor_surfaces", ListSurfaces);
        ToolRegistry.Register("civil3d_create_corridor_surface", CreateSurface);
        ToolRegistry.Register("civil3d_get_corridor_regions", Regions);
    }

    private static Corridor RequireCorridor(Transaction tr, CivilDocument civ, string name, OpenMode mode = OpenMode.ForRead)
    {
        var id = Helpers.FindCorridorId(tr, civ, name) ?? throw new ToolException($"corridor '{name}' not found");
        return (Corridor)tr.GetObject(id, mode);
    }

    private static object Codes(JsonElement args)
    {
        var name = args.GetRequiredString("corridor");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var c = RequireCorridor(tr, civ, name);
        var res = new
        {
            corridor = c.Name,
            linkCodes = c.GetLinkCodes().OrderBy(x => x).ToArray(),
            pointCodes = c.GetPointCodes().OrderBy(x => x).ToArray(),
            shapeCodes = c.GetShapeCodes().OrderBy(x => x).ToArray(),
        };
        tr.Commit();
        return res;
    }

    private static object ListSurfaces(JsonElement args)
    {
        var name = args.GetRequiredString("corridor");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var c = RequireCorridor(tr, civ, name);
        var list = new List<object>();
        foreach (CorridorSurface cs in c.CorridorSurfaces)
            list.Add(new
            {
                name = cs.Name, linkCodes = cs.LinkCodes(), featureLineCodes = cs.FeatureLineCodes(),
                boundaryCount = cs.Boundaries.Count, overhangCorrection = cs.OverhangCorrection.ToString(),
                surfaceHandle = cs.SurfaceId.IsNull ? null : cs.SurfaceId.Handle.Value.ToString("x"),
            });
        tr.Commit();
        return new { corridor = name, count = list.Count, surfaces = list };
    }

    /// <summary>
    /// { corridor, name, link_codes:["Top"] | ["Datum"], feature_line_codes?:[], overhang:"none|top|bottom", add_extents_boundary=true, rebuild=true }
    /// </summary>
    private static object CreateSurface(JsonElement args)
    {
        var cName = args.GetRequiredString("corridor");
        var sName = args.GetRequiredString("name");
        var links = args.GetOptionalArray("link_codes").Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList();
        if (links.Count == 0) links.Add("Top");
        var fls = args.GetOptionalArray("feature_line_codes").Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList();
        var overhang = (args.GetOptionalString("overhang") ?? "none").ToLowerInvariant();

        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var c = RequireCorridor(tr, civ, cName, OpenMode.ForWrite);
        foreach (CorridorSurface existing in c.CorridorSurfaces)
            if (string.Equals(existing.Name, sName, StringComparison.OrdinalIgnoreCase))
                throw new ToolException($"corridor surface '{sName}' already exists");

        var known = c.GetLinkCodes().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = links.Where(l => !known.Contains(l)).ToList();
        if (missing.Count > 0) throw new ToolException($"link codes not in corridor: {string.Join(", ", missing)}. Available: {string.Join(", ", known)}");

        var cs = c.CorridorSurfaces.Add(sName);
        foreach (var l in links) cs.AddLinkCode(l, true);
        foreach (var f in fls) cs.AddFeatureLineCode(f);
        cs.OverhangCorrection = overhang switch
        {
            "top" => OverhangCorrectionType.TopLinks,
            "bottom" => OverhangCorrectionType.BottomLinks,
            _ => OverhangCorrectionType.None,
        };
        if (args.GetOptionalBool("add_extents_boundary", true))
            cs.Boundaries.AddCorridorExtentsBoundary($"{sName} extents");
        if (args.GetOptionalBool("rebuild", true)) c.Rebuild();
        tr.Commit();
        return new { corridor = cName, surface = sName, linkCodes = links, featureLineCodes = fls, overhang, rebuilt = args.GetOptionalBool("rebuild", true) };
    }

    private static object Regions(JsonElement args)
    {
        var name = args.GetRequiredString("corridor");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var c = RequireCorridor(tr, civ, name);
        var baselines = new List<object>();
        foreach (Baseline b in c.Baselines)
        {
            var regions = new List<object>();
            foreach (BaselineRegion r in b.BaselineRegions)
            {
                string? asm = null;
                try { asm = ((Assembly)tr.GetObject(r.AssemblyId, OpenMode.ForRead)).Name; } catch { }
                double[] freq = Array.Empty<double>();
                try { freq = r.SortedStations(); } catch { }
                regions.Add(new { name = r.Name, startStation = R(r.StartStation), endStation = R(r.EndStation), assembly = asm, appliedStationCount = freq.Length });
            }
            string? alName = null, pName = null;
            try { alName = ((Alignment)tr.GetObject(b.AlignmentId, OpenMode.ForRead)).Name; } catch { }
            try { pName = ((Profile)tr.GetObject(b.ProfileId, OpenMode.ForRead)).Name; } catch { }
            baselines.Add(new { name = b.Name, alignment = alName, profile = pName, startStation = R(b.StartStation), endStation = R(b.EndStation), regions });
        }
        tr.Commit();
        return new { corridor = name, isOutOfDate = c.IsOutOfDate, baselines };
    }
}
