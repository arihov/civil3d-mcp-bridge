using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace Civil3DMcpBridge.Tools;

internal static class SurfaceTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_list_surfaces", ListSurfaces);
        ToolRegistry.Register("civil3d_get_surface_volume", GetSurfaceVolume);
        ToolRegistry.Register("civil3d_create_tin_surface", CreateTinSurface);
        ToolRegistry.Register("civil3d_add_points_to_surface", AddPointsToSurface);
        ToolRegistry.Register("civil3d_get_elevation_at_xy", GetElevationAtXy);
        ToolRegistry.Register("civil3d_sample_surface_along_alignment", SampleAlongAlignment);
    }

    // ---- Inspection --------------------------------------------------------

    private static object ListSurfaces(JsonElement args)
    {
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var results = new List<object>();
        foreach (ObjectId sid in civDoc.GetSurfaceIds())
        {
            var s = (Surface)tr.GetObject(sid, OpenMode.ForRead);
            var stats = s.GetGeneralProperties();
            results.Add(new
            {
                name = s.Name,
                description = s.Description,
                isVolumeSurface = s.IsVolumeSurface,
                minElevation = stats.MinimumElevation,
                maxElevation = stats.MaximumElevation,
                meanElevation = stats.MeanElevation,
                pointCount = stats.NumberOfPoints,
                handle = s.Handle.Value.ToString("x"),
            });
        }
        tr.Commit();
        return new { count = results.Count, surfaces = results };
    }

    private static object GetSurfaceVolume(JsonElement args)
    {
        var baseName = args.GetRequiredString("base");
        var compName = args.GetRequiredString("comparison");
        var keep = args.TryGetProperty("keep", out var k) && k.ValueKind == JsonValueKind.True;

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var baseId = Helpers.FindSurfaceId(tr, civDoc, baseName)
            ?? throw new ToolException($"surface '{baseName}' not found");
        var compId = Helpers.FindSurfaceId(tr, civDoc, compName)
            ?? throw new ToolException($"surface '{compName}' not found");

        var tempName = $"_mcp_vol_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        var volumeId = TinVolumeSurface.Create(tempName, baseId, compId);
        var volSurf = (TinVolumeSurface)tr.GetObject(volumeId, OpenMode.ForWrite);
        var props = volSurf.GetVolumeProperties();

        var result = new
        {
            baseSurface = baseName,
            comparisonSurface = compName,
            cutVolume = props.UnadjustedCutVolume,
            fillVolume = props.UnadjustedFillVolume,
            netVolume = props.UnadjustedNetVolume,
            netGraph = props.UnadjustedNetVolume >= 0 ? "fill" : "cut",
            tempSurfaceName = keep ? tempName : null,
            kept = keep,
        };
        if (!keep) volSurf.Erase();
        tr.Commit();
        return result;
    }

    // ---- Creation ----------------------------------------------------------

    /// <summary>
    /// Create an empty TIN surface. Use add_points_to_surface afterwards to
    /// populate it, or use civil3d_create_tin_from_dem with a DEM file.
    /// </summary>
    private static object CreateTinSurface(JsonElement args)
    {
        var name = args.GetRequiredString("name");
        var description = args.GetOptionalString("description") ?? "";
        var styleName = args.GetOptionalString("style") ?? "Standard";

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var styleId = ResolveSurfaceStyle(tr, civDoc, styleName);
        var sid = TinSurface.Create(name, styleId);

        var s = (TinSurface)tr.GetObject(sid, OpenMode.ForWrite);
        if (!string.IsNullOrEmpty(description)) s.Description = description;
        tr.Commit();
        return new { name, surfaceType = "TIN", handle = s.Handle.Value.ToString("x") };
    }

    /// <summary>
    /// Add an array of {x,y,z} points to a TIN surface as drawing objects
    /// then rebuild. Args:
    ///   { "surface": "string", "points": [{x,y,z}, ...] }
    /// </summary>
    private static object AddPointsToSurface(JsonElement args)
    {
        var name = args.GetRequiredString("surface");
        if (!args.TryGetProperty("points", out var pts) || pts.ValueKind != JsonValueKind.Array)
            throw new ToolException("'points' must be an array of {x, y, z}");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var sid = Helpers.FindSurfaceId(tr, civDoc, name)
            ?? throw new ToolException($"surface '{name}' not found");
        var s = (TinSurface)tr.GetObject(sid, OpenMode.ForWrite);

        var added = 0;
        var coords = new Point3dCollection();
        foreach (var p in pts.EnumerateArray())
        {
            coords.Add(new Point3d(
                p.GetRequiredDouble("x"),
                p.GetRequiredDouble("y"),
                p.GetRequiredDouble("z")));
            added++;
        }
        s.AddVertices(coords);
        s.Rebuild();
        tr.Commit();
        return new { surface = name, pointsAdded = added };
    }

    // ---- Sampling ----------------------------------------------------------

    private static object GetElevationAtXy(JsonElement args)
    {
        var name = args.GetRequiredString("surface");
        var x = args.GetRequiredDouble("x");
        var y = args.GetRequiredDouble("y");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var sid = Helpers.FindSurfaceId(tr, civDoc, name)
            ?? throw new ToolException($"surface '{name}' not found");
        var s = (Surface)tr.GetObject(sid, OpenMode.ForRead);

        double z;
        try { z = s.FindElevationAtXY(x, y); }
        catch (Exception ex)
        {
            throw new ToolException($"point ({x}, {y}) is outside the surface bounds: {ex.Message}");
        }
        tr.Commit();
        return new { surface = name, x, y, elevation = z };
    }

    /// <summary>
    /// Sample a surface along an alignment at a fixed interval — useful for
    /// quickly building drainage / hydrology long-sections without setting
    /// up a full profile view.
    /// </summary>
    private static object SampleAlongAlignment(JsonElement args)
    {
        var alignmentName = args.GetRequiredString("alignment");
        var surfaceName = args.GetRequiredString("surface");
        var interval = args.GetOptionalDouble("interval", 10.0);
        if (interval <= 0) throw new ToolException("'interval' must be positive");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = Helpers.RequireAlignment(tr, civDoc, alignmentName);
        var sid = Helpers.FindSurfaceId(tr, civDoc, surfaceName)
            ?? throw new ToolException($"surface '{surfaceName}' not found");
        var s = (Surface)tr.GetObject(sid, OpenMode.ForRead);

        var samples = new List<object>();
        var station = al.StartingStation;
        int outOfBounds = 0;
        while (station <= al.EndingStation + 1e-6)
        {
            var pt = Helpers.PointAtStation(al, station);
            double z;
            try { z = s.FindElevationAtXY(pt.X, pt.Y); }
            catch { z = double.NaN; outOfBounds++; }
            samples.Add(new { station, x = pt.X, y = pt.Y, elevation = double.IsNaN(z) ? (double?)null : z });
            station += interval;
        }
        tr.Commit();
        return new
        {
            alignment = alignmentName,
            surface = surfaceName,
            interval,
            count = samples.Count,
            outOfBounds,
            samples,
        };
    }

    private static ObjectId ResolveSurfaceStyle(Transaction tr, CivilDocument civDoc, string name)
    {
        foreach (ObjectId id in civDoc.Styles.SurfaceStyles)
        {
            var s = (Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle)tr.GetObject(id, OpenMode.ForRead);
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
        }
        if (civDoc.Styles.SurfaceStyles.Count > 0) return civDoc.Styles.SurfaceStyles[0];
        throw new ToolException($"no surface styles in drawing (looked for '{name}')");
    }
}
