using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using static Civil3DMcpBridge.Common;

namespace Civil3DMcpBridge.Tools;

/// <summary>
/// Plain AutoCAD geometry: polylines, 3D polylines, text, entity lookup and
/// a polyline measurement schedule (lengths / areas by layer - handy for BOQ
/// take-off of kerbs, drains, pavement areas drawn as closed polylines).
/// </summary>
internal static class GeometryTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_create_polyline", CreatePolyline);
        ToolRegistry.Register("civil3d_create_3d_polyline", Create3dPolyline);
        ToolRegistry.Register("civil3d_create_text", CreateText);
        ToolRegistry.Register("civil3d_measure_polylines", MeasurePolylines);
        ToolRegistry.Register("civil3d_get_entity_info", EntityInfo);
        ToolRegistry.Register("civil3d_erase_entities", Erase);
    }

    private static object CreatePolyline(JsonElement args)
    {
        var pts = ReadPoints(args, "points").ToList();
        if (pts.Count < 2) throw new ToolException("'points' needs at least 2 vertices");
        var (doc, _) = DrawingContext.RequireActive();
        var db = doc.Database;
        using var docLock = doc.LockDocument();
        using var tr = db.TransactionManager.StartTransaction();
        var pl = new Polyline();
        for (var i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
        pl.Closed = args.GetOptionalBool("closed", false);
        pl.Elevation = args.GetOptionalDouble("elevation", 0);
        pl.LayerId = EnsureLayer(tr, db, args.GetOptionalString("layer"));
        ModelSpace(tr, db).AppendEntity(pl);
        tr.AddNewlyCreatedDBObject(pl, true);
        var res = new { handle = HandleOf(pl), vertices = pts.Count, length = R(pl.Length), area = pl.Closed ? R(pl.Area) : (double?)null, layer = pl.Layer };
        tr.Commit();
        return res;
    }

    private static object Create3dPolyline(JsonElement args)
    {
        var pts = new Point3dCollection();
        foreach (var p in ReadPoints(args, "points", requireZ: true)) pts.Add(p);
        if (pts.Count < 2) throw new ToolException("'points' needs at least 2 vertices");
        var (doc, _) = DrawingContext.RequireActive();
        var db = doc.Database;
        using var docLock = doc.LockDocument();
        using var tr = db.TransactionManager.StartTransaction();
        var pl = new Polyline3d(Poly3dType.SimplePoly, pts, args.GetOptionalBool("closed", false));
        pl.LayerId = EnsureLayer(tr, db, args.GetOptionalString("layer"));
        ModelSpace(tr, db).AppendEntity(pl);
        tr.AddNewlyCreatedDBObject(pl, true);
        var res = new { handle = HandleOf(pl), vertices = pts.Count, length = R(pl.Length), layer = pl.Layer };
        tr.Commit();
        return res;
    }

    /// <summary>{ text, x, y, height=2.5, rotation_deg=0, layer?, mtext=false, width? }</summary>
    private static object CreateText(JsonElement args)
    {
        var text = args.GetRequiredString("text");
        var pos = new Point3d(args.GetRequiredDouble("x"), args.GetRequiredDouble("y"), 0);
        var h = args.GetOptionalDouble("height", 2.5);
        var rot = args.GetOptionalDouble("rotation_deg", 0) * Math.PI / 180;
        var (doc, _) = DrawingContext.RequireActive();
        var db = doc.Database;
        using var docLock = doc.LockDocument();
        using var tr = db.TransactionManager.StartTransaction();
        Entity ent;
        if (args.GetOptionalBool("mtext", false))
            ent = new MText { Contents = text, Location = pos, TextHeight = h, Rotation = rot, Width = args.GetOptionalDouble("width", 0) };
        else
            ent = new DBText { TextString = text, Position = pos, Height = h, Rotation = rot };
        ent.LayerId = EnsureLayer(tr, db, args.GetOptionalString("layer"));
        ModelSpace(tr, db).AppendEntity(ent);
        tr.AddNewlyCreatedDBObject(ent, true);
        var res = new { handle = HandleOf(ent), type = ent.GetType().Name, layer = ent.Layer };
        tr.Commit();
        return res;
    }

    /// <summary>{ layers?:["C-KERB*"], closed_only=false, include_items=false } - wildcard layer filter.</summary>
    private static object MeasurePolylines(JsonElement args)
    {
        var filters = args.GetOptionalArray("layers").Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList();
        var closedOnly = args.GetOptionalBool("closed_only", false);
        var items = args.GetOptionalBool("include_items", false);
        bool Match(string layer) => filters.Count == 0 || filters.Any(f =>
            System.Text.RegularExpressions.Regex.IsMatch(layer, "^" + System.Text.RegularExpressions.Regex.Escape(f).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        var (doc, _) = DrawingContext.RequireActive();
        var db = doc.Database;
        using var docLock = doc.LockDocument();
        using var tr = db.TransactionManager.StartTransaction();
        var byLayer = new SortedDictionary<string, (int n, double len, double area, int closed)>(StringComparer.OrdinalIgnoreCase);
        var list = new List<object>();
        foreach (ObjectId id in ModelSpace(tr, db, OpenMode.ForRead))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Curve c) continue;
            if (c is not (Polyline or Polyline2d or Polyline3d)) continue;
            if (!Match(c.Layer)) continue;
            var closed = c.Closed;
            if (closedOnly && !closed) continue;
            double len = 0, area = 0;
            try { len = c.GetDistanceAtParameter(c.EndParam); } catch { }
            if (closed) try { area = c.Area; } catch { }
            (int n, double len, double area, int closed) cur = byLayer.TryGetValue(c.Layer, out var v) ? v : (0, 0.0, 0.0, 0);
            byLayer[c.Layer] = (cur.n + 1, cur.len + len, cur.area + area, cur.closed + (closed ? 1 : 0));
            if (items && list.Count < 2000) list.Add(new { handle = HandleOf(c), layer = c.Layer, type = c.GetType().Name, closed, length = R(len), area = closed ? R(area) : (double?)null });
        }
        tr.Commit();
        return new
        {
            layerFilter = filters,
            summary = byLayer.Select(kv => new { layer = kv.Key, count = kv.Value.n, closedCount = kv.Value.closed, totalLength = R(kv.Value.len, 2), totalArea = R(kv.Value.area, 2) }),
            totals = new { count = byLayer.Values.Sum(v => v.n), length = R(byLayer.Values.Sum(v => v.len), 2), area = R(byLayer.Values.Sum(v => v.area), 2) },
            items = items ? list : null,
            note = "Polyline2d/3d lengths are 3D-true; LWPolyline lengths are plan length. Areas are plan areas of closed curves.",
        };
    }

    private static object EntityInfo(JsonElement args)
    {
        var h = args.GetRequiredString("handle");
        var (doc, _) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var o = tr.GetObject(RequireHandle(doc.Database, h), OpenMode.ForRead);
        object? ext = null;
        string? layer = null;
        if (o is Entity e)
        {
            layer = e.Layer;
            try { var b = e.GeometricExtents; ext = new { minX = R(b.MinPoint.X), minY = R(b.MinPoint.Y), maxX = R(b.MaxPoint.X), maxY = R(b.MaxPoint.Y), minZ = R(b.MinPoint.Z), maxZ = R(b.MaxPoint.Z) }; } catch { }
        }
        string? name = null;
        try { name = o.GetType().GetProperty("Name")?.GetValue(o) as string; } catch { }
        double? length = null, area = null;
        if (o is Curve c)
        {
            try { length = R(c.GetDistanceAtParameter(c.EndParam)); } catch { }
            if (c.Closed) try { area = R(c.Area); } catch { }
        }
        tr.Commit();
        return new { handle = h, type = o.GetType().FullName, dxfClass = o.GetRXClass().DxfName, name, layer, extents = ext, length, area };
    }

    /// <summary>{ handles:[...] } - erases plain AutoCAD or Civil 3D objects. Undo with civil3d_undo.</summary>
    private static object Erase(JsonElement args)
    {
        var handles = args.GetRequiredArray("handles").Select(e => e.GetString() ?? "").ToList();
        if (handles.Count > 5000) throw new ToolException("max 5000 handles per call");
        var (doc, _) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var erased = new List<string>();
        var failed = new List<object>();
        foreach (var h in handles)
        {
            try { tr.GetObject(RequireHandle(doc.Database, h), OpenMode.ForWrite).Erase(); erased.Add(h); }
            catch (Exception ex) { failed.Add(new { handle = h, error = ex.Message }); }
        }
        tr.Commit();
        return new { erased = erased.Count, failed };
    }
}
