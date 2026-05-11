using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;

namespace Civil3DMcpBridge.Tools;

/// <summary>
/// Road markings — solid/dashed lane lines, edge lines, double yellows,
/// pedestrian crossings. Output is plain AutoCAD polylines on dedicated
/// layers so they print correctly without needing Civil 3D specific
/// rendering. Geometry is sampled from the parent alignment so markings
/// follow horizontal curvature exactly.
/// </summary>
internal static class MarkingTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_draw_lane_marking", DrawLaneMarking);
        ToolRegistry.Register("civil3d_draw_pedestrian_crossing", DrawPedestrianCrossing);
    }

    /// <summary>
    /// Draw a lane marking polyline parallel to an alignment between two
    /// chainages at a constant lateral offset.
    ///
    /// Args:
    /// {
    ///   "alignment": "string",
    ///   "start_station": number,
    ///   "end_station": number,
    ///   "offset": number,                       // right-positive
    ///   "marking_type": "solid"|"dashed"|"double_solid"|"double_dashed"|"solid_dashed",
    ///   "step": number (default 1.0),           // sampling step along alignment
    ///   "dash_length": number (default 3.0),    // for dashed markings
    ///   "gap_length": number (default 6.0),
    ///   "double_spacing": number (default 0.1), // gap between the two parallel lines
    ///   "layer": "string" (default "C-ROAD-MARK"),
    ///   "color_aci": int (1..255 AutoCAD colour index, default 7 = white)
    /// }
    /// </summary>
    private static object DrawLaneMarking(JsonElement args)
    {
        var alignmentName = args.GetRequiredString("alignment");
        var startStation = args.GetRequiredDouble("start_station");
        var endStation = args.GetRequiredDouble("end_station");
        var offset = args.GetRequiredDouble("offset");
        var markingType = (args.GetOptionalString("marking_type") ?? "solid").ToLowerInvariant();
        var step = args.GetOptionalDouble("step", 1.0);
        var dashLength = args.GetOptionalDouble("dash_length", 3.0);
        var gapLength = args.GetOptionalDouble("gap_length", 6.0);
        var doubleSpacing = args.GetOptionalDouble("double_spacing", 0.1);
        var layer = args.GetOptionalString("layer") ?? "C-ROAD-MARK";
        var colorAci = args.GetOptionalInt("color_aci", 7);

        if (endStation <= startStation)
            throw new ToolException("'end_station' must be greater than 'start_station'");
        if (step <= 0) throw new ToolException("'step' must be positive");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = AlignmentTools.FindAlignmentByName(tr, civDoc, alignmentName)
            ?? throw new ToolException($"alignment '{alignmentName}' not found");

        var ms = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
        EnsureLayer(tr, doc.Database, layer);

        int segmentsDrawn = 0;
        var segments = ComputeSegments(startStation, endStation, markingType, dashLength, gapLength);

        // For each segment (a, b), build one or two polylines depending on type.
        foreach (var (a, b) in segments)
        {
            switch (markingType)
            {
                case "solid":
                case "dashed":
                    segmentsDrawn += DrawSingleParallel(tr, ms, al, a, b, offset, step, layer, colorAci);
                    break;
                case "double_solid":
                case "double_dashed":
                    segmentsDrawn += DrawSingleParallel(tr, ms, al, a, b, offset + doubleSpacing / 2, step, layer, colorAci);
                    segmentsDrawn += DrawSingleParallel(tr, ms, al, a, b, offset - doubleSpacing / 2, step, layer, colorAci);
                    break;
                case "solid_dashed":
                    // Solid on the no-overtaking side, dashed on the other.
                    // Here we approximate: the inner one (lower abs offset) solid.
                    var inner = offset >= 0 ? offset - doubleSpacing / 2 : offset + doubleSpacing / 2;
                    var outer = offset >= 0 ? offset + doubleSpacing / 2 : offset - doubleSpacing / 2;
                    // The segment we're inside represents a "dash" already in dashed mode,
                    // so just draw both as solid here and let the segment loop handle dashing.
                    segmentsDrawn += DrawSingleParallel(tr, ms, al, a, b, inner, step, layer, colorAci);
                    segmentsDrawn += DrawSingleParallel(tr, ms, al, a, b, outer, step, layer, colorAci);
                    break;
                default:
                    throw new ToolException($"unknown marking_type: '{markingType}'");
            }
        }

        tr.Commit();
        return new
        {
            alignment = alignmentName,
            markingType,
            startStation,
            endStation,
            offset,
            segmentsRequested = segments.Count,
            polylineCount = segmentsDrawn,
            layer,
        };
    }

    /// <summary>
    /// Draw a zebra-style pedestrian crossing perpendicular to the
    /// alignment at a station. Produces N parallel bars on the crossing
    /// layer; the swath extends from -lane_width/2 to +lane_width/2 on
    /// each side.
    ///
    /// Args:
    /// {
    ///   "alignment": "string",
    ///   "station": number,
    ///   "width": number,            // crossing width along travel direction (default 3.0)
    ///   "lane_width": number,       // total carriageway width (default 7.0)
    ///   "bar_width": number,        // each bar's width (default 0.5)
    ///   "gap": number,              // gap between bars (default 0.5)
    ///   "layer": "string" (default "C-ROAD-MARK-CROSSING")
    /// }
    /// </summary>
    private static object DrawPedestrianCrossing(JsonElement args)
    {
        var alignmentName = args.GetRequiredString("alignment");
        var station = args.GetRequiredDouble("station");
        var width = args.GetOptionalDouble("width", 3.0);
        var laneWidth = args.GetOptionalDouble("lane_width", 7.0);
        var barWidth = args.GetOptionalDouble("bar_width", 0.5);
        var gap = args.GetOptionalDouble("gap", 0.5);
        var layer = args.GetOptionalString("layer") ?? "C-ROAD-MARK-CROSSING";

        if (width <= 0 || laneWidth <= 0 || barWidth <= 0)
            throw new ToolException("widths must be positive");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = AlignmentTools.FindAlignmentByName(tr, civDoc, alignmentName)
            ?? throw new ToolException($"alignment '{alignmentName}' not found");
        GeometryHelpers.ValidateStation(al, station);

        var ms = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
        EnsureLayer(tr, doc.Database, layer);

        // Bars are along the travel direction (alignment tangent).
        var halfWidth = width / 2.0;
        var halfLane = laneWidth / 2.0;
        var step = barWidth + gap;

        // Number of bars that fit across the lane.
        var nBars = (int)Math.Floor((laneWidth + gap) / step);
        // Symmetric layout — first bar's centre at -halfLane + barWidth/2
        var firstCentreOffset = -halfLane + barWidth / 2.0;

        var startStation = station - halfWidth;
        var endStation = station + halfWidth;
        if (startStation < al.StartingStation || endStation > al.EndingStation)
            throw new ToolException("crossing extends outside alignment range");

        int barsCreated = 0;
        for (var i = 0; i < nBars; i++)
        {
            var centreOffset = firstCentreOffset + i * step;
            var leftOffset = centreOffset - barWidth / 2.0;
            var rightOffset = centreOffset + barWidth / 2.0;

            // Build a closed polyline rectangle in plan: 4 corners.
            double e1 = 0, n1 = 0, e2 = 0, n2 = 0, e3 = 0, n3 = 0, e4 = 0, n4 = 0;
            al.PointLocation(startStation, leftOffset, ref e1, ref n1);
            al.PointLocation(endStation, leftOffset, ref e2, ref n2);
            al.PointLocation(endStation, rightOffset, ref e3, ref n3);
            al.PointLocation(startStation, rightOffset, ref e4, ref n4);

            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(e1, n1), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(e2, n2), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(e3, n3), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(e4, n4), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            barsCreated++;
        }

        tr.Commit();
        return new
        {
            alignment = alignmentName,
            station,
            width,
            laneWidth,
            barsCreated,
            layer,
        };
    }

    // ------------------------------------------------------------------

    private static List<(double a, double b)> ComputeSegments(
        double start, double end, string markingType, double dashLength, double gapLength)
    {
        var segments = new List<(double, double)>();
        if (markingType.Contains("dashed"))
        {
            var s = start;
            while (s < end)
            {
                var b = Math.Min(end, s + dashLength);
                segments.Add((s, b));
                s = b + gapLength;
            }
        }
        else
        {
            segments.Add((start, end));
        }
        return segments;
    }

    private static int DrawSingleParallel(
        Transaction tr,
        BlockTableRecord ms,
        Alignment al,
        double startStation,
        double endStation,
        double offset,
        double step,
        string layer,
        int colorAci)
    {
        var pl = new Polyline();
        int idx = 0;

        // Clamp to alignment range.
        startStation = Math.Max(startStation, al.StartingStation);
        endStation = Math.Min(endStation, al.EndingStation);
        if (endStation <= startStation) return 0;

        for (var s = startStation; s < endStation; s += step)
        {
            double e = 0, n = 0;
            al.PointLocation(s, offset, ref e, ref n);
            pl.AddVertexAt(idx++, new Point2d(e, n), 0, 0, 0);
        }
        double ex = 0, nx = 0;
        al.PointLocation(endStation, offset, ref ex, ref nx);
        pl.AddVertexAt(idx, new Point2d(ex, nx), 0, 0, 0);

        pl.Layer = layer;
        pl.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorAci);
        ms.AppendEntity(pl);
        tr.AddNewlyCreatedDBObject(pl, true);
        return 1;
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
