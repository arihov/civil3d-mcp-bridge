using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

namespace Civil3DMcpBridge.Tools;

internal static class PointTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_list_points", ListPoints);
        ToolRegistry.Register("civil3d_create_point", CreatePoint);
        ToolRegistry.Register("civil3d_import_points_csv", ImportPointsCsv);
        ToolRegistry.Register("civil3d_export_points_csv", ExportPointsCsv);
        ToolRegistry.Register("civil3d_list_point_groups", ListPointGroups);
        ToolRegistry.Register("civil3d_create_point_group", CreatePointGroup);
    }

    private static object ListPoints(JsonElement args)
    {
        var limit = Math.Max(1, Math.Min(args.GetOptionalInt("limit", 100), 1000));
        var offset = Math.Max(0, args.GetOptionalInt("offset", 0));

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var total = civDoc.CogoPoints.Count;
        var results = new List<object>();
        int index = 0;
        foreach (var pointId in civDoc.CogoPoints)
        {
            if (index < offset) { index++; continue; }
            if (results.Count >= limit) break;
            var p = (CogoPoint)tr.GetObject(pointId, OpenMode.ForRead);
            results.Add(new
            {
                pointNumber = p.PointNumber,
                northing = p.Northing,
                easting = p.Easting,
                elevation = p.Elevation,
                rawDescription = p.RawDescription,
                fullDescription = p.FullDescription,
                pointName = p.PointName,
            });
            index++;
        }
        tr.Commit();
        return new
        {
            total,
            offset,
            count = results.Count,
            hasMore = offset + results.Count < total,
            nextOffset = offset + results.Count < total ? offset + results.Count : (int?)null,
            points = results,
        };
    }

    private static object CreatePoint(JsonElement args)
    {
        var northing = args.GetRequiredDouble("northing");
        var easting = args.GetRequiredDouble("easting");
        var elevation = args.GetRequiredDouble("elevation");
        var description = args.GetOptionalString("description") ?? "";
        var pointNumber = args.GetOptionalInt("point_number", 0);

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var loc = new Point3d(easting, northing, elevation);
        var newId = civDoc.CogoPoints.Add(loc, pointNumber == 0);
        var pt = (CogoPoint)tr.GetObject(newId, OpenMode.ForWrite);
        if (pointNumber > 0) pt.PointNumber = (uint)pointNumber;
        if (!string.IsNullOrEmpty(description)) pt.RawDescription = description;
        var resultNumber = pt.PointNumber;
        tr.Commit();

        return new { pointNumber = resultNumber, northing, easting, elevation, description };
    }

    /// <summary>
    /// Batch-import COGO points from a CSV file. Default column order is
    /// PNEZD (point number, northing, easting, elevation, description) which
    /// matches most Ugandan total-station exports; override with the
    /// "format" arg to pick another standard layout, or supply
    /// "column_map" for arbitrary column positions.
    ///
    /// Args:
    /// {
    ///   "path": "string (absolute or relative to drawing)",
    ///   "format": "PNEZD"|"PENZD"|"NEZD"|"ENZD" (default PNEZD),
    ///   "column_map": { "point_number": int, "northing": int, "easting": int,
    ///                   "elevation": int, "description": int } (overrides format),
    ///   "has_header": bool (default true),
    ///   "delimiter": "string (default ',')"
    /// }
    /// </summary>
    private static object ImportPointsCsv(JsonElement args)
    {
        var path = args.GetRequiredString("path");
        var hasHeader = !args.TryGetProperty("has_header", out var h) || h.ValueKind != JsonValueKind.False;
        var delimiter = (args.GetOptionalString("delimiter") ?? ",").FirstOrDefault();
        if (delimiter == '\0') delimiter = ',';

        var format = (args.GetOptionalString("format") ?? "PNEZD").ToUpperInvariant();
        var map = ResolveColumnMap(format, args);

        var (doc, civDoc) = DrawingContext.RequireActive();
        var fullPath = Helpers.ResolvePath(doc, path);
        if (!File.Exists(fullPath))
            throw new ToolException($"file not found: {fullPath}");

        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var imported = 0;
        var errors = new List<object>();
        var lines = File.ReadAllLines(fullPath);
        for (int i = 0; i < lines.Length; i++)
        {
            if (hasHeader && i == 0) continue;
            var raw = lines[i].Trim();
            if (string.IsNullOrEmpty(raw)) continue;

            var parts = raw.Split(delimiter).Select(s => s.Trim()).ToArray();
            try
            {
                int? pn = null;
                if (map.PointNumber >= 0 && map.PointNumber < parts.Length &&
                    int.TryParse(parts[map.PointNumber], NumberStyles.Any, CultureInfo.InvariantCulture, out var pNum))
                    pn = pNum;

                var n = double.Parse(parts[map.Northing], NumberStyles.Float, CultureInfo.InvariantCulture);
                var e = double.Parse(parts[map.Easting], NumberStyles.Float, CultureInfo.InvariantCulture);
                var z = double.Parse(parts[map.Elevation], NumberStyles.Float, CultureInfo.InvariantCulture);
                var d = map.Description >= 0 && map.Description < parts.Length ? parts[map.Description] : "";

                var loc = new Point3d(e, n, z);
                var newId = civDoc.CogoPoints.Add(loc, !pn.HasValue);
                var pt = (CogoPoint)tr.GetObject(newId, OpenMode.ForWrite);
                if (pn.HasValue) pt.PointNumber = (uint)pn.Value;
                if (!string.IsNullOrEmpty(d)) pt.RawDescription = d;
                imported++;
            }
            catch (Exception ex)
            {
                errors.Add(new { line = i + 1, content = raw, error = ex.Message });
                if (errors.Count >= 20) // cap so we don't blow up the response
                {
                    errors.Add(new { line = -1, content = "...", error = "(further errors truncated)" });
                    break;
                }
            }
        }
        tr.Commit();
        return new
        {
            path = fullPath,
            format,
            imported,
            errorCount = errors.Count,
            errors,
        };
    }

    /// <summary>Export all (or a subset by point group) of COGO points to CSV.</summary>
    private static object ExportPointsCsv(JsonElement args)
    {
        var path = args.GetRequiredString("path");
        var groupName = args.GetOptionalString("group");

        var (doc, civDoc) = DrawingContext.RequireActive();
        var fullPath = Helpers.ResolvePath(doc, path);

        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        IEnumerable<ObjectId> pointIds;
        if (!string.IsNullOrEmpty(groupName))
        {
            var groupId = FindPointGroupId(tr, civDoc, groupName)
                ?? throw new ToolException($"point group '{groupName}' not found");
            var group = (PointGroup)tr.GetObject(groupId, OpenMode.ForRead);
            pointIds = group.GetPointNumbers()
                .Select(num => civDoc.CogoPoints.GetPointByPointNumber(num))
                .ToList();
        }
        else
        {
            pointIds = civDoc.CogoPoints.Cast<ObjectId>().ToList();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var w = new StreamWriter(fullPath, false, new UTF8Encoding(false));
        w.WriteLine("PointNumber,Northing,Easting,Elevation,Description");
        int exported = 0;
        foreach (var id in pointIds)
        {
            var p = (CogoPoint)tr.GetObject(id, OpenMode.ForRead);
            w.WriteLine(string.Join(",", new[]
            {
                p.PointNumber.ToString(CultureInfo.InvariantCulture),
                p.Northing.ToString("F4", CultureInfo.InvariantCulture),
                p.Easting.ToString("F4", CultureInfo.InvariantCulture),
                p.Elevation.ToString("F3", CultureInfo.InvariantCulture),
                EscapeCsv(p.RawDescription ?? ""),
            }));
            exported++;
        }
        tr.Commit();
        return new { path = fullPath, exported, group = groupName };
    }

    private static object ListPointGroups(JsonElement args)
    {
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var results = new List<object>();
        foreach (ObjectId id in civDoc.PointGroups)
        {
            var g = (PointGroup)tr.GetObject(id, OpenMode.ForRead);
            results.Add(new
            {
                name = g.Name,
                description = g.Description,
                pointCount = g.PointsCount,
            });
        }
        tr.Commit();
        return new { count = results.Count, pointGroups = results };
    }

    private static object CreatePointGroup(JsonElement args)
    {
        var name = args.GetRequiredString("name");
        var description = args.GetOptionalString("description") ?? "";
        var includeNumbers = args.GetOptionalString("include_point_numbers"); // e.g. "1-100,200,300-350"
        var includeDescriptions = args.GetOptionalString("include_raw_descriptions"); // e.g. "IP*,BM*"

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var groupId = civDoc.PointGroups.Add(name);
        var group = (PointGroup)tr.GetObject(groupId, OpenMode.ForWrite);
        group.Description = description;

        var query = group.GetQuery() as StandardPointGroupQuery;
        if (query != null)
        {
            if (!string.IsNullOrEmpty(includeNumbers))
                query.IncludeNumbers = includeNumbers;
            if (!string.IsNullOrEmpty(includeDescriptions))
                query.IncludeRawDescriptions = includeDescriptions;
            group.SetQuery(query);
        }

        tr.Commit();
        return new
        {
            name = group.Name,
            description = group.Description,
            pointCount = group.PointsCount,
        };
    }

    // ---- internals ---------------------------------------------------------

    private struct ColumnMap
    {
        public int PointNumber, Northing, Easting, Elevation, Description;
    }

    private static ColumnMap ResolveColumnMap(string format, JsonElement args)
    {
        if (args.TryGetProperty("column_map", out var cm) && cm.ValueKind == JsonValueKind.Object)
        {
            return new ColumnMap
            {
                PointNumber = cm.GetOptionalInt("point_number", -1),
                Northing = cm.GetOptionalInt("northing", -1),
                Easting = cm.GetOptionalInt("easting", -1),
                Elevation = cm.GetOptionalInt("elevation", -1),
                Description = cm.GetOptionalInt("description", -1),
            };
        }

        return format switch
        {
            "PNEZD" => new ColumnMap { PointNumber = 0, Northing = 1, Easting = 2, Elevation = 3, Description = 4 },
            "PENZD" => new ColumnMap { PointNumber = 0, Easting = 1, Northing = 2, Elevation = 3, Description = 4 },
            "NEZD"  => new ColumnMap { PointNumber = -1, Northing = 0, Easting = 1, Elevation = 2, Description = 3 },
            "ENZD"  => new ColumnMap { PointNumber = -1, Easting = 0, Northing = 1, Elevation = 2, Description = 3 },
            _ => throw new ToolException($"unknown format '{format}' (use PNEZD|PENZD|NEZD|ENZD or supply column_map)"),
        };
    }

    private static ObjectId? FindPointGroupId(Transaction tr, CivilDocument civDoc, string name)
    {
        foreach (ObjectId id in civDoc.PointGroups)
        {
            var g = (PointGroup)tr.GetObject(id, OpenMode.ForRead);
            if (string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
        }
        return null;
    }

    private static string EscapeCsv(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
