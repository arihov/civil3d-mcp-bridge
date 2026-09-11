using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcpBridge.Tools;

/// <summary>
/// Junction / intersection layout — corner fillet arcs, approach edge lines
/// and a central channelizing island, generated as plain AutoCAD geometry at
/// the crossing of two virtual centrelines. This is a design-assist layout
/// (draws the swept-corner geometry a designer would dimension); it does not
/// create a native Civil 3D <c>Intersection</c> object.
///
/// All geometry is right-angle: road A runs along <c>bearing_deg</c>, road B
/// perpendicular to it. A rounded corner (fillet) of radius <c>corner_radius</c>
/// is tangent to both kerb lines at each of the four quadrants.
/// </summary>
internal static class JunctionTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_create_junction_corner_fillets", CreateCornerFillets);
    }

    /// <summary>
    /// Args:
    /// {
    ///   "center_x": number, "center_y": number,     // crossing point
    ///   "bearing_deg": number,                        // road A direction of travel, 0 = East, CCW
    ///   "half_width_a": number, "half_width_b": number, // carriageway half widths
    ///   "corner_radius": number,
    ///   "draw_approach_lines": bool (default true),
    ///   "approach_length": number (default 15),       // kerb line extent beyond corner
    ///   "island": bool (default false),               // central channelizing island circle
    ///   "layer": "string" (default "C-JUNCTION"),
    ///   "color_aci": int (default 7)
    /// }
    /// </summary>
    private static object CreateCornerFillets(JsonElement args)
    {
        var cx = args.GetRequiredDouble("center_x");
        var cy = args.GetRequiredDouble("center_y");
        var bearingDeg = args.GetOptionalDouble("bearing_deg", 0.0);
        var halfA = args.GetRequiredDouble("half_width_a");
        var halfB = args.GetRequiredDouble("half_width_b");
        var r = args.GetRequiredDouble("corner_radius");
        var drawApproach = args.GetOptionalBool("draw_approach_lines", true);
        var approachLen = args.GetOptionalDouble("approach_length", 15.0);
        var island = args.GetOptionalBool("island", false);
        var layer = args.GetOptionalString("layer") ?? "C-JUNCTION";
        var colorAci = args.GetOptionalInt("color_aci", 7);

        if (halfA <= 0 || halfB <= 0) throw new ToolException("half_width_a / half_width_b must be positive");
        if (r <= 0) throw new ToolException("corner_radius must be positive");

        var (doc, _) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var ms = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
        EnsureLayer(tr, doc.Database, layer);

        double theta = bearingDeg * Math.PI / 180.0;
        var uA = new Vector2d(Math.Cos(theta), Math.Sin(theta));    // road A axis
        var perpA = uA.RotateBy(Math.PI / 2.0);                     // road A normal (left positive)
        var uB = perpA;                                            // road B axis (perpendicular)
        var perpB = uB.RotateBy(Math.PI / 2.0);                     // road B normal

        var corners = new List<object>();
        int arcs = 0;

        foreach (var sa in new[] { 1.0, -1.0 })
        foreach (var sb in new[] { 1.0, -1.0 })
        {
            // Kerb line of A at signed offset sa*halfA (direction uA), kerb line of B at sb*halfB.
            var pA = new Point2d(cx, cy) + perpA.MultiplyScalar(sa * halfA);
            var pB = new Point2d(cx, cy) + perpB.MultiplyScalar(sb * halfB);
            // Intersection of the two kerb lines = the sharp corner.
            if (!LineLineIntersection(pA, uB, pB, uA, out var corner))
                continue; // parallel (shouldn't happen for right angle)

            // Interior unit directions (from kerb toward centre).
            var inA = perpA.MultiplyScalar(-sa);
            var inB = perpB.MultiplyScalar(-sb);
            var center = corner + inA.MultiplyScalar(r) + inB.MultiplyScalar(r);
            var tanA = corner + inA.MultiplyScalar(r);
            var tanB = corner + inB.MultiplyScalar(r);

            double startAng = Math.Atan2(tanA.Y - center.Y, tanA.X - center.X);
            double endAng = Math.Atan2(tanB.Y - center.Y, tanB.X - center.X);

            var arc = new Arc(new Point3d(center.X, center.Y, 0), r, startAng, endAng)
            {
                Layer = layer,
                Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorAci),
            };
            ms.AppendEntity(arc);
            tr.AddNewlyCreatedDBObject(arc, true);
            arcs++;

            corners.Add(new
            {
                quadrant = $"{(sa > 0 ? "L" : "R")}{(sb > 0 ? "A" : "B")}",
                cornerPoint = new { x = corner.X, y = corner.Y },
                filletCenter = new { x = center.X, y = center.Y },
                radius = r,
                startAngleDeg = startAng * 180.0 / Math.PI,
                endAngleDeg = endAng * 180.0 / Math.PI,
            });
        }

        int approachPolylines = 0;
        if (drawApproach)
        {
            foreach (var (axis, normal, half, sign) in new[]
                     {
                         (uA, perpA, halfA, 1.0), (uA, perpA, halfA, -1.0),
                         (uB, perpB, halfB, 1.0), (uB, perpB, halfB, -1.0),
                     })
            {
                var base_ = new Point2d(cx, cy) + normal.MultiplyScalar(sign * half);
                var p1 = base_ - axis.MultiplyScalar(approachLen);
                var p2 = base_ + axis.MultiplyScalar(approachLen);
                var pl = new Polyline();
                pl.AddVertexAt(0, p1, 0, 0, 0);
                pl.AddVertexAt(1, p2, 0, 0, 0);
                pl.Layer = layer;
                pl.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorAci);
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                approachPolylines++;
            }
        }

        if (island)
        {
            var islandR = Math.Min(halfA, halfB) * 0.6;
            var circle = new Circle(new Point3d(cx, cy, 0), Vector3d.ZAxis, islandR)
            {
                Layer = layer,
                Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorAci),
            };
            ms.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
        }

        tr.Commit();
        return new
        {
            center = new { x = cx, y = cy },
            bearingDeg,
            cornerRadius = r,
            filletArcsCreated = arcs,
            approachPolylines,
            islandCreated = island,
            layer,
            corners,
        };
    }

    /// <summary>Solve p1 + t*d1 = p2 + s*d2 for the intersection point (2D).</summary>
    private static bool LineLineIntersection(
        Point2d p1, Vector2d d1, Point2d p2, Vector2d d2, out Point2d hit)
    {
        hit = Point2d.Origin;
        double denom = d1.X * d2.Y - d1.Y * d2.X;
        if (Math.Abs(denom) < 1e-9) return false;
        double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        double t = (dx * d2.Y - dy * d2.X) / denom;
        hit = p1 + d1.MultiplyScalar(t);
        return true;
    }

    private static void EnsureLayer(Transaction tr, Database db, string layerName)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        if (lt.Has(layerName)) return;
        var newLayer = new LayerTableRecord { Name = layerName };
        lt.Add(newLayer);
        tr.AddNewlyCreatedDBObject(newLayer, true);
    }
}

/// <summary>Scalar-multiply convenience for AutoCAD's 2D vectors.</summary>
internal static class Vector2dExt
{
    public static Vector2d MultiplyScalar(this Vector2d v, double k) => v * k;
}
