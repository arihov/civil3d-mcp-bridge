using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3DMcpBridge.Tools;

internal static class AlignmentTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_list_alignments", ListAlignments);
        ToolRegistry.Register("civil3d_get_alignment_info", GetAlignmentInfo);
        ToolRegistry.Register("civil3d_get_xy_at_station", GetXyAtStation);
        ToolRegistry.Register("civil3d_point_at_chainage", GetXyAtStation); // alias
        ToolRegistry.Register("civil3d_get_station_at_xy", GetStationAtXy);
        ToolRegistry.Register("civil3d_export_alignment_geometry", ExportGeometry);
        // create_alignment_from_polyline, create_offset_alignment, set_design_speed
        // are registered by AlignmentEditTools.
    }

    private static object ListAlignments(JsonElement args)
    {
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var results = new List<object>();
        foreach (ObjectId id in civDoc.GetAlignmentIds())
        {
            var al = (Alignment)tr.GetObject(id, OpenMode.ForRead);
            results.Add(new
            {
                name = al.Name,
                description = al.Description,
                length = al.Length,
                startStation = al.StartingStation,
                endStation = al.EndingStation,
                alignmentType = al.AlignmentType.ToString(),
                style = al.StyleName,
                handle = al.Handle.Value.ToString("x"),
            });
        }
        tr.Commit();
        return new { count = results.Count, alignments = results };
    }

    private static object GetAlignmentInfo(JsonElement args)
    {
        var name = args.GetRequiredString("name");
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = Helpers.RequireAlignment(tr, civDoc, name);
        var entities = new List<object>();
        foreach (AlignmentEntity ent in al.Entities)
        {
            double length = 0, startStation = 0, endStation = 0;
            if (ent is AlignmentCurve cv)
            {
                length = cv.Length; startStation = cv.StartStation; endStation = cv.EndStation;
            }
            entities.Add(new
            {
                type = ent.EntityType.ToString(),
                length,
                startStation,
                endStation,
            });
        }
        var profileNames = new List<string>();
        foreach (ObjectId pid in al.GetProfileIds())
        {
            var p = (Profile)tr.GetObject(pid, OpenMode.ForRead);
            profileNames.Add(p.Name);
        }
        tr.Commit();
        return new
        {
            name = al.Name,
            description = al.Description,
            length = al.Length,
            startStation = al.StartingStation,
            endStation = al.EndingStation,
            referencePoint = new { x = al.ReferencePoint.X, y = al.ReferencePoint.Y },
            style = al.StyleName,
            site = al.SiteName,
            entityCount = al.Entities.Count,
            entities,
            profiles = profileNames,
        };
    }

    private static object GetXyAtStation(JsonElement args)
    {
        var name = args.GetRequiredString("alignment");
        var station = args.GetRequiredDouble("station");
        var offset = args.GetOptionalDouble("offset", 0);

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = Helpers.RequireAlignment(tr, civDoc, name);
        if (station < al.StartingStation - 1e-6 || station > al.EndingStation + 1e-6)
            throw new ToolException(
                $"station {station} outside alignment range [{al.StartingStation}, {al.EndingStation}]");

        var pt = Helpers.PointAtStation(al, station, offset);
        var dir = Helpers.DirectionAtStation(al, station);
        tr.Commit();
        return new
        {
            alignment = name,
            station,
            offset,
            x = pt.X,
            y = pt.Y,
            bearingRadians = dir,
            bearingDegrees = dir * 180.0 / Math.PI,
        };
    }

    private static object GetStationAtXy(JsonElement args)
    {
        var name = args.GetRequiredString("alignment");
        var x = args.GetRequiredDouble("x");
        var y = args.GetRequiredDouble("y");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = Helpers.RequireAlignment(tr, civDoc, name);
        double station = 0, offset = 0;
        al.StationOffset(x, y, ref station, ref offset);
        tr.Commit();
        return new { alignment = name, x, y, station, offset };
    }

    private static object ExportGeometry(JsonElement args)
    {
        var name = args.GetRequiredString("alignment");
        var interval = args.GetOptionalDouble("interval", 10.0);
        if (interval <= 0) throw new ToolException("'interval' must be positive");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = Helpers.RequireAlignment(tr, civDoc, name);
        var samples = new List<object>();

        var station = al.StartingStation;
        while (station <= al.EndingStation + 1e-6)
        {
            var pt = Helpers.PointAtStation(al, station);
            var dir = Helpers.DirectionAtStation(al, station);
            samples.Add(new
            {
                station,
                x = pt.X,
                y = pt.Y,
                bearingDegrees = dir * 180.0 / Math.PI,
            });
            station += interval;
        }
        tr.Commit();
        return new { alignment = name, interval, count = samples.Count, samples };
    }

    // Legacy entry point still used by other tool families.
    internal static Alignment? FindAlignmentByName(Transaction tr, CivilDocument civDoc, string name)
    {
        var id = Helpers.FindAlignmentId(tr, civDoc, name);
        if (id is null) return null;
        return (Alignment)tr.GetObject(id.Value, OpenMode.ForRead);
    }
}

internal static class DrawingContext
{
    public static (Autodesk.AutoCAD.ApplicationServices.Document Doc, CivilDocument Civ) RequireActive()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new ToolException("no active Civil 3D drawing — open a DWG first");
        var civ = CivilApplication.ActiveDocument
            ?? throw new ToolException("active drawing is not a Civil 3D document");
        return (doc, civ);
    }
}
