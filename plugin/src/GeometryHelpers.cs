using System;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;

namespace Civil3DMcpBridge;

/// <summary>
/// Geometry utilities shared across tools — alignment chainage lookups,
/// direction calculations, perpendicular offsets. Centralised so the
/// formulas for "place an object at station X, offset Y, rotated to match
/// alignment direction" only live in one place.
/// </summary>
internal static class GeometryHelpers
{
    /// <summary>
    /// Returns the world XYZ at a given alignment station and lateral offset.
    /// Offset is right-positive looking down-station. Z is taken from the
    /// supplied profile if non-null; otherwise 0.
    /// </summary>
    public static Point3d PointOnAlignment(
        Alignment alignment,
        double station,
        double offset = 0,
        Profile? profile = null)
    {
        ValidateStation(alignment, station);
        double easting = 0, northing = 0;
        alignment.PointLocation(station, offset, ref easting, ref northing);

        double z = 0;
        if (profile != null)
        {
            try
            {
                z = profile.ElevationAt(station);
            }
            catch
            {
                // Station outside profile range — leave Z=0.
            }
        }

        return new Point3d(easting, northing, z);
    }

    /// <summary>
    /// Returns the alignment's tangent direction angle (radians, AutoCAD
    /// convention: 0=East, π/2=North) at the given station. Computed
    /// numerically by sampling two close points along the alignment.
    /// </summary>
    public static double TangentAngleAtStation(Alignment alignment, double station)
    {
        ValidateStation(alignment, station);
        const double delta = 0.01; // 10 mm — small enough not to cross most curves.

        var sBack = Math.Max(alignment.StartingStation, station - delta);
        var sFwd = Math.Min(alignment.EndingStation, station + delta);
        if (Math.Abs(sFwd - sBack) < 1e-9)
        {
            // Station sits at a boundary; nudge to the opposite end.
            sBack = station;
            sFwd = Math.Min(alignment.EndingStation, station + delta);
        }

        double e1 = 0, n1 = 0, e2 = 0, n2 = 0;
        alignment.PointLocation(sBack, 0, ref e1, ref n1);
        alignment.PointLocation(sFwd, 0, ref e2, ref n2);
        return Math.Atan2(n2 - n1, e2 - e1);
    }

    /// <summary>
    /// Rotation (radians) for an object placed normal to an alignment —
    /// i.e. rotated so its local X axis points along the alignment.
    /// Same as TangentAngleAtStation but renamed for read-site clarity.
    /// </summary>
    public static double AlongAlignmentRotation(Alignment alignment, double station)
        => TangentAngleAtStation(alignment, station);

    /// <summary>
    /// Rotation (radians) for an object placed perpendicular to an alignment
    /// (e.g. a culvert crossing the road). Adds 90° to the tangent angle.
    /// </summary>
    public static double PerpendicularToAlignmentRotation(Alignment alignment, double station)
        => TangentAngleAtStation(alignment, station) + Math.PI / 2.0;

    public static void ValidateStation(Alignment alignment, double station)
    {
        if (station < alignment.StartingStation - 0.001 || station > alignment.EndingStation + 0.001)
            throw new ToolException(
                $"station {station:F3} is outside alignment '{alignment.Name}' " +
                $"range [{alignment.StartingStation:F3}, {alignment.EndingStation:F3}]");
    }
}
