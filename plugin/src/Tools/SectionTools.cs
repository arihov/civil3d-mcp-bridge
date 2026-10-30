using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using static Civil3DMcpBridge.Common;
using Section = Autodesk.Civil.DatabaseServices.Section;

namespace Civil3DMcpBridge.Tools;

/// <summary>Sample lines, sampled sections and section views.</summary>
internal static class SectionTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_list_sample_lines", ListSampleLines);
        ToolRegistry.Register("civil3d_export_sampled_sections", ExportSections);
        ToolRegistry.Register("civil3d_create_section_views", CreateSectionViews);
    }

    internal static SampleLineGroup RequireGroup(Transaction tr, Alignment al, string group)
    {
        foreach (ObjectId gid in al.GetSampleLineGroupIds())
        {
            var g = (SampleLineGroup)tr.GetObject(gid, OpenMode.ForRead);
            if (string.Equals(g.Name, group, StringComparison.OrdinalIgnoreCase)) return g;
        }
        throw new ToolException($"sample line group '{group}' not found on '{al.Name}'");
    }

    private static IEnumerable<SampleLine> Lines(Transaction tr, SampleLineGroup g)
        => g.GetSampleLineIds().Cast<ObjectId>().Select(id => (SampleLine)tr.GetObject(id, OpenMode.ForRead)).OrderBy(s => s.Station);

    private static object ListSampleLines(JsonElement args)
    {
        var alName = args.GetRequiredString("alignment");
        var group = args.GetRequiredString("group");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var al = Helpers.RequireAlignment(tr, civ, alName);
        var g = RequireGroup(tr, al, group);
        var list = new List<object>();
        foreach (var sl in Lines(tr, g))
        {
            double minO = double.MaxValue, maxO = double.MinValue;
            foreach (SampleLineVertex v in sl.Vertices)
            {
                double st = 0, off = 0;
                al.StationOffset(v.Location.X, v.Location.Y, ref st, ref off);
                minO = Math.Min(minO, off); maxO = Math.Max(maxO, off);
            }
            list.Add(new { name = sl.Name, station = R(sl.Station), number = sl.Number, leftOffset = R(minO), rightOffset = R(maxO), sectionCount = sl.GetSectionIds().Count, handle = HandleOf(sl) });
        }
        tr.Commit();
        return new { alignment = alName, group = g.Name, count = list.Count, sampleLines = list };
    }

    /// <summary>
    /// Offset/elevation points of every sampled section (EG, FG, corridor surfaces...).
    /// { alignment, group, sources?:[names], csv_path? } - CSV columns: station, source, offset, elevation.
    /// </summary>
    private static object ExportSections(JsonElement args)
    {
        var alName = args.GetRequiredString("alignment");
        var group = args.GetRequiredString("group");
        var wanted = args.GetOptionalArray("sources").Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var al = Helpers.RequireAlignment(tr, civ, alName);
        var g = RequireGroup(tr, al, group);

        var rows = new List<object?[]>();
        var summary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sl in Lines(tr, g))
        {
            foreach (ObjectId sid in sl.GetSectionIds())
            {
                var sec = (Section)tr.GetObject(sid, OpenMode.ForRead);
                var src = sec.Name;
                if (wanted.Count > 0 && !wanted.Any(w => src.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
                summary[src] = summary.TryGetValue(src, out var c) ? c + 1 : 1;
                foreach (SectionPoint p in sec.SectionPoints)
                    rows.Add(new object?[] { R(sl.Station), src, R(p.Location.X), R(p.Location.Y) });
            }
        }
        tr.Commit();
        string? written = null;
        var csv = args.GetOptionalString("csv_path");
        if (!string.IsNullOrWhiteSpace(csv))
        {
            written = Helpers.ResolvePath(doc, csv);
            WriteText(written, Csv(new[] { "station", "source", "offset", "elevation" }, rows));
        }
        return new
        {
            alignment = alName, group = g.Name, pointCount = rows.Count,
            sources = summary.Select(kv => new { section = kv.Key, sampleLines = kv.Value }),
            csvPath = written,
            preview = written is null ? rows.Take(400).Select(r => new { station = r[0], source = r[1], offset = r[2], elevation = r[3] }) : null,
            truncated = written is null && rows.Count > 400,
        };
    }

    /// <summary>
    /// One section view per sample line laid out in a grid.
    /// { alignment, group, origin:[x,y], columns=5, spacing_x=60, spacing_y=40, start_station?, end_station?, max_views=200 }
    /// </summary>
    private static object CreateSectionViews(JsonElement args)
    {
        var alName = args.GetRequiredString("alignment");
        var group = args.GetRequiredString("group");
        var origin = ReadPoints(args, "origin").FirstOrDefault();
        var cols = Math.Max(1, args.GetOptionalInt("columns", 5));
        var dx = args.GetOptionalDouble("spacing_x", 60);
        var dy = args.GetOptionalDouble("spacing_y", 40);
        var maxViews = Math.Clamp(args.GetOptionalInt("max_views", 200), 1, 2000);
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var al = Helpers.RequireAlignment(tr, civ, alName);
        var g = RequireGroup(tr, al, group);
        var s0 = args.GetOptionalDouble("start_station", double.MinValue);
        var s1 = args.GetOptionalDouble("end_station", double.MaxValue);

        var created = new List<object>();
        var i = 0;
        foreach (var sl in Lines(tr, g))
        {
            if (sl.Station < s0 - 1e-6 || sl.Station > s1 + 1e-6) continue;
            if (created.Count >= maxViews) break;
            var loc = new Point3d(origin.X + (i % cols) * dx, origin.Y - (i / cols) * dy, 0);
            var name = $"{g.Name} {sl.Station:0+000.00}";
            var id = SectionView.Create(name, sl.ObjectId, loc);
            created.Add(new { station = R(sl.Station), name, handle = HandleOf(tr.GetObject(id, OpenMode.ForRead)), x = R(loc.X), y = R(loc.Y) });
            i++;
        }
        tr.Commit();
        return new { alignment = alName, group = g.Name, created = created.Count, views = created };
    }
}
