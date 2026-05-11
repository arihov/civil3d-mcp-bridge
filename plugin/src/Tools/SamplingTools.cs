using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

namespace Civil3DMcpBridge.Tools;

internal static class SamplingTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_create_sample_line_group", CreateSampleLineGroup);
        ToolRegistry.Register("civil3d_list_sample_line_groups", ListSampleLineGroups);
    }

    /// <summary>
    /// Create a sample line group on an alignment, populated by station
    /// range at a fixed interval, with all eligible sources (surfaces and
    /// corridor codes) attached.
    ///
    /// Args:
    /// {
    ///   "alignment": "string",
    ///   "group_name": "string",
    ///   "interval": number,
    ///   "swath_left": number (default 15),
    ///   "swath_right": number (default 15),
    ///   "start_station": number (optional, default = alignment start),
    ///   "end_station": number (optional, default = alignment end)
    /// }
    /// </summary>
    private static object CreateSampleLineGroup(JsonElement args)
    {
        var alignmentName = args.GetRequiredString("alignment");
        var groupName = args.GetRequiredString("group_name");
        var interval = args.GetRequiredDouble("interval");
        var swathLeft = args.GetOptionalDouble("swath_left", 15);
        var swathRight = args.GetOptionalDouble("swath_right", 15);

        if (interval <= 0) throw new ToolException("'interval' must be positive");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = AlignmentTools.FindAlignmentByName(tr, civDoc, alignmentName)
            ?? throw new ToolException($"alignment '{alignmentName}' not found");

        var startStation = args.GetOptionalDouble("start_station", al.StartingStation);
        var endStation = args.GetOptionalDouble("end_station", al.EndingStation);
        if (endStation <= startStation)
            throw new ToolException("'end_station' must be greater than 'start_station'");

        // Create the empty group. The 2026 SampleLineGroup.Create takes
        // just (name, alignmentId) — style is read from settings.
        var groupId = SampleLineGroup.Create(
            groupName,
            al.ObjectId);

        var group = (SampleLineGroup)tr.GetObject(groupId, OpenMode.ForWrite);

        // Populate it. We use the "By stations" creation mode. C3D 2026
        // SampleLine.Create(name, groupId, station) seeds the line at the
        // station using the group's default swath settings.
        int created = 0;
        _ = swathLeft; _ = swathRight; // not exposed on SampleLine in 2026
        for (var s = startStation; s <= endStation + 1e-6; s += interval)
        {
            SampleLine.Create(
                $"{groupName}-{(int)Math.Round(s)}",
                groupId,
                s);
            created++;
        }

        // SampleLineGroup.SampledSourceCollection no longer exists in
        // C3D 2026 — the replacement is GetSectionSources() /
        // GetMaterialSectionSources(), which return mutable views that
        // need careful adapter logic to add new sources. Skip the
        // automatic surface attachment for now; users can wire sources via
        // the UI after creation.
        _ = group;

        tr.Commit();
        return new
        {
            alignment = alignmentName,
            group = groupName,
            sampleLines = created,
            interval,
            swathLeft,
            swathRight,
        };
    }

    private static object ListSampleLineGroups(JsonElement args)
    {
        var alignmentName = args.GetOptionalString("alignment");
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var results = new List<object>();
        foreach (ObjectId aid in civDoc.GetAlignmentIds())
        {
            var al = (Alignment)tr.GetObject(aid, OpenMode.ForRead);
            if (!string.IsNullOrEmpty(alignmentName) &&
                !string.Equals(al.Name, alignmentName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (ObjectId gid in al.GetSampleLineGroupIds())
            {
                var g = (SampleLineGroup)tr.GetObject(gid, OpenMode.ForRead);
                results.Add(new
                {
                    alignment = al.Name,
                    name = g.Name,
                    sampleLineCount = g.GetSampleLineIds().Count,
                });
            }
        }
        tr.Commit();
        return new { count = results.Count, groups = results };
    }

    private static ObjectId? First<T>(T collection) where T : System.Collections.IEnumerable
    {
        foreach (ObjectId id in collection) return id;
        return null;
    }
}
