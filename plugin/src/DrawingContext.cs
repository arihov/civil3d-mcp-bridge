using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Civil.ApplicationServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3DMcpBridge;

/// <summary>
/// Resolves which drawing a tool call acts on. By default that is the active
/// (MDI) document; an /invoke request may name another open drawing via its
/// "document" field (file name or full path), set here for the duration of the
/// call. All calls run serially on the main thread, so a static is safe.
/// </summary>
internal static class DrawingContext
{
    public static string? TargetDocument { get; set; }

    public static (Document Doc, CivilDocument Civ) RequireActive()
    {
        var doc = TargetDocument is null
            ? AcadApp.DocumentManager.MdiActiveDocument
                ?? throw new ToolException("no active Civil 3D drawing - open a DWG first")
            : FindDocument(TargetDocument)
                ?? throw new ToolException(
                    $"drawing '{TargetDocument}' is not open. Open drawings: {string.Join(", ", OpenNames())}");

        CivilDocument? civ;
        try { civ = CivilDocument.GetCivilDocument(doc.Database); }
        catch { civ = null; }
        civ ??= ReferenceEquals(doc, AcadApp.DocumentManager.MdiActiveDocument) ? CivilApplication.ActiveDocument : null;
        if (civ is null) throw new ToolException($"'{Path.GetFileName(doc.Name)}' is not a Civil 3D document");
        return (doc, civ);
    }

    public static Document? FindDocument(string nameOrPath)
    {
        var wanted = nameOrPath.Trim();
        foreach (Document d in AcadApp.DocumentManager)
        {
            if (string.Equals(d.Name, wanted, StringComparison.OrdinalIgnoreCase)) return d;
            var file = Path.GetFileName(d.Name);
            if (string.Equals(file, wanted, StringComparison.OrdinalIgnoreCase)) return d;
            if (string.Equals(Path.GetFileNameWithoutExtension(file), wanted, StringComparison.OrdinalIgnoreCase)) return d;
        }
        return null;
    }

    private static IEnumerable<string> OpenNames()
    {
        foreach (Document d in AcadApp.DocumentManager) yield return Path.GetFileName(d.Name);
    }

    /// <summary>Main-thread only. Used by /health and civil3d_list_documents.</summary>
    public static object DescribeDocuments()
    {
        var active = AcadApp.DocumentManager.MdiActiveDocument;
        var list = new List<object>();
        foreach (Document d in AcadApp.DocumentManager)
        {
            list.Add(new
            {
                name = Path.GetFileName(d.Name),
                path = d.Name,
                active = ReferenceEquals(d, active),
                readOnly = d.IsReadOnly,
                commandInProgress = string.IsNullOrEmpty(d.CommandInProgress) ? null : d.CommandInProgress,
            });
        }
        return new { active = active is null ? null : Path.GetFileName(active.Name), count = list.Count, open = list };
    }
}
