using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

namespace Civil3DMcpBridge.Tools;

/// <summary>
/// Roundabout geometry — build a closed circular centreline alignment from an
/// exact 4-quadrant bulged circle, and (optionally) inner / outer kerb offset
/// alignments at a given carriageway width. Corridors / superelevation are
/// layered on top with the existing corridor + offset tools.
/// </summary>
internal static class RoundaboutTools
{
    // tan(90deg / 4) → a single 90-degree arc's bulge factor on a Polyline.
    private const double QuadrantBulge = 0.41421356237309503;

    public static void Register()
    {
        ToolRegistry.Register("civil3d_create_roundabout_centerline", CreateCenterline);
    }

    /// <summary>
    /// Args:
    /// {
    ///   "center_x": number, "center_y": number,
    ///   "radius": number,                 // centreline radius (m / drawing units)
    ///   "name": "string",
    ///   "lane_width": number (optional),  // if >0, create inner+outer kerb offsets
    ///   "create_offsets": bool (default true),
    ///   "site": "string" (optional),
    ///   "description": "string" (optional)
    /// }
    /// </summary>
    private static object CreateCenterline(JsonElement args)
    {
        var cx = args.GetRequiredDouble("center_x");
        var cy = args.GetRequiredDouble("center_y");
        var radius = args.GetRequiredDouble("radius");
        var name = args.GetRequiredString("name");
        var laneWidth = args.GetOptionalDouble("lane_width", 0.0);
        var createOffsets = args.GetOptionalBool("create_offsets", true);
        var description = args.GetOptionalString("description");

        if (radius <= 0) throw new ToolException("radius must be positive");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var ms = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);

        // Exact circle as a closed 4-vertex bulged polyline (quadrant arcs, CCW).
        var circle = new Polyline();
        for (var i = 0; i < 4; i++)
        {
            double ang = i * Math.PI / 2.0;
            var pt = new Point2d(cx + radius * Math.Cos(ang), cy + radius * Math.Sin(ang));
            circle.AddVertexAt(i, pt, QuadrantBulge, 0, 0);
        }
        circle.Closed = true;
        ms.AppendEntity(circle);
        tr.AddNewlyCreatedDBObject(circle, true);

        var options = new PolylineOptions
        {
            PlineId = circle.ObjectId,
            AddCurvesBetweenTangents = false,
            EraseExistingEntities = true,
        };

        ObjectId alId;
        try
        {
            alId = Alignment.Create(civDoc, options, name, ObjectId.Null, ObjectId.Null, ObjectId.Null, ObjectId.Null);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            circle.Erase();
            throw new ToolException($"could not create circular alignment: {ex.Message}");
        }

        var al = (Alignment)tr.GetObject(alId, OpenMode.ForWrite);
        if (!string.IsNullOrEmpty(description)) al.Description = description;

        var result = new Dictionary<string, object>
        {
            ["name"] = al.Name,
            ["radius"] = radius,
            ["circumference"] = 2.0 * Math.PI * radius,
            ["startStation"] = al.StartingStation,
            ["endStation"] = al.EndingStation,
            ["handle"] = al.Handle.Value.ToString("x"),
        };

        if (createOffsets && laneWidth > 0)
        {
            var offsets = new List<object>();
            foreach (var (suffix, off) in new[] { ("IN", laneWidth / 2.0), ("OUT", -laneWidth / 2.0) })
            {
                string offName = $"{name}-{suffix}";
                try
                {
                    var oid = Alignment.CreateOffsetAlignment(offName, al.ObjectId, off, al.StyleId);
                    var oal = (Alignment)tr.GetObject(oid, OpenMode.ForRead);
                    offsets.Add(new { name = oal.Name, offset = off, length = oal.Length });
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    offsets.Add(new { name = offName, offset = off, error = ex.Message });
                }
            }
            result["kerbOffsets"] = offsets;
            result["laneWidth"] = laneWidth;
        }

        tr.Commit();
        return result;
    }
}
