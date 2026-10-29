using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil;
using Autodesk.Civil.DatabaseServices;
using static Civil3DMcpBridge.Common;

namespace Civil3DMcpBridge.Tools;

/// <summary>
/// Vertical design: per-entity geometry with K-values, a one-shot
/// "profile from PVI table" builder, PVI removal and curve-length edits.
/// </summary>
internal static class ProfileDesignTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_get_profile_geometry_detail", GeometryDetail);
        ToolRegistry.Register("civil3d_create_profile_by_pvis", CreateByPvis);
        ToolRegistry.Register("civil3d_delete_pvi", DeletePvi);
        ToolRegistry.Register("civil3d_sample_profile", SampleProfile);
    }

    private static object GeometryDetail(JsonElement args)
    {
        var alName = args.GetRequiredString("alignment");
        var pName = args.GetRequiredString("profile");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var p = ProfileTools.FindProfile(tr, civ, alName, pName);

        var ents = new List<object>();
        foreach (ProfileEntity e in p.Entities)
        {
            switch (e)
            {
                case ProfileTangent t:
                    ents.Add(new
                    {
                        kind = "tangent", startStation = R(t.StartStation), endStation = R(t.EndStation),
                        startElevation = R(t.StartElevation), endElevation = R(t.EndElevation),
                        length = R(t.Length), gradePct = R(t.Grade * 100, 4),
                    });
                    break;
                case ProfileParabolaSymmetric ps:
                    ents.Add(CurveInfo("parabola-symmetric", ps, ps.GradeIn, ps.GradeOut, ps.Length, ps.K, ps.CurveType));
                    break;
                case ProfileParabolaAsymmetric pa:
                    ents.Add(CurveInfo("parabola-asymmetric", pa, pa.GradeIn, pa.GradeOut, pa.Length, pa.K, pa.CurveType));
                    break;
                case ProfileCircular pc:
                    ents.Add(new
                    {
                        kind = "circular", startStation = R(pc.StartStation), endStation = R(pc.EndStation),
                        length = R(pc.Length), radius = R(pc.Radius),
                        gradeInPct = R(pc.GradeIn * 100, 4), gradeOutPct = R(pc.GradeOut * 100, 4),
                        curveType = pc.CurveType.ToString(),
                    });
                    break;
                default:
                    ents.Add(new { kind = e.EntityType.ToString(), startStation = R(e.StartStation), endStation = R(e.EndStation), length = R(e.Length) });
                    break;
            }
        }
        var pvis = new List<object>();
        foreach (ProfilePVI v in p.PVIs)
            pvis.Add(new
            {
                station = R(v.RawStation), elevation = R(v.Elevation),
                gradeInPct = R(v.GradeIn * 100, 4), gradeOutPct = R(v.GradeOut * 100, 4),
                gradeChangePct = R((v.GradeOut - v.GradeIn) * 100, 4),
                type = v.PVIType.ToString(),
            });
        tr.Commit();
        return new
        {
            alignment = alName, profile = p.Name, profileType = p.ProfileType.ToString(),
            startStation = R(p.StartingStation), endStation = R(p.EndingStation),
            elevationMin = R(p.ElevationMin), elevationMax = R(p.ElevationMax),
            entities = ents, pvis,
        };
    }

    private static object CurveInfo(string kind, ProfileEntity e, double gIn, double gOut, double len, double k, VerticalCurveType type)
        => new
        {
            kind, curveType = type.ToString(),
            startStation = R(e.StartStation), endStation = R(e.EndStation),
            startElevation = R(e.StartElevation), endElevation = R(e.EndElevation),
            length = R(len), kValue = R(k, 2),
            gradeInPct = R(gIn * 100, 4), gradeOutPct = R(gOut * 100, 4),
            algebraicDifferencePct = R(Math.Abs(gOut - gIn) * 100, 4),
        };

    /// <summary>
    /// { alignment, name, pvis:[[station, elevation],...], curve_lengths?:[L for each interior PVI], style?, label_set?, layer? }
    /// </summary>
    private static object CreateByPvis(JsonElement args)
    {
        var alName = args.GetRequiredString("alignment");
        var name = args.GetRequiredString("name");
        var pvis = Common.ReadPoints(args, "pvis").Select(p => (sta: p.X, el: p.Y)).OrderBy(p => p.sta).ToList();
        if (pvis.Count < 2) throw new ToolException("'pvis' needs at least 2 [station, elevation] pairs");
        var lengths = args.GetOptionalArray("curve_lengths").Select(e => e.GetDouble()).ToList();

        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var al = Helpers.RequireAlignment(tr, civ, alName);
        foreach (ObjectId pid in al.GetProfileIds())
            if (string.Equals(((Profile)tr.GetObject(pid, OpenMode.ForRead)).Name, name, StringComparison.OrdinalIgnoreCase))
                throw new ToolException($"profile '{name}' already exists on '{alName}'");

        var layerId = EnsureLayer(tr, doc.Database, args.GetOptionalString("layer") ?? "C-ROAD-PROF");
        var styleId = StyleTools.FindStyle(civ.Styles.ProfileStyles, args.GetOptionalString("style"));
        var labelId = StyleTools.FindStyle(civ.Styles.LabelSetStyles.ProfileLabelSetStyles, args.GetOptionalString("label_set"));
        var profId = Profile.CreateByLayout(name, al.ObjectId, layerId, styleId, labelId);
        var prof = (Profile)tr.GetObject(profId, OpenMode.ForWrite);

        for (var i = 0; i < pvis.Count - 1; i++)
            prof.Entities.AddFixedTangent(new Autodesk.AutoCAD.Geometry.Point2d(pvis[i].sta, pvis[i].el),
                                          new Autodesk.AutoCAD.Geometry.Point2d(pvis[i + 1].sta, pvis[i + 1].el));

        var curves = new List<object>();
        for (var k = 1; k < pvis.Count - 1 && k - 1 < lengths.Count; k++)
        {
            var L = lengths[k - 1];
            if (L <= 0) continue;
            var pvi = prof.PVIs.GetPVIAt(pvis[k].sta, pvis[k].el);
            var c = prof.Entities.AddFreeSymmetricParabolaByPVIAndCurveLength(pvi, L);
            curves.Add(new { pviStation = pvis[k].sta, length = L, curveType = c.CurveType.ToString() });
        }
        var res = new { alignment = alName, name, handle = HandleOf(prof), pviCount = pvis.Count, curves };
        tr.Commit();
        return res;
    }

    private static object DeletePvi(JsonElement args)
    {
        var alName = args.GetRequiredString("alignment");
        var pName = args.GetRequiredString("profile");
        var station = args.GetRequiredDouble("station");
        var tol = args.GetOptionalDouble("tolerance", 0.5);
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var p = ProfileTools.FindProfile(tr, civ, alName, pName);
        p.UpgradeOpen();
        ProfilePVI? hit = null;
        foreach (ProfilePVI v in p.PVIs)
            if (Math.Abs(v.RawStation - station) <= tol && (hit is null || Math.Abs(v.RawStation - station) < Math.Abs(hit.RawStation - station))) hit = v;
        if (hit is null) throw new ToolException($"no PVI within {tol} of station {station}");
        var removed = new { station = R(hit.RawStation), elevation = R(hit.Elevation) };
        p.PVIs.RemoveAt(hit.RawStation, hit.Elevation);
        tr.Commit();
        return new { alignment = alName, profile = pName, removed };
    }

    /// <summary>Elevation and grade every `interval` along a profile (for long-section tables).</summary>
    private static object SampleProfile(JsonElement args)
    {
        var alName = args.GetRequiredString("alignment");
        var pName = args.GetRequiredString("profile");
        var interval = args.GetOptionalDouble("interval", 20);
        if (interval <= 0) throw new ToolException("'interval' must be positive");
        var csvPath = args.GetOptionalString("csv_path");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var p = ProfileTools.FindProfile(tr, civ, alName, pName);
        var rows = new List<object[]>();
        var start = Math.Max(p.StartingStation, args.GetOptionalDouble("start_station", p.StartingStation));
        var end = Math.Min(p.EndingStation, args.GetOptionalDouble("end_station", p.EndingStation));
        for (var s = start; s <= end + 1e-6; s += interval)
        {
            var st = Math.Min(s, end);
            double? el = null, g = null;
            try { el = p.ElevationAt(st); } catch { }
            try { g = p.GradeAt(st) * 100; } catch { }
            rows.Add(new object[] { R(st), el is null ? null! : R(el.Value), g is null ? null! : R(g.Value, 4) });
        }
        tr.Commit();
        string? written = null;
        if (!string.IsNullOrWhiteSpace(csvPath))
        {
            written = Helpers.ResolvePath(doc, csvPath);
            WriteText(written, Csv(new[] { "station", "elevation", "grade_pct" }, rows));
        }
        return new
        {
            alignment = alName, profile = pName, interval, count = rows.Count,
            csvPath = written,
            samples = rows.Select(r => new { station = r[0], elevation = r[1], gradePct = r[2] }),
        };
    }
}
