using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Surface = Autodesk.Civil.DatabaseServices.Surface;
using static Civil3DMcpBridge.Common;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace Civil3DMcpBridge.Tools;

/// <summary>Style discovery and assignment - lets Claude apply house/MoWT drafting standards by name.</summary>
internal static class StyleTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_list_styles", ListStyles);
        ToolRegistry.Register("civil3d_set_object_style", SetObjectStyle);
    }

    private static Dictionary<string, Func<CivilDocument, IEnumerable>> Collections() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["alignment"] = c => c.Styles.AlignmentStyles,
        ["profile"] = c => c.Styles.ProfileStyles,
        ["profile_view"] = c => c.Styles.ProfileViewStyles,
        ["profile_view_band_set"] = c => c.Styles.ProfileViewBandSetStyles,
        ["surface"] = c => c.Styles.SurfaceStyles,
        ["corridor"] = c => c.Styles.CorridorStyles,
        ["point"] = c => c.Styles.PointStyles,
        ["section"] = c => c.Styles.SectionStyles,
        ["section_view"] = c => c.Styles.SectionViewStyles,
        ["sample_line"] = c => c.Styles.SampleLineStyles,
        ["feature_line"] = c => c.Styles.FeatureLineStyles,
        ["parcel"] = c => c.Styles.ParcelStyles,
        ["pipe"] = c => c.Styles.PipeStyles,
        ["structure"] = c => c.Styles.StructureStyles,
        ["marker"] = c => c.Styles.MarkerStyles,
        ["code_set"] = c => c.Styles.CodeSetStyles,
        ["alignment_label_set"] = c => c.Styles.LabelSetStyles.AlignmentLabelSetStyles,
        ["profile_label_set"] = c => c.Styles.LabelSetStyles.ProfileLabelSetStyles,
    };

    /// <summary>Style by name, else the collection's first style, else null id.</summary>
    internal static ObjectId FindStyle(IEnumerable collection, string? name)
    {
        ObjectId first = ObjectId.Null;
        foreach (var o in collection)
        {
            if (o is not ObjectId id) continue;
            if (first.IsNull) first = id;
            if (string.IsNullOrWhiteSpace(name)) return id;
            if (id.GetObject(OpenMode.ForRead) is StyleBase sb && string.Equals(sb.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
        }
        if (!string.IsNullOrWhiteSpace(name) && !first.IsNull)
            throw new ToolException($"style '{name}' not found");
        return first;
    }

    private static object ListStyles(JsonElement args)
    {
        var kind = args.GetOptionalString("collection");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var cols = Collections();
        if (string.IsNullOrWhiteSpace(kind))
        {
            var summary = new Dictionary<string, object>();
            foreach (var (k, get) in cols)
            {
                try
                {
                    var names = new List<string>();
                    foreach (var o in get(civ)) if (o is ObjectId id && tr.GetObject(id, OpenMode.ForRead) is StyleBase s) names.Add(s.Name);
                    summary[k] = names;
                }
                catch (Exception e) { summary[k] = new { error = e.Message }; }
            }
            tr.Commit();
            return new { collections = summary };
        }
        if (!cols.TryGetValue(kind, out var getter))
            throw new ToolException($"unknown collection '{kind}'. Use one of: {string.Join(", ", cols.Keys)}");
        var list = new List<object>();
        foreach (var o in getter(civ))
            if (o is ObjectId id && tr.GetObject(id, OpenMode.ForRead) is StyleBase s)
                list.Add(new { name = s.Name, description = s.Description, handle = HandleOf(s) });
        tr.Commit();
        return new { collection = kind, count = list.Count, styles = list };
    }

    /// <summary>{ object_type: alignment|profile|surface|corridor|feature_line|handle, name?, alignment? (for profile), handle?, style }</summary>
    private static object SetObjectStyle(JsonElement args)
    {
        var type = args.GetRequiredString("object_type").ToLowerInvariant();
        var styleName = args.GetRequiredString("style");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        Entity target;
        string collectionKey;
        switch (type)
        {
            case "alignment":
                target = Helpers.RequireAlignment(tr, civ, args.GetRequiredString("name")); collectionKey = "alignment"; break;
            case "profile":
                target = ProfileTools.FindProfile(tr, civ, args.GetRequiredString("alignment"), args.GetRequiredString("name")); collectionKey = "profile"; break;
            case "surface":
                target = RequireSurface<Surface>(tr, civ, args.GetRequiredString("name")); collectionKey = "surface"; break;
            case "corridor":
                target = (Entity)tr.GetObject(Helpers.FindCorridorId(tr, civ, args.GetRequiredString("name")) ?? throw new ToolException("corridor not found"), OpenMode.ForRead);
                collectionKey = "corridor"; break;
            default:
                var h = args.GetRequiredString("handle");
                target = (Entity)tr.GetObject(RequireHandle(doc.Database, h), OpenMode.ForRead);
                collectionKey = type;
                break;
        }
        if (!Collections().TryGetValue(collectionKey, out var getter))
            throw new ToolException($"no style collection for '{collectionKey}'");
        var styleId = FindStyle(getter(civ), styleName);
        var prop = target.GetType().GetProperty("StyleId") ?? throw new ToolException($"{target.GetType().Name} has no StyleId");
        target.UpgradeOpen();
        prop.SetValue(target, styleId);
        tr.Commit();
        return new { objectType = type, handle = HandleOf(target), style = styleName };
    }
}
