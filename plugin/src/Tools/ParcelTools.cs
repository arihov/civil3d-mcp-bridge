using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using static Civil3DMcpBridge.Common;

namespace Civil3DMcpBridge.Tools;

/// <summary>Sites and parcels (land acquisition / RAP schedules).</summary>
internal static class ParcelTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_list_sites_parcels", List);
    }

    private static object List(JsonElement args)
    {
        var csv = args.GetOptionalString("csv_path");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var sites = new List<object>();
        var rows = new List<object?[]>();
        foreach (ObjectId sid in civ.GetSiteIds())
        {
            var site = (Site)tr.GetObject(sid, OpenMode.ForRead);
            var parcels = new List<object>();
            foreach (ObjectId pid in site.GetParcelIds())
            {
                var p = (Parcel)tr.GetObject(pid, OpenMode.ForRead);
                double? perim = null;
                try { perim = p.GetDistanceAtParameter(p.EndParam); } catch { }
                parcels.Add(new { name = p.Name, number = p.Number, area = R(p.Area, 2), areaHa = R(p.Area / 10000, 4), perimeter = perim is null ? (double?)null : R(perim.Value, 2), handle = HandleOf(p) });
                rows.Add(new object?[] { site.Name, p.Name, p.Number, R(p.Area, 2), R(p.Area / 10000, 4), perim is null ? null : R(perim.Value, 2) });
            }
            sites.Add(new
            {
                name = site.Name, parcelCount = parcels.Count,
                alignmentCount = site.GetAlignmentIds().Count, featureLineCount = site.GetFeatureLineIds().Count,
                parcels,
            });
        }
        tr.Commit();
        string? written = null;
        if (!string.IsNullOrWhiteSpace(csv))
        {
            written = Helpers.ResolvePath(doc, csv);
            WriteText(written, Csv(new[] { "site", "parcel", "number", "area_m2", "area_ha", "perimeter_m" }, rows));
        }
        return new { siteCount = sites.Count, csvPath = written, sites };
    }
}
