using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;

namespace Civil3DMcpBridge.Tools;

internal static class SurfaceEditTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_create_surface_from_points", CreateFromPoints);
        ToolRegistry.Register("civil3d_create_persistent_volume_surface", CreatePersistentVolume);
        ToolRegistry.Register("civil3d_add_breakline_from_polyline", AddBreaklineFromPolyline);
    }

    /// <summary>
    /// Create an empty TIN surface and immediately add COGO points from
    /// a named point group as the only data source. Good for quickly
    /// generating an existing-ground surface from a field survey.
    ///
    /// Args:
    /// {
    ///   "name": "string",
    ///   "description": "string" (optional),
    ///   "point_group": "string" (optional, uses all CogoPoints if omitted)
    /// }
    /// </summary>
    private static object CreateFromPoints(JsonElement args)
    {
        var name = args.GetRequiredString("name");
        var description = args.GetOptionalString("description") ?? "";
        var pointGroupName = args.GetOptionalString("point_group");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        // Resolve a TIN surface style.
        ObjectId styleId = ObjectId.Null;
        foreach (ObjectId sid in civDoc.Styles.SurfaceStyles)
        {
            styleId = sid;
            break;
        }
        if (styleId.IsNull) throw new ToolException("no surface styles in this drawing");

        var surfaceId = TinSurface.Create(name, styleId);
        var surface = (TinSurface)tr.GetObject(surfaceId, OpenMode.ForWrite);
        surface.Description = description;

        if (string.IsNullOrEmpty(pointGroupName))
        {
            // Use all CogoPoints as data source by adding each point's
            // location as a TIN vertex. (C3D 2026 dropped the AddCgPoints
            // overload that took an ObjectId collection.)
            int added = 0;
            foreach (var pid in civDoc.CogoPoints)
            {
                var cp = (CogoPoint)tr.GetObject(pid, OpenMode.ForRead);
                surface.AddVertex(cp.Location);
                added++;
            }
            if (added == 0)
                throw new ToolException("no COGO points in drawing to build a surface from");
        }
        else
        {
            ObjectId groupId = ObjectId.Null;
            foreach (ObjectId gid in civDoc.PointGroups)
            {
                var pg = (PointGroup)tr.GetObject(gid, OpenMode.ForRead);
                if (string.Equals(pg.Name, pointGroupName, StringComparison.OrdinalIgnoreCase))
                {
                    groupId = gid;
                    break;
                }
            }
            if (groupId.IsNull) throw new ToolException($"point group '{pointGroupName}' not found");
            surface.PointGroupsDefinition.AddPointGroup(groupId);
        }

        surface.Rebuild();
        var stats = surface.GetGeneralProperties();
        var result = new
        {
            name = surface.Name,
            pointCount = stats.NumberOfPoints,
            minElevation = stats.MinimumElevation,
            maxElevation = stats.MaximumElevation,
        };
        tr.Commit();
        return result;
    }

    /// <summary>
    /// Create a persistent (not-temp) volume surface for ongoing tracking
    /// of cut/fill as either underlying surface changes.
    /// Args: { "name": "string", "base": "string", "comparison": "string" }
    /// </summary>
    private static object CreatePersistentVolume(JsonElement args)
    {
        var name = args.GetRequiredString("name");
        var baseName = args.GetRequiredString("base");
        var compName = args.GetRequiredString("comparison");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var baseId = Helpers.FindSurfaceId(tr, civDoc, baseName)
            ?? throw new ToolException($"surface '{baseName}' not found");
        var compId = Helpers.FindSurfaceId(tr, civDoc, compName)
            ?? throw new ToolException($"surface '{compName}' not found");

        var id = TinVolumeSurface.Create(name, baseId, compId);
        var volSurface = (TinVolumeSurface)tr.GetObject(id, OpenMode.ForRead);
        var props = volSurface.GetVolumeProperties();
        var result = new
        {
            name,
            baseSurface = baseName,
            comparisonSurface = compName,
            cutVolume = props.UnadjustedCutVolume,
            fillVolume = props.UnadjustedFillVolume,
            netVolume = props.UnadjustedNetVolume,
            handle = volSurface.Handle.Value.ToString("x"),
        };
        tr.Commit();
        return result;
    }

    /// <summary>
    /// Add an existing polyline as a breakline to a TIN surface.
    /// Args: { "surface": "string", "polyline_handle": "string", "description": "string" }
    /// </summary>
    private static object AddBreaklineFromPolyline(JsonElement args)
    {
        var surfaceName = args.GetRequiredString("surface");
        var handleHex = args.GetRequiredString("polyline_handle");
        var description = args.GetOptionalString("description") ?? "breakline";

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        if (!long.TryParse(handleHex, System.Globalization.NumberStyles.HexNumber, null, out var handleLong))
            throw new ToolException($"invalid handle hex: '{handleHex}'");
        if (!doc.Database.TryGetObjectId(new Handle(handleLong), out var polylineId))
            throw new ToolException($"no object with handle {handleHex}");

        var surfaceId = Helpers.FindSurfaceId(tr, civDoc, surfaceName)
            ?? throw new ToolException($"surface '{surfaceName}' not found");
        var surface = (TinSurface)tr.GetObject(surfaceId, OpenMode.ForWrite);

        var polylineIds = new ObjectIdCollection { polylineId };
        surface.BreaklinesDefinition.AddStandardBreaklines(
            polylineIds,
            1.0,    // mid-ordinate distance
            1.0,    // maximum distance
            45.0,   // weeding distance
            0.0);   // weeding angle
        surface.Rebuild();
        tr.Commit();
        return new { surface = surfaceName, description, added = true };
    }
}
