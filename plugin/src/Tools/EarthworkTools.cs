using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Surface = Autodesk.Civil.DatabaseServices.Surface;
using static Civil3DMcpBridge.Common;

namespace Civil3DMcpBridge.Tools;

/// <summary>
/// Average-end-area earthworks straight from two surfaces along an alignment.
/// No sample line group, material list or QTO criteria needed - the tool
/// samples both surfaces across a swath at each station, integrates cut and
/// fill areas (splitting at grade crossings) and applies AEA between stations.
/// Gives an independent check on Civil 3D's QTO and a mass-haul ordinate.
/// </summary>
internal static class EarthworkTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_earthwork_by_sections", ByAea);
    }

    /// <summary>Cut and fill areas of one cross-section from paired samples (offset, existing, design).</summary>
    internal static (double cut, double fill, double footprint) SectionAreas(IReadOnlyList<(double o, double? e, double? d)> s)
    {
        double cut = 0, fill = 0, foot = 0;
        for (var i = 0; i < s.Count - 1; i++)
        {
            var a = s[i]; var b = s[i + 1];
            if (a.e is null || a.d is null || b.e is null || b.d is null) continue;
            var w = b.o - a.o;
            var h1 = a.d.Value - a.e.Value; // + fill, - cut
            var h2 = b.d.Value - b.e.Value;
            foot += w;
            if (h1 >= 0 && h2 >= 0) fill += w * (h1 + h2) / 2;
            else if (h1 <= 0 && h2 <= 0) cut += w * (-h1 - h2) / 2;
            else
            {
                var t = h1 / (h1 - h2); // crossing fraction
                var w1 = w * t; var w2 = w - w1;
                if (h1 > 0) { fill += w1 * h1 / 2; cut += w2 * -h2 / 2; }
                else { cut += w1 * -h1 / 2; fill += w2 * h2 / 2; }
            }
        }
        return (cut, fill, foot);
    }

    /// <summary>
    /// { alignment, existing_surface, design_surface, interval=20, left_width=20, right_width=20,
    ///   sample_step=0.5, start_station?, end_station?, stations?:[...],
    ///   fill_factor=1.0 (bulking/shrinkage applied to fill when computing mass haul),
    ///   strip_depth=0 (topsoil strip over the design footprint, m), csv_path? }
    /// </summary>
    private static object ByAea(JsonElement args)
    {
        var alName = args.GetRequiredString("alignment");
        var exName = args.GetRequiredString("existing_surface");
        var dsName = args.GetRequiredString("design_surface");
        var interval = args.GetOptionalDouble("interval", 20);
        var left = args.GetOptionalDouble("left_width", 20);
        var right = args.GetOptionalDouble("right_width", 20);
        var step = args.GetOptionalDouble("sample_step", 0.5);
        var fillFactor = args.GetOptionalDouble("fill_factor", 1.0);
        var strip = args.GetOptionalDouble("strip_depth", 0.0);
        if (interval <= 0 || step <= 0 || left < 0 || right < 0) throw new ToolException("interval, sample_step must be > 0 and widths >= 0");
        if ((left + right) / step > 4000) throw new ToolException("swath/sample_step gives > 4000 samples per section; increase sample_step");

        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var al = Helpers.RequireAlignment(tr, civ, alName);
        var ex = RequireSurface<Surface>(tr, civ, exName);
        var ds = RequireSurface<Surface>(tr, civ, dsName);

        var start = Math.Max(al.StartingStation, args.GetOptionalDouble("start_station", al.StartingStation));
        var end = Math.Min(al.EndingStation, args.GetOptionalDouble("end_station", al.EndingStation));
        var stations = args.GetOptionalArray("stations").Select(e => e.GetDouble()).Where(s => s >= start && s <= end).ToList();
        if (stations.Count == 0)
        {
            for (var s = start; s < end - 1e-6; s += interval) stations.Add(s);
            stations.Add(end);
        }
        stations = stations.Distinct().OrderBy(s => s).ToList();
        if (stations.Count > 20000) throw new ToolException("more than 20000 sections; increase interval");

        var sections = new List<(double st, double cut, double fill, double foot)>();
        foreach (var st in stations)
        {
            var samples = new List<(double, double?, double?)>();
            for (var o = -left; o <= right + 1e-9; o += step)
            {
                double e = 0, n = 0;
                al.PointLocation(st, o, ref e, ref n);
                samples.Add((o, TryElevation(ex, e, n), TryElevation(ds, e, n)));
            }
            var (c, f, w) = SectionAreas(samples);
            sections.Add((st, c, f, w));
        }
        tr.Commit();

        double totCut = 0, totFill = 0, totStrip = 0, mass = 0;
        var rows = new List<object?[]>();
        var table = new List<object>();
        for (var i = 0; i < sections.Count; i++)
        {
            double vc = 0, vf = 0, vs = 0;
            if (i > 0)
            {
                var d = sections[i].st - sections[i - 1].st;
                vc = d * (sections[i].cut + sections[i - 1].cut) / 2;
                vf = d * (sections[i].fill + sections[i - 1].fill) / 2;
                vs = d * strip * (sections[i].foot + sections[i - 1].foot) / 2;
            }
            totCut += vc; totFill += vf; totStrip += vs;
            mass += vc - vf * fillFactor;
            var sec = sections[i];
            rows.Add(new object?[] { R(sec.st), R(sec.cut), R(sec.fill), R(vc), R(vf), R(totCut), R(totFill), R(mass) });
            table.Add(new { station = R(sec.st), cutArea = R(sec.cut), fillArea = R(sec.fill), footprintWidth = R(sec.foot, 2), cutVolume = R(vc), fillVolume = R(vf), massOrdinate = R(mass) });
        }

        string? written = null;
        var csv = args.GetOptionalString("csv_path");
        if (!string.IsNullOrWhiteSpace(csv))
        {
            written = Helpers.ResolvePath(doc, csv);
            WriteText(written, Csv(new[] { "station", "cut_area_m2", "fill_area_m2", "cut_vol_m3", "fill_vol_m3", "cum_cut_m3", "cum_fill_m3", "mass_ordinate_m3" }, rows));
        }

        return new
        {
            alignment = alName, existingSurface = exName, designSurface = dsName,
            method = "average end area; areas integrated across swath with grade-crossing split",
            stationRange = new { start = R(start), end = R(end) }, sectionCount = sections.Count,
            swath = new { left, right, sampleStep = step }, fillFactor, stripDepth = strip,
            totals = new
            {
                cutVolume = R(totCut, 1), fillVolume = R(totFill, 1),
                adjustedFillVolume = R(totFill * fillFactor, 1),
                netVolume = R(totCut - totFill * fillFactor, 1),
                topsoilStripVolume = strip > 0 ? R(totStrip, 1) : (double?)null,
                units = "drawing units cubed (m3 for metric drawings)",
            },
            caveats = new[]
            {
                "Areas only count offsets where BOTH surfaces have elevations; check footprintWidth for gaps.",
                "AEA overstates volume on sharp curves (no curvature correction); use a smaller interval there.",
            },
            csvPath = written,
            sections = table,
        };
    }
}
