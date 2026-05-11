using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3DMcpBridge.Tools;

/// <summary>
/// Drawing-level utilities: save, zoom, layer CRUD, run an arbitrary
/// AutoCAD/Civil 3D command (escape hatch for anything we haven't wrapped).
/// </summary>
internal static class DrawingTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_get_drawing_info", GetDrawingInfo);
        ToolRegistry.Register("civil3d_save_drawing", SaveDrawing);
        ToolRegistry.Register("civil3d_zoom_to_alignment", ZoomToAlignment);
        ToolRegistry.Register("civil3d_zoom_extents", ZoomExtents);
        ToolRegistry.Register("civil3d_list_layers", ListLayers);
        ToolRegistry.Register("civil3d_create_layer", CreateLayer);
        ToolRegistry.Register("civil3d_set_current_layer", SetCurrentLayer);
        ToolRegistry.Register("civil3d_run_command", RunCommand);
    }

    private static object GetDrawingInfo(JsonElement args)
    {
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var counts = new
        {
            alignments = civDoc.GetAlignmentIds().Count,
            corridors = civDoc.CorridorCollection.Count,
            surfaces = civDoc.GetSurfaceIds().Count,
            cogoPoints = civDoc.CogoPoints.Count,
            pipeNetworks = civDoc.GetPipeNetworkIds().Count,
            assemblies = civDoc.AssemblyCollection.Count,
        };
        var result = new
        {
            name = doc.Name,
            filename = string.IsNullOrEmpty(doc.Database.Filename) ? null : doc.Database.Filename,
            isReadOnly = doc.IsReadOnly,
            isUnnamed = string.IsNullOrEmpty(doc.Database.Filename),
            dbVersion = doc.Database.OriginalFileVersion.ToString(),
            counts,
        };
        tr.Commit();
        return result;
    }

    private static object SaveDrawing(JsonElement args)
    {
        var path = args.GetOptionalString("path");
        var (doc, _) = DrawingContext.RequireActive();

        // Document.CloseAndSave / SaveAs are document-mgmt operations and
        // must be on the main thread (which we already are). We use
        // SendStringToExecute for QSAVE/SAVEAS since the .NET wrappers
        // are picky about file-locking states.
        if (string.IsNullOrEmpty(path))
        {
            doc.SendStringToExecute("_.QSAVE ", true, false, true);
            return new { saved = true, path = doc.Database.Filename };
        }

        // Quote-escape the path for AutoCAD's command-line parser.
        var quoted = path.Replace("\\", "/").Replace("\"", "");
        doc.SendStringToExecute($"_.SAVEAS  2018 \"{quoted}\" ", true, false, true);
        return new { saved = true, path };
    }

    private static object ZoomToAlignment(JsonElement args)
    {
        var name = args.GetRequiredString("alignment");
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = AlignmentTools.FindAlignmentByName(tr, civDoc, name)
            ?? throw new ToolException($"alignment '{name}' not found");

        var extents = al.GeometricExtents;
        // Expand extents slightly so the alignment doesn't crowd the viewport.
        var pad = 0.1 * Math.Max(extents.MaxPoint.X - extents.MinPoint.X,
                                 extents.MaxPoint.Y - extents.MinPoint.Y);
        var min = new Point3d(extents.MinPoint.X - pad, extents.MinPoint.Y - pad, 0);
        var max = new Point3d(extents.MaxPoint.X + pad, extents.MaxPoint.Y + pad, 0);

        ZoomTo(doc.Editor, min, max);
        tr.Commit();
        return new
        {
            alignment = name,
            zoomedTo = new
            {
                min = new { x = min.X, y = min.Y },
                max = new { x = max.X, y = max.Y },
            },
        };
    }

    private static object ZoomExtents(JsonElement args)
    {
        var (doc, _) = DrawingContext.RequireActive();
        doc.SendStringToExecute("_.ZOOM _Extents ", true, false, true);
        return new { zoomed = "extents" };
    }

    private static object ListLayers(JsonElement args)
    {
        var (doc, _) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
        var results = new List<object>();
        foreach (ObjectId id in lt)
        {
            var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            results.Add(new
            {
                name = l.Name,
                isFrozen = l.IsFrozen,
                isOff = l.IsOff,
                isLocked = l.IsLocked,
                isCurrent = doc.Database.Clayer == id,
                colorIndex = l.Color.ColorIndex,
                lineweight = l.LineWeight.ToString(),
            });
        }
        tr.Commit();
        return new { count = results.Count, layers = results };
    }

    private static object CreateLayer(JsonElement args)
    {
        var name = args.GetRequiredString("name");
        var colorIndex = args.GetOptionalInt("color_index", 7);
        var description = args.GetOptionalString("description") ?? "";

        var (doc, _) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForWrite);
        if (lt.Has(name))
        {
            tr.Commit();
            return new { name, created = false, alreadyExists = true };
        }
        var l = new LayerTableRecord
        {
            Name = name,
            Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex),
            Description = description,
        };
        lt.Add(l);
        tr.AddNewlyCreatedDBObject(l, true);
        tr.Commit();
        return new { name, created = true, colorIndex };
    }

    private static object SetCurrentLayer(JsonElement args)
    {
        var name = args.GetRequiredString("name");
        var (doc, _) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(name))
            throw new ToolException($"layer '{name}' not found — create it first");
        doc.Database.Clayer = lt[name];
        tr.Commit();
        return new { currentLayer = name };
    }

    /// <summary>
    /// Escape hatch: send an arbitrary AutoCAD/Civil 3D command line to
    /// the active document, exactly as if typed at the command prompt.
    /// Pass <c>command</c> ending with a space (Enter). Beware: there's
    /// no return value — this is "fire and forget". Use it for things
    /// like running scripts or invoking AutoLISP routines.
    /// Args: { "command": "string" }
    /// </summary>
    private static object RunCommand(JsonElement args)
    {
        var command = args.GetRequiredString("command");
        var (doc, _) = DrawingContext.RequireActive();
        doc.SendStringToExecute(command, true, false, true);
        return new { sent = command, length = command.Length };
    }

    private static void ZoomTo(Editor ed, Point3d min, Point3d max)
    {
        using var view = ed.GetCurrentView();
        var center = new Point2d((min.X + max.X) / 2, (min.Y + max.Y) / 2);
        view.CenterPoint = center;
        view.Width = max.X - min.X;
        view.Height = max.Y - min.Y;
        ed.SetCurrentView(view);
    }
}
