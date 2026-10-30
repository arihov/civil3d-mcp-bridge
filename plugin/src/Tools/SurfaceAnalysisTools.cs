using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil;
using Autodesk.Civil.DatabaseServices;
using Surface = Autodesk.Civil.DatabaseServices.Surface;
using static Civil3DMcpBridge.Common;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace Civil3DMcpBridge.Tools;

/// <summary>Surface statistics, slope/elevation banding, transects, boundaries and contour extraction.</summary>
internal static class SurfaceAnalysisTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_get_surface_statistics", Statistics);
        ToolRegistry.Register("civil3d_analyze_surface_slopes", SlopeBands);
        ToolRegistry.Register("civil3d_sample_surface_transect", Transect);
        ToolRegistry.Register("civil3d_add_surface_boundary", AddBoundary);
        ToolRegistry.Register("civil3d_extract_surface_contours", ExtractContours);
        ToolRegistry.Register("civil3d_rebuild_surface", Rebuild);
    }

    private static object Statistics(JsonElement args)
    {
        var name = args.GetRequiredString("surface");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var s = RequireSurface<Surface>(tr, civ, name);
        var g = s.GetGeneralProperties();
        object? terrain = null;
        if (s is TinSurface tin)
        {
            try
            {
                var tp = tin.GetTerrainProperties();
                terrain = new
                {
                    area2d = R(tp.SurfaceArea2D, 2), area3d = R(tp.SurfaceArea3D, 2),
                    minGradePct = R(tp.MinimumGradeOrSlope * 100, 3), maxGradePct = R(tp.MaximumGradeOrSlope * 100, 3),
                    meanGradePct = R(tp.MeanGradeOrSlope * 100, 3),
                };
            }
            catch (Exception e) { terrain = new { error = e.Message }; }
        }
        var res = new
        {
            surface = s.Name, type = s.GetType().Name, isOutOfDate = s.IsOutOfDate,
            pointCount = g.NumberOfPoints,
            minElevation = R(g.MinimumElevation), maxElevation = R(g.MaximumElevation), meanElevation = R(g.MeanElevation),
            extents = new { minX = R(g.MinimumCoordinateX), minY = R(g.MinimumCoordinateY), maxX = R(g.MaximumCoordinateX), maxY = R(g.MaximumCoordinateY) },
            terrain,
        };
        tr.Commit();
        return res;
    }

    /// <summary>{ surface, bands_pct?:[0,2,5,10,15,25,50], elevation_interval? } - area-weighted from TIN triangles.</summary>
    private static object SlopeBands(JsonElement args)
    {
        var name = args.GetRequiredString("surface");
        var bounds = args.GetOptionalArray("bands_pct").Select(e => e.GetDouble()).ToList();
        if (bounds.Count == 0) bounds = new List<double> { 0, 2, 5, 10, 15, 25, 50 };
        bounds = bounds.Distinct().OrderBy(b => b).ToList();
        var elevInterval = args.GetOptionalDouble("elevation_interval", 0);

        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var tin = RequireSurface<TinSurface>(tr, civ, name);

        var slopeArea = new double[bounds.Count];
        var elevArea = new SortedDictionary<double, double>();
        double total = 0; long tris = 0;
        foreach (TinSurfaceTriangle t in tin.GetTriangles(false))
        {
            var a = t.Vertex1.Location; var b = t.Vertex2.Location; var c = t.Vertex3.Location;
            var u = b - a; var v = c - a;
            var n = u.CrossProduct(v);
            var area = Math.Abs(n.Z) / 2;
            if (area <= 0) continue;
            var slopePct = Math.Sqrt(n.X * n.X + n.Y * n.Y) / Math.Abs(n.Z) * 100;
            var idx = bounds.Count - 1;
            for (var i = 0; i < bounds.Count - 1; i++) if (slopePct < bounds[i + 1]) { idx = i; break; }
            slopeArea[idx] += area; total += area; tris++;
            if (elevInterval > 0)
            {
                var z = (a.Z + b.Z + c.Z) / 3;
                var key = Math.Floor(z / elevInterval) * elevInterval;
                elevArea[key] = elevArea.TryGetValue(key, out var x) ? x + area : area;
            }
        }
        tr.Commit();
        var slope = new List<object>();
        for (var i = 0; i < bounds.Count; i++)
            slope.Add(new
            {
                fromPct = bounds[i], toPct = i < bounds.Count - 1 ? bounds[i + 1] : (double?)null,
                area2d = R(slopeArea[i], 1), share = total > 0 ? R(slopeArea[i] / total * 100, 2) : 0,
            });
        return new
        {
            surface = name, triangles = tris, totalArea2d = R(total, 1),
            slopeBands = slope,
            elevationBands = elevInterval > 0 ? elevArea.Select(kv => new { from = R(kv.Key), to = R(kv.Key + elevInterval), area2d = R(kv.Value, 1) }) : null,
        };
    }

    /// <summary>{ surface, start:[x,y], end:[x,y], step=1 } or { surface, points:[[x,y],...] }.</summary>
    private static object Transect(JsonElement args)
    {
        var name = args.GetRequiredString("surface");
        var step = args.GetOptionalDouble("step", 1.0);
        if (step <= 0) throw new ToolException("'step' must be positive");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var s = RequireSurface<Surface>(tr, civ, name);
        var res = new List<object>();
        if (args.TryGetProperty("points", out _))
        {
            foreach (var p in ReadPoints(args, "points"))
                res.Add(new { x = R(p.X), y = R(p.Y), z = TryElevation(s, p.X, p.Y) is { } z ? R(z) : (double?)null });
        }
        else
        {
            var a = ReadPoints(args, "start").FirstOrDefault();
            var b = ReadPoints(args, "end").FirstOrDefault();
            var len = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
            var n = Math.Min(20000, (int)Math.Ceiling(len / step));
            for (var i = 0; i <= n; i++)
            {
                var t = n == 0 ? 0 : (double)i / n;
                var x = a.X + (b.X - a.X) * t; var y = a.Y + (b.Y - a.Y) * t;
                res.Add(new { distance = R(len * t), x = R(x), y = R(y), z = TryElevation(s, x, y) is { } z ? R(z) : (double?)null });
            }
        }
        tr.Commit();
        return new { surface = name, count = res.Count, samples = res };
    }

    /// <summary>{ surface, polyline_handle, type: outer|hide|show, mid_ordinate=0.1, non_destructive=true }</summary>
    private static object AddBoundary(JsonElement args)
    {
        var name = args.GetRequiredString("surface");
        var handle = args.GetRequiredString("polyline_handle");
        var typeStr = (args.GetOptionalString("type") ?? "outer").ToLowerInvariant();
        var type = typeStr switch
        {
            "outer" => SurfaceBoundaryType.Outer,
            "hide" => SurfaceBoundaryType.Hide,
            "show" => SurfaceBoundaryType.Show,
            "data_clip" or "dataclip" => SurfaceBoundaryType.DataClip,
            _ => throw new ToolException("type must be outer, hide, show or data_clip"),
        };
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var tin = RequireSurface<TinSurface>(tr, civ, name, OpenMode.ForWrite);
        var ids = new ObjectIdCollection { RequireHandle(doc.Database, handle) };
        tin.BoundariesDefinition.AddBoundaries(ids, args.GetOptionalDouble("mid_ordinate", 0.1), type, args.GetOptionalBool("non_destructive", true));
        tin.Rebuild();
        tr.Commit();
        return new { surface = name, boundaryType = type.ToString(), polylineHandle = handle, rebuilt = true };
    }

    /// <summary>{ surface, interval=1.0, layer? } - creates polylines at the given interval.</summary>
    private static object ExtractContours(JsonElement args)
    {
        var name = args.GetRequiredString("surface");
        var interval = args.GetOptionalDouble("interval", 1.0);
        if (interval <= 0) throw new ToolException("'interval' must be positive");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var s = RequireSurface<TinSurface>(tr, civ, name);
        var ids = s.ExtractContours(interval);
        var layer = args.GetOptionalString("layer");
        if (!string.IsNullOrWhiteSpace(layer))
        {
            var lid = EnsureLayer(tr, doc.Database, layer);
            foreach (ObjectId id in ids) ((Entity)tr.GetObject(id, OpenMode.ForWrite)).LayerId = lid;
        }
        tr.Commit();
        return new { surface = name, interval, created = ids.Count, layer };
    }

    private static object Rebuild(JsonElement args)
    {
        var name = args.GetRequiredString("surface");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var s = RequireSurface<Surface>(tr, civ, name, OpenMode.ForWrite);
        s.Rebuild();
        tr.Commit();
        return new { surface = name, rebuilt = true };
    }
}
