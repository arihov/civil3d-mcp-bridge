using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using static Civil3DMcpBridge.Common;

namespace Civil3DMcpBridge.Tools;

/// <summary>
/// Horizontal design: full entity geometry (radii, spirals, deflections),
/// PI-based alignment creation, station equations and superelevation read-out.
/// </summary>
internal static class AlignmentDesignTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_get_alignment_geometry_detail", GeometryDetail);
        ToolRegistry.Register("civil3d_create_alignment_by_pis", CreateByPis);
        ToolRegistry.Register("civil3d_add_station_equation", AddStationEquation);
        ToolRegistry.Register("civil3d_get_superelevation", GetSuperelevation);
        ToolRegistry.Register("civil3d_get_design_speeds", GetDesignSpeeds);
    }

    private static object Pt(Point2d p) => new { x = R(p.X), y = R(p.Y) };

    private static object GeometryDetail(JsonElement args)
    {
        var name = args.GetRequiredString("alignment");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var al = Helpers.RequireAlignment(tr, civ, name);

        var list = new List<object>();
        for (var i = 0; i < al.Entities.Count; i++)
        {
            var ent = al.Entities.GetEntityByOrder(i);
            for (var j = 0; j < ent.SubEntityCount; j++)
            {
                var sub = ent[j];
                switch (sub)
                {
                    case AlignmentSubEntityLine ln:
                        list.Add(new
                        {
                            entityId = ent.EntityId, order = i, kind = "tangent",
                            startStation = R(ln.StartStation), endStation = R(ln.EndStation), length = R(ln.Length),
                            directionDeg = R(ln.Direction * 180 / Math.PI, 6),
                            start = Pt(ln.StartPoint), end = Pt(ln.EndPoint),
                        });
                        break;
                    case AlignmentSubEntityArc arc:
                        list.Add(new
                        {
                            entityId = ent.EntityId, order = i, kind = "arc",
                            startStation = R(arc.StartStation), endStation = R(arc.EndStation), length = R(arc.Length),
                            radius = R(arc.Radius), deltaDeg = R(arc.Delta * 180 / Math.PI, 6),
                            clockwise = arc.Clockwise,
                            chordLength = R(arc.ChordLength),
                            center = Pt(arc.CenterPoint), start = Pt(arc.StartPoint), end = Pt(arc.EndPoint),
                        });
                        break;
                    case AlignmentSubEntitySpiral sp:
                        list.Add(new
                        {
                            entityId = ent.EntityId, order = i, kind = "spiral",
                            startStation = R(sp.StartStation), endStation = R(sp.EndStation), length = R(sp.Length),
                            radiusIn = double.IsInfinity(sp.RadiusIn) ? (double?)null : R(sp.RadiusIn),
                            radiusOut = double.IsInfinity(sp.RadiusOut) ? (double?)null : R(sp.RadiusOut),
                            a = R(sp.A), spiralDefinition = sp.SpiralDefinition.ToString(),
                            start = Pt(sp.StartPoint), end = Pt(sp.EndPoint),
                        });
                        break;
                    default:
                        list.Add(new
                        {
                            entityId = ent.EntityId, order = i, kind = sub.SubEntityType.ToString(),
                            startStation = R(sub.StartStation), endStation = R(sub.EndStation), length = R(sub.Length),
                        });
                        break;
                }
            }
        }

        var eqs = new List<object>();
        foreach (StationEquation eq in al.StationEquations)
            eqs.Add(new { rawStationBack = R(eq.RawStationBack), stationBack = R(eq.StationBack), stationAhead = R(eq.StationAhead), type = eq.EquationType.ToString() });

        tr.Commit();
        return new
        {
            alignment = al.Name, length = R(al.Length),
            startStation = R(al.StartingStation), endStation = R(al.EndingStation),
            elementCount = list.Count, elements = list, stationEquations = eqs,
        };
    }

    /// <summary>
    /// Create an alignment from a PI list with per-PI curve radii (and optional
    /// symmetric clothoid spiral lengths). Args:
    /// { name, pis:[[x,y],...], radii:[r1..r(n-2)], spiral_lengths?:[...], site?, style?, label_set?, layer? }
    /// </summary>
    private static object CreateByPis(JsonElement args)
    {
        var name = args.GetRequiredString("name");
        var pis = ReadPoints(args, "pis").ToList();
        if (pis.Count < 2) throw new ToolException("'pis' needs at least 2 points");
        var radii = args.GetOptionalArray("radii").Select(e => e.GetDouble()).ToList();
        var spirals = args.GetOptionalArray("spiral_lengths").Select(e => e.GetDouble()).ToList();
        var interior = pis.Count - 2;
        if (interior > 0 && radii.Count != interior)
            throw new ToolException($"'radii' must have {interior} values (one per interior PI); got {radii.Count}");

        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var db = doc.Database;

        if (Helpers.FindAlignmentId(tr, civ, name) is not null)
            throw new ToolException($"alignment '{name}' already exists");

        var layerId = EnsureLayer(tr, db, args.GetOptionalString("layer") ?? "C-ROAD-ALIGN");
        var siteName = args.GetOptionalString("site");
        var siteId = ObjectId.Null;
        if (!string.IsNullOrWhiteSpace(siteName))
        {
            foreach (ObjectId sid in civ.GetSiteIds())
            {
                var site = (Site)tr.GetObject(sid, OpenMode.ForRead);
                if (string.Equals(site.Name, siteName, StringComparison.OrdinalIgnoreCase)) { siteId = sid; break; }
            }
            if (siteId.IsNull) siteId = Site.Create(civ, siteName);
        }
        var styleId = StyleTools.FindStyle(civ.Styles.AlignmentStyles, args.GetOptionalString("style"));
        var labelSetId = StyleTools.FindStyle(civ.Styles.LabelSetStyles.AlignmentLabelSetStyles, args.GetOptionalString("label_set"));

        var alId = Alignment.Create(civ, name, siteId, layerId, styleId, labelSetId);
        var al = (Alignment)tr.GetObject(alId, OpenMode.ForWrite);

        var lineIds = new List<int>();
        for (var i = 0; i < pis.Count - 1; i++)
        {
            var ln = al.Entities.AddFixedLine(new Point3d(pis[i].X, pis[i].Y, 0), new Point3d(pis[i + 1].X, pis[i + 1].Y, 0));
            lineIds.Add(ln.EntityId);
        }

        var curves = new List<object>();
        for (var k = 0; k < interior; k++)
        {
            var r = radii[k];
            var ls = k < spirals.Count ? spirals[k] : 0;
            if (r <= 0) { curves.Add(new { pi = k + 1, skipped = "radius <= 0 (angle point)" }); continue; }
            if (ls > 0)
            {
                al.Entities.AddFreeSCS(lineIds[k], lineIds[k + 1], ls, ls,
                    SpiralParamType.Length, r, false, SpiralType.Clothoid);
                curves.Add(new { pi = k + 1, radius = r, spiralLength = ls });
            }
            else
            {
                al.Entities.AddFreeCurve(lineIds[k], lineIds[k + 1], r, CurveParamType.Radius, false, CurveType.Compound);
                curves.Add(new { pi = k + 1, radius = r });
            }
        }
        var result = new { name, handle = HandleOf(al), length = R(al.Length), tangents = lineIds.Count, curves };
        tr.Commit();
        return result;
    }

    private static object AddStationEquation(JsonElement args)
    {
        var name = args.GetRequiredString("alignment");
        var rawBack = args.GetRequiredDouble("raw_station_back");
        var ahead = args.GetRequiredDouble("station_ahead");
        var decreasing = args.GetOptionalBool("decreasing", false);
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var al = Helpers.RequireAlignment(tr, civ, name);
        al.UpgradeOpen();
        al.StationEquations.Add(rawBack, ahead,
            decreasing ? StationEquationType.Decreasing : StationEquationType.Increasing);
        tr.Commit();
        return new { alignment = name, rawStationBack = rawBack, stationAhead = ahead, decreasing };
    }

    private static object GetSuperelevation(JsonElement args)
    {
        var name = args.GetRequiredString("alignment");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var al = Helpers.RequireAlignment(tr, civ, name);

        var curves = new List<object>();
        foreach (SuperelevationCurve sc in al.SuperelevationCurves)
        {
            var stations = new List<object>();
            foreach (SuperelevationCriticalStation cs in sc.CriticalStations)
            {
                double? Slope(SuperelevationCrossSegmentType t)
                {
                    try { return R(cs.GetSlope(t) * 100, 3); } catch { return null; }
                }
                stations.Add(new
                {
                    station = R(cs.Station),
                    type = cs.StationType.ToString(),
                    leftOutsideLanePct = Slope(SuperelevationCrossSegmentType.LeftOutLaneCrossSlope),
                    rightOutsideLanePct = Slope(SuperelevationCrossSegmentType.RightOutLaneCrossSlope),
                    leftInsideLanePct = Slope(SuperelevationCrossSegmentType.LeftInLaneCrossSlope),
                    rightInsideLanePct = Slope(SuperelevationCrossSegmentType.RightInLaneCrossSlope),
                });
            }
            curves.Add(new { name = sc.Name, startStation = R(sc.StartStation), endStation = R(sc.EndStation), criticalStations = stations });
        }
        tr.Commit();
        return new { alignment = name, curveCount = curves.Count, curves, note = curves.Count == 0 ? "no superelevation calculated on this alignment" : null };
    }

    private static object GetDesignSpeeds(JsonElement args)
    {
        var name = args.GetRequiredString("alignment");
        var (doc, civ) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var al = Helpers.RequireAlignment(tr, civ, name);
        var list = new List<object>();
        foreach (DesignSpeed ds in al.DesignSpeeds)
            list.Add(new { station = R(ds.Station), speed = ds.Value, comment = ds.Comment });
        tr.Commit();
        return new { alignment = name, count = list.Count, designSpeeds = list };
    }
}
