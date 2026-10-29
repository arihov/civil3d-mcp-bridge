using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Surface = Autodesk.Civil.DatabaseServices.Surface;
using static Civil3DMcpBridge.Common;

namespace Civil3DMcpBridge.Tools;

/// <summary>Feature lines: list, create from polyline/3D points, drape on surface, export vertices.</summary>
internal static class FeatureLineTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_list_feature_lines", List);
        ToolRegistry.Register("civil3d_create_feature_line", Create);
        ToolRegistry.Register("civil3d_feature_line_elevations_from_surface", FromSurface);
        ToolRegistry.Register("civil3d_get_feature_line_points", GetPoints);
    }

    private static IEnumerable<FeatureLine> All(Transaction tr, CivilDocument civ, Database db)
    {
        var seen = new HashSet<ObjectId>();
        foreach (ObjectId sid in civ.GetSiteIds())
        {
            var site = (Site)tr.GetObject(sid, OpenMode.ForRead);
            foreach (ObjectId fid in site.GetFeatureLineIds())
                if (seen.Add(fid)) yield return (FeatureLine)tr.GetObject(fid, OpenMode.ForRead);
        }
        // Site-less feature lines live directly in model space.
        var ms = ModelSpace(tr, db, OpenMode.ForRead);
        foreach (ObjectId id in ms)
            if (id.ObjectClass.Name == "AeccDbFeatureLine" && seen.Add(id))
                yield return (FeatureLine)tr.GetObject(id, OpenMode.ForRead);
    }

    private static FeatureLine Require(Transaction tr, CivilDocument civ, Database db, JsonElement args)
    {
        var handle = args.GetOptionalString("handle");
        if (!string.IsNullOrWhiteSpace(handle))
            return tr.GetObject(RequireHandle(db, handle), OpenMode.ForRead) as FeatureLine
                   ?? throw new ToolException($"handle {handle} is not a feature line");
        var name = args.GetRequiredString("name");
        foreach (var f in All(tr, civ, db))
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
        throw new ToolException($"feature line '{name}' not found");
    }

    private static object List(JsonElement args)
    {
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var list = new List<object>();
        foreach (var f in All(tr, civ, doc.Database))
            list.Add(new
            {
                name = f.Name, handle = HandleOf(f), site = f.SiteId.IsNull ? null : ((Site)tr.GetObject(f.SiteId, OpenMode.ForRead)).Name,
                length2d = R(f.Length2D), length3d = R(f.Length3D),
                minElevation = R(f.MinElevation), maxElevation = R(f.MaxElevation),
                piCount = f.PIPointsCount, elevationPointCount = f.ElevationPointsCount,
                layer = f.Layer,
            });
        tr.Commit();
        return new { count = list.Count, featureLines = list };
    }

    /// <summary>{ name?, polyline_handle? | points:[[x,y,z],...], site?, layer? }</summary>
    private static object Create(JsonElement args)
    {
        var name = args.GetOptionalString("name") ?? "";
        var (doc, civ) = DrawingContext.RequireActive();
        var db = doc.Database;
        using var docLock = doc.LockDocument();
        using var tr = db.TransactionManager.StartTransaction();

        var siteId = ObjectId.Null;
        var siteName = args.GetOptionalString("site");
        if (!string.IsNullOrWhiteSpace(siteName))
        {
            foreach (ObjectId sid in civ.GetSiteIds())
                if (string.Equals(((Site)tr.GetObject(sid, OpenMode.ForRead)).Name, siteName, StringComparison.OrdinalIgnoreCase)) siteId = sid;
            if (siteId.IsNull) siteId = Site.Create(civ, siteName);
        }

        ObjectId sourceId;
        var erase = false;
        var ph = args.GetOptionalString("polyline_handle");
        if (!string.IsNullOrWhiteSpace(ph)) sourceId = RequireHandle(db, ph);
        else
        {
            var pts = new Point3dCollection();
            foreach (var p in ReadPoints(args, "points")) pts.Add(p);
            if (pts.Count < 2) throw new ToolException("'points' needs at least 2 vertices");
            var ms = ModelSpace(tr, db);
            var poly = new Polyline3d(Poly3dType.SimplePoly, pts, false);
            sourceId = ms.AppendEntity(poly);
            tr.AddNewlyCreatedDBObject(poly, true);
            erase = true;
        }

        var flId = FeatureLine.Create(name, sourceId, siteId);
        var fl = (FeatureLine)tr.GetObject(flId, OpenMode.ForWrite);
        var layer = args.GetOptionalString("layer");
        if (!string.IsNullOrWhiteSpace(layer)) fl.LayerId = EnsureLayer(tr, db, layer);
        if (erase) tr.GetObject(sourceId, OpenMode.ForWrite).Erase();
        var res = new { name = fl.Name, handle = HandleOf(fl), length3d = R(fl.Length3D), minElevation = R(fl.MinElevation), maxElevation = R(fl.MaxElevation) };
        tr.Commit();
        return res;
    }

    private static object FromSurface(JsonElement args)
    {
        var surfName = args.GetRequiredString("surface");
        var addBreaks = args.GetOptionalBool("insert_grade_breaks", true);
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var fl = Require(tr, civ, doc.Database, args);
        var s = RequireSurface<Surface>(tr, civ, surfName);
        fl.UpgradeOpen();
        fl.AssignElevationsFromSurface(s.ObjectId, addBreaks);
        var res = new { featureLine = fl.Name, surface = s.Name, minElevation = R(fl.MinElevation), maxElevation = R(fl.MaxElevation), length3d = R(fl.Length3D) };
        tr.Commit();
        return res;
    }

    private static object GetPoints(JsonElement args)
    {
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var fl = Require(tr, civ, doc.Database, args);
        var pts = fl.GetPoints(FeatureLinePointType.AllPoints);
        var list = new List<object>();
        var chain = 0.0;
        Point3d? prev = null;
        foreach (Point3d p in pts)
        {
            if (prev is { } q) chain += Math.Sqrt(Math.Pow(p.X - q.X, 2) + Math.Pow(p.Y - q.Y, 2));
            list.Add(new { chainage = R(chain), x = R(p.X), y = R(p.Y), z = R(p.Z) });
            prev = p;
        }
        tr.Commit();
        var csv = args.GetOptionalString("csv_path");
        string? written = null;
        if (!string.IsNullOrWhiteSpace(csv))
        {
            written = Helpers.ResolvePath(doc, csv);
            var rows = new List<object?[]>();
            foreach (Point3d p in pts) rows.Add(new object?[] { R(p.X), R(p.Y), R(p.Z) });
            WriteText(written, Csv(new[] { "x", "y", "z" }, rows));
        }
        return new { featureLine = fl.Name, count = list.Count, csvPath = written, points = list };
    }
}
