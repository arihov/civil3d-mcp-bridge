using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3DMcpBridge.Tools;

/// <summary>Session-level tools: open drawings, drawing settings, undo.</summary>
internal static class SessionTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_list_documents", _ => DrawingContext.DescribeDocuments());
        ToolRegistry.Register("civil3d_activate_document", ActivateDocument);
        ToolRegistry.Register("civil3d_get_drawing_settings", GetDrawingSettings);
        ToolRegistry.Register("civil3d_undo", Undo);
    }

    private static object ActivateDocument(JsonElement args)
    {
        var name = args.GetRequiredString("document");
        var doc = DrawingContext.FindDocument(name) ?? throw new ToolException($"drawing '{name}' is not open");
        AcadApp.DocumentManager.MdiActiveDocument = doc;
        return new { activated = System.IO.Path.GetFileName(doc.Name) };
    }

    /// <summary>Units, coordinate zone and ambient precision - what an engineer checks before trusting numbers.</summary>
    private static object GetDrawingSettings(JsonElement args)
    {
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        var db = doc.Database;
        var s = civ.Settings.DrawingSettings;
        object? zone = null, units = null;
        try
        {
            var uz = s.UnitZoneSettings;
            zone = new
            {
                coordinateSystemCode = uz.CoordinateSystemCode,
                drawingUnits = uz.DrawingUnits.ToString(),
                angularUnits = uz.AngularUnits.ToString(),
                drawingScale = uz.DrawingScale,
                scaleObjectsFromOtherDrawings = uz.ScaleObjectsFromOtherDrawings,
            };
        }
        catch (Exception ex) { zone = new { error = ex.Message }; }
        try
        {
            units = new
            {
                insunits = db.Insunits.ToString(),
                lunits = db.Lunits,
                luprec = db.Luprec,
                measurement = db.Measurement.ToString(),
            };
        }
        catch (Exception ex) { units = new { error = ex.Message }; }

        return new
        {
            drawing = System.IO.Path.GetFileName(doc.Name),
            path = doc.Name,
            unitZone = zone,
            autocadUnits = units,
            extents = new
            {
                min = new { x = db.Extmin.X, y = db.Extmin.Y },
                max = new { x = db.Extmax.X, y = db.Extmax.Y },
            },
        };
    }

    /// <summary>Queue N undo steps (each MCP write is one transaction, usually one undo step).</summary>
    private static object Undo(JsonElement args)
    {
        var steps = Math.Clamp(args.GetOptionalInt("steps", 1), 1, 50);
        var (doc, _) = DrawingContext.RequireActive();
        doc.SendStringToExecute($"_.UNDO {steps} ", true, false, true);
        return new { queued = true, steps, note = "UNDO runs once Civil 3D is idle; verify with a read tool." };
    }
}
