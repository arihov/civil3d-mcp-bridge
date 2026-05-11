using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace Civil3DMcpBridge;

/// <summary>
/// Lookup + utility helpers shared across every tool family — name-based
/// ObjectId resolution for the Civil 3D collections, station/offset
/// sampling, and path resolution relative to the active drawing.
/// </summary>
internal static class Helpers
{
    public static ObjectId? FindAlignmentId(Transaction tr, CivilDocument civDoc, string name)
    {
        foreach (ObjectId id in civDoc.GetAlignmentIds())
        {
            var al = (Alignment)tr.GetObject(id, OpenMode.ForRead);
            if (string.Equals(al.Name, name, StringComparison.OrdinalIgnoreCase))
                return id;
        }
        return null;
    }

    public static Alignment RequireAlignment(Transaction tr, CivilDocument civDoc, string name)
    {
        var id = FindAlignmentId(tr, civDoc, name)
            ?? throw new ToolException($"alignment '{name}' not found");
        return (Alignment)tr.GetObject(id, OpenMode.ForRead);
    }

    public static ObjectId? FindCorridorId(Transaction tr, CivilDocument civDoc, string name)
    {
        foreach (ObjectId id in civDoc.CorridorCollection)
        {
            var c = (Corridor)tr.GetObject(id, OpenMode.ForRead);
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                return id;
        }
        return null;
    }

    public static ObjectId? FindAssemblyId(Transaction tr, CivilDocument civDoc, string name)
    {
        foreach (ObjectId id in civDoc.AssemblyCollection)
        {
            var a = (Assembly)tr.GetObject(id, OpenMode.ForRead);
            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                return id;
        }
        return null;
    }

    public static ObjectId? FindSurfaceId(Transaction tr, CivilDocument civDoc, string name)
    {
        foreach (ObjectId id in civDoc.GetSurfaceIds())
        {
            var s = (Surface)tr.GetObject(id, OpenMode.ForRead);
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                return id;
        }
        return null;
    }

    /// <summary>
    /// World-space point at (station, offset) on the alignment. Z is left
    /// at zero; callers that want a profile elevation should use
    /// <see cref="GeometryHelpers.PointOnAlignment"/>.
    /// </summary>
    public static Point3d PointAtStation(Alignment al, double station, double offset = 0)
    {
        double easting = 0, northing = 0;
        al.PointLocation(station, offset, ref easting, ref northing);
        return new Point3d(easting, northing, 0);
    }

    /// <summary>Tangent direction (radians, 0=East) at a station — thin wrapper.</summary>
    public static double DirectionAtStation(Alignment al, double station)
        => GeometryHelpers.TangentAngleAtStation(al, station);

    /// <summary>
    /// Resolve a user-supplied path. Absolute paths pass through; relative
    /// paths anchor against the directory of the active drawing if it has
    /// been saved, otherwise the user's profile folder.
    /// </summary>
    public static string ResolvePath(Document doc, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ToolException("path must not be empty");
        if (Path.IsPathRooted(path)) return path;

        string baseDir;
        var dwgName = doc.Name;
        if (!string.IsNullOrEmpty(dwgName) && Path.IsPathRooted(dwgName))
            baseDir = Path.GetDirectoryName(dwgName)!;
        else
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.GetFullPath(Path.Combine(baseDir, path));
    }
}
