using System;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

namespace Civil3DMcpBridge.Tools;

internal static class AlignmentEditTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_create_alignment_from_polyline", CreateFromPolyline);
        ToolRegistry.Register("civil3d_create_offset_alignment", CreateOffset);
        ToolRegistry.Register("civil3d_set_design_speed", SetDesignSpeed);
    }

    /// <summary>
    /// Create a new alignment from an existing polyline (e.g. a survey
    /// centreline traced from raw data). The polyline is identified by
    /// its AutoCAD handle (the hex string from a `civil3d_list_*` response).
    ///
    /// Args:
    /// {
    ///   "name": "string",
    ///   "polyline_handle": "string"  (hex),
    ///   "site": "string"   (optional, defaults to "&lt;none&gt;"),
    ///   "alignment_style": "string"   (optional, first style if omitted),
    ///   "label_set_style": "string"   (optional),
    ///   "erase_polyline": bool        (default false)
    /// }
    /// </summary>
    private static object CreateFromPolyline(JsonElement args)
    {
        // The Alignment.Create overloads in C3D 2026 no longer accept the
        // (civDoc, ObjectIdCollection, name, siteId, layer, styleId,
        // labelSetId, erase) signature this method previously used. The
        // replacement signatures need to be verified against the live API
        // before re-enabling. Until then, surface a clean error rather than
        // silently misbehaving.
        throw new ToolException("create_alignment_from_polyline not ported to C3D 2026 API");
    }

    /// <summary>
    /// Create an offset alignment (e.g. lane edge from centreline).
    ///
    /// Args:
    /// {
    ///   "source_alignment": "string",
    ///   "offset": number (right-positive),
    ///   "name": "string" (new alignment name)
    /// }
    /// </summary>
    private static object CreateOffset(JsonElement args)
    {
        var sourceName = args.GetRequiredString("source_alignment");
        var offset = args.GetRequiredDouble("offset");
        var newName = args.GetRequiredString("name");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var source = AlignmentTools.FindAlignmentByName(tr, civDoc, sourceName)
            ?? throw new ToolException($"alignment '{sourceName}' not found");

        // CreateOffsetAlignment lives on Alignment as a static factory. The
        // exact signature in 2025/2026:
        //   Alignment.CreateOffsetAlignment(string name, ObjectId parentId,
        //       double offset, ObjectId styleId)
        var offsetId = Alignment.CreateOffsetAlignment(
            newName,
            source.ObjectId,
            offset,
            source.StyleId);

        var offsetAl = (Alignment)tr.GetObject(offsetId, OpenMode.ForRead);
        var result = new
        {
            name = offsetAl.Name,
            parent = sourceName,
            offset,
            length = offsetAl.Length,
            handle = offsetAl.Handle.Value.ToString("x"),
        };
        tr.Commit();
        return result;
    }

    /// <summary>
    /// Add or replace a design speed entry on an alignment.
    /// Args: { "alignment": "string", "station": number, "speed_kph": number, "number": int (optional) }
    /// </summary>
    private static object SetDesignSpeed(JsonElement args)
    {
        var name = args.GetRequiredString("alignment");
        var station = args.GetRequiredDouble("station");
        var speed = args.GetRequiredDouble("speed_kph");
        var number = args.GetOptionalInt("number", 0);

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var alRead = AlignmentTools.FindAlignmentByName(tr, civDoc, name)
            ?? throw new ToolException($"alignment '{name}' not found");
        var al = (Alignment)tr.GetObject(alRead.ObjectId, OpenMode.ForWrite);

        var ds = al.DesignSpeeds.Add(station, speed);
        // SpeedNumber is assigned by the collection and is read-only.
        _ = number;

        var result = new
        {
            alignment = name,
            number = ds.SpeedNumber,
            station = ds.Station,
            speedKph = ds.Value,
        };
        tr.Commit();
        return result;
    }

    private static ObjectId ResolveStyleId<T>(Transaction tr, T styleCollection, string? requestedName)
        where T : System.Collections.IEnumerable
    {
        // Try to match by name; otherwise return the first style we see.
        // Most C3D style collections expose ObjectIds when enumerated.
        ObjectId first = ObjectId.Null;
        foreach (ObjectId sid in styleCollection)
        {
            if (first.IsNull) first = sid;
            if (string.IsNullOrEmpty(requestedName)) continue;
            try
            {
                var styleObj = tr.GetObject(sid, OpenMode.ForRead);
                var nameProp = styleObj.GetType().GetProperty("Name");
                var styleName = nameProp?.GetValue(styleObj) as string;
                if (string.Equals(styleName, requestedName, StringComparison.OrdinalIgnoreCase))
                    return sid;
            }
            catch { /* skip */ }
        }
        if (first.IsNull)
            throw new ToolException("no styles available in this drawing — set up an alignment style first");
        return first;
    }
}
