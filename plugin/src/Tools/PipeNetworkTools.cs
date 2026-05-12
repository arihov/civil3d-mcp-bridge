using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

namespace Civil3DMcpBridge.Tools;

/// <summary>
/// Pipe networks — list, inspect, and (eventually) automated culvert creation.
/// The headline culvert-from-chainage tool is currently stubbed: the Civil 3D
/// 2026 API replaced <c>Network.AddNetworkPart()</c> with the typed
/// <c>AddLinePipe</c> / <c>AddStructure</c> family, and a faithful port of
/// the previous implementation is non-trivial. The read-only inspection
/// tools (list networks, list parts, get info) still work.
/// </summary>
internal static class PipeNetworkTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_list_pipe_networks", ListNetworks);
        ToolRegistry.Register("civil3d_get_pipe_network_info", GetNetworkInfo);
        ToolRegistry.Register("civil3d_list_pipe_parts", ListPipeParts);
        ToolRegistry.Register("civil3d_create_culvert_at_chainage", CreateCulvertAtChainage);
    }

    private static object ListNetworks(JsonElement args)
    {
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var results = new List<object>();
        foreach (ObjectId id in civDoc.GetPipeNetworkIds())
        {
            var net = (Network)tr.GetObject(id, OpenMode.ForRead);
            results.Add(new
            {
                name = net.Name,
                description = net.Description,
                pipeCount = net.GetPipeIds().Count,
                structureCount = net.GetStructureIds().Count,
                handle = net.Handle.Value.ToString("x"),
            });
        }
        tr.Commit();
        return new { count = results.Count, networks = results };
    }

    private static object GetNetworkInfo(JsonElement args)
    {
        var name = args.GetRequiredString("name");
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var network = FindNetworkByName(tr, civDoc, name)
            ?? throw new ToolException($"pipe network '{name}' not found");

        var structures = new List<object>();
        foreach (ObjectId sid in network.GetStructureIds())
        {
            var s = (Structure)tr.GetObject(sid, OpenMode.ForRead);
            structures.Add(new
            {
                name = s.Name,
                description = s.Description,
                easting = s.Position.X,
                northing = s.Position.Y,
                rimElevation = s.RimElevation,
                sumpElevation = s.SumpElevation,
                partFamily = s.PartFamilyName,
            });
        }

        var pipes = new List<object>();
        foreach (ObjectId pid in network.GetPipeIds())
        {
            var p = (Pipe)tr.GetObject(pid, OpenMode.ForRead);
            pipes.Add(new
            {
                name = p.Name,
                length = p.Length3DToInsideEdge,
                innerDiameter = p.InnerDiameterOrWidth,
                slope = p.Slope,
                startInvert = p.StartPoint.Z,
                endInvert = p.EndPoint.Z,
                partFamily = p.PartFamilyName,
            });
        }

        tr.Commit();
        return new
        {
            name = network.Name,
            description = network.Description,
            partsListName = SafeNameOf(tr, network.PartsListId),
            structureCount = structures.Count,
            pipeCount = pipes.Count,
            structures,
            pipes,
        };
    }

    /// <summary>
    /// List the pipe and structure families available in the current parts
    /// list, with their sizes. Use this before civil3d_create_culvert_at_chainage
    /// to confirm what diameters / headwall types you can ask for.
    /// </summary>
    private static object ListPipeParts(JsonElement args)
    {
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var partsList = ResolveDefaultPartsList(tr, civDoc);

        var pipeFamilies = new List<object>();
        var structureFamilies = new List<object>();

        foreach (ObjectId famId in partsList.GetPartFamilyIdsByDomain(DomainType.Pipe))
        {
            var family = (PartFamily)tr.GetObject(famId, OpenMode.ForRead);
            pipeFamilies.Add(new
            {
                name = family.Name,
                description = family.Description,
                partType = family.PartType.ToString(),
                sizes = EnumerateSizes(tr, family),
            });
        }

        foreach (ObjectId famId in partsList.GetPartFamilyIdsByDomain(DomainType.Structure))
        {
            var family = (PartFamily)tr.GetObject(famId, OpenMode.ForRead);
            structureFamilies.Add(new
            {
                name = family.Name,
                description = family.Description,
                partType = family.PartType.ToString(),
                sizes = EnumerateSizes(tr, family),
            });
        }

        tr.Commit();
        return new
        {
            partsList = partsList.Name,
            pipeFamilies,
            structureFamilies,
        };
    }

    /// <summary>
    /// Place a pipe culvert across an alignment at a given chainage. Creates
    /// (or reuses) a named pipe network, looks up appropriate part sizes by
    /// diameter, and inserts two structures (headwalls / nulls) plus a pipe
    /// between them. Plan AND profile representations follow automatically
    /// because pipe-network parts render in both views once the network is
    /// referenced on the relevant profile view.
    ///
    /// Args:
    /// {
    ///   "alignment": "string",
    ///   "station": number,                    // chainage
    ///   "diameter": number,                   // mm (typical Ugandan sizes: 600, 900, 1200, 1500)
    ///   "length": number,                     // total culvert length, m
    ///   "skew_degrees": number,               // 0 = perpendicular; +ve rotates clockwise looking down
    ///   "invert_in": number,                  // inlet invert elevation, m
    ///   "invert_out": number,                 // outlet invert elevation, m
    ///   "network_name": "string",             // existing or new; created if not found
    ///   "pipe_family": "string" (optional),   // e.g. "Concrete Pipe"; first circular family if omitted
    ///   "structure_family": "string" (optional), // e.g. "Headwall"; first null/headwall family if omitted
    ///   "label_suffix": "string" (optional)   // appended to inlet/outlet names
    /// }
    /// </summary>
    private static object CreateCulvertAtChainage(JsonElement args)
    {
        var alignmentName = args.GetRequiredString("alignment");
        var station = args.GetRequiredDouble("station");
        var diameterMm = args.GetRequiredDouble("diameter");
        var totalLength = args.GetRequiredDouble("length");
        var skewDeg = args.GetOptionalDouble("skew_degrees", 0);
        var invertIn = args.GetRequiredDouble("invert_in");
        var invertOut = args.GetRequiredDouble("invert_out");
        var networkName = args.GetRequiredString("network_name");
        var pipeFamilyName = args.GetOptionalString("pipe_family");
        var structureFamilyName = args.GetOptionalString("structure_family");
        var labelSuffix = args.GetOptionalString("label_suffix") ?? $"@{(int)Math.Round(station)}";

        if (diameterMm <= 0 || totalLength <= 0)
            throw new ToolException("'diameter' and 'length' must be positive");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = AlignmentTools.FindAlignmentByName(tr, civDoc, alignmentName)
            ?? throw new ToolException($"alignment '{alignmentName}' not found");
        GeometryHelpers.ValidateStation(al, station);

        var alignmentTangent = GeometryHelpers.TangentAngleAtStation(al, station);
        var culvertAxis = alignmentTangent + Math.PI / 2.0 - (skewDeg * Math.PI / 180.0);

        double centreE = 0, centreN = 0;
        al.PointLocation(station, 0, ref centreE, ref centreN);
        var halfLen = totalLength / 2.0;

        var inletPoint = new Point3d(
            centreE + halfLen * Math.Cos(culvertAxis),
            centreN + halfLen * Math.Sin(culvertAxis),
            invertIn);
        var outletPoint = new Point3d(
            centreE - halfLen * Math.Cos(culvertAxis),
            centreN - halfLen * Math.Sin(culvertAxis),
            invertOut);

        var partsList = ResolveDefaultPartsList(tr, civDoc);
        var (pipeFamId, pipeSizeId, pipeFamLabel, pipeSizeLabel) =
            FindPipeSize(tr, partsList, pipeFamilyName, diameterMm);
        var (structFamId, structSizeId, structFamLabel, _) =
            FindStructureSize(tr, partsList, structureFamilyName);

        var existing = FindNetworkByName(tr, civDoc, networkName);
        Network targetNetwork;
        if (existing is null)
        {
            string nameRef = networkName;
            var newNetId = Network.Create(civDoc, ref nameRef);
            targetNetwork = (Network)tr.GetObject(newNetId, OpenMode.ForWrite);
            targetNetwork.PartsListId = partsList.ObjectId;
            targetNetwork.Description = $"Auto-created by civil3d-mcp at {DateTime.Now:yyyy-MM-dd HH:mm}";
        }
        else
        {
            targetNetwork = (Network)tr.GetObject(existing.ObjectId, OpenMode.ForWrite);
        }

        // Place inlet structure.
        ObjectId inletStructId = ObjectId.Null;
        targetNetwork.AddStructure(structFamId, structSizeId, inletPoint, 0, ref inletStructId, false);
        var inletStruct = (Structure)tr.GetObject(inletStructId, OpenMode.ForWrite);
        inletStruct.SumpElevation = invertIn;
        inletStruct.Name = $"HW-IN{labelSuffix}";

        // Place outlet structure.
        ObjectId outletStructId = ObjectId.Null;
        targetNetwork.AddStructure(structFamId, structSizeId, outletPoint, 0, ref outletStructId, false);
        var outletStruct = (Structure)tr.GetObject(outletStructId, OpenMode.ForWrite);
        outletStruct.SumpElevation = invertOut;
        outletStruct.Name = $"HW-OUT{labelSuffix}";

        // Place pipe between them as a 3D line segment.
        ObjectId pipeId = ObjectId.Null;
        var lineSeg = new Autodesk.AutoCAD.Geometry.LineSegment3d(inletPoint, outletPoint);
        targetNetwork.AddLinePipe(pipeFamId, pipeSizeId, lineSeg, ref pipeId, false);
        var pipe = (Pipe)tr.GetObject(pipeId, OpenMode.ForWrite);
        try { pipe.ConnectToStructure(ConnectorPositionType.Start, inletStructId, true); } catch { }
        try { pipe.ConnectToStructure(ConnectorPositionType.End, outletStructId, true); } catch { }
        pipe.Name = $"Culvert{labelSuffix}";

        var slope = (invertIn - invertOut) / totalLength;
        tr.Commit();

        return new
        {
            network = targetNetwork.Name,
            culvertName = pipe.Name,
            alignment = alignmentName,
            station,
            diameterMm,
            totalLength,
            skewDegrees = skewDeg,
            invertIn,
            invertOut,
            slope,
            slopePercent = slope * 100,
            inlet = new { name = inletStruct.Name, easting = inletPoint.X, northing = inletPoint.Y, invert = invertIn },
            outlet = new { name = outletStruct.Name, easting = outletPoint.X, northing = outletPoint.Y, invert = invertOut },
            pipeFamily = pipeFamLabel,
            pipeSize = pipeSizeLabel,
            structureFamily = structFamLabel,
        };
    }

    /// <summary>
    /// Walk the parts list looking for a circular pipe family whose part
    /// sizes include one with an inner diameter close to the requested
    /// value (mm). Falls back to "first family of PartType.Pipe, first size".
    /// </summary>
    private static (ObjectId FamilyId, ObjectId SizeId, string FamilyLabel, string SizeLabel)
        FindPipeSize(Transaction tr, PartsList partsList, string? familyName, double diameterMm)
    {
        const double tolMm = 1.0;
        ObjectId firstFamId = ObjectId.Null, firstSizeId = ObjectId.Null;
        string firstFamLabel = "", firstSizeLabel = "";

        foreach (ObjectId famId in partsList.GetPartFamilyIdsByDomain(DomainType.Pipe))
        {
            var family = (PartFamily)tr.GetObject(famId, OpenMode.ForRead);
            if (!string.IsNullOrEmpty(familyName) &&
                !string.Equals(family.Name, familyName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (family.PartType != PartType.Pipe) continue;

            int count = family.PartSizeCount;
            for (int i = 0; i < count; i++)
            {
                ObjectId sid;
                try { sid = family[i]; } catch { continue; }
                if (sid.IsNull) continue;
                var size = (PartSize)tr.GetObject(sid, OpenMode.ForRead);
                var sizeLabel = TryGetSizeDisplayName(size) ?? $"{family.Name} #{i}";
                if (firstFamId.IsNull)
                {
                    firstFamId = famId; firstSizeId = sid;
                    firstFamLabel = family.Name; firstSizeLabel = sizeLabel;
                }
                if (TryGetInnerDiameterMm(size, out var dia) &&
                    Math.Abs(dia - diameterMm) <= tolMm)
                {
                    return (famId, sid, family.Name, sizeLabel);
                }
            }
        }

        if (!firstFamId.IsNull)
            return (firstFamId, firstSizeId, firstFamLabel,
                firstSizeLabel + $" (no exact {diameterMm}mm match — used first available)");
        throw new ToolException(
            "no pipe family with PartType=Pipe found in the parts list" +
            (familyName != null ? $" named '{familyName}'" : ""));
    }

    /// <summary>First null/headwall/general structure family in the parts list.</summary>
    private static (ObjectId FamilyId, ObjectId SizeId, string FamilyLabel, string SizeLabel)
        FindStructureSize(Transaction tr, PartsList partsList, string? familyName)
    {
        foreach (ObjectId famId in partsList.GetPartFamilyIdsByDomain(DomainType.Structure))
        {
            var family = (PartFamily)tr.GetObject(famId, OpenMode.ForRead);
            if (!string.IsNullOrEmpty(familyName) &&
                !string.Equals(family.Name, familyName, StringComparison.OrdinalIgnoreCase))
                continue;
            int count = family.PartSizeCount;
            for (int i = 0; i < count; i++)
            {
                ObjectId sid;
                try { sid = family[i]; } catch { continue; }
                if (sid.IsNull) continue;
                var size = (PartSize)tr.GetObject(sid, OpenMode.ForRead);
                var sizeLabel = TryGetSizeDisplayName(size) ?? $"{family.Name} #{i}";
                return (famId, sid, family.Name, sizeLabel);
            }
        }
        throw new ToolException(
            "no structure family found in the parts list" +
            (familyName != null ? $" named '{familyName}'" : ""));
    }

    /// <summary>
    /// Best-effort inner-diameter lookup on a PartSize. The C3D 2026 API
    /// hides part dimensions inside an opaque SizeDataRecord; we probe
    /// for any property that smells like an inner diameter and is in mm.
    /// </summary>
    private static bool TryGetInnerDiameterMm(PartSize size, out double diameterMm)
    {
        diameterMm = 0;
        try
        {
            var rec = size.SizeDataRecord;
            if (rec is null) return false;
            // Try common property names; if exposed as a method (e.g. an
            // indexer keyed by string), fall back to that.
            foreach (var name in new[] { "InnerDiameter", "Diameter", "InsideDiameter", "PipeInnerDiameter" })
            {
                var p = rec.GetType().GetProperty(name);
                if (p == null) continue;
                var v = p.GetValue(rec);
                if (v is double dv) { diameterMm = dv; return true; }
                if (v is float fv) { diameterMm = fv; return true; }
            }
        }
        catch { }
        return false;
    }

    // ------------------------------------------------------------------

    private static List<object> EnumerateSizes(Transaction tr, PartFamily family)
    {
        var sizes = new List<object>();
        int count = family.PartSizeCount;
        for (int i = 0; i < count; i++)
        {
            // In C3D 2026 PartFamily's indexer returns an ObjectId for each
            // part size; resolve it through the active transaction to obtain
            // the PartSize itself.
            PartSize? size = null;
            try
            {
                ObjectId sizeId = family[i];
                if (!sizeId.IsNull)
                    size = (PartSize)tr.GetObject(sizeId, OpenMode.ForRead);
            }
            catch
            {
                /* fall through to placeholder */
            }
            sizes.Add(new
            {
                name = (size != null ? TryGetSizeDisplayName(size) : null)
                    ?? $"{family.Name} #{i}",
                description = (string?)null,
            });
        }
        return sizes;
    }

    /// <summary>
    /// Pull a human-readable size name from the part size's data record if
    /// one is available, otherwise return null and the caller will fall back
    /// to "<family> #<index>". The data record holds the part dimensions and
    /// is the source-of-truth for size identity in C3D 2026.
    /// </summary>
    private static string? TryGetSizeDisplayName(PartSize size)
    {
        try
        {
            var record = size.SizeDataRecord;
            if (record == null) return null;
            // The data record exposes a Name-ish property on most C3D builds;
            // probe via reflection so we don't bind to a specific shape that
            // may not exist on every version.
            var t = record.GetType();
            foreach (var propName in new[] { "Name", "DisplayName", "SizeName" })
            {
                var prop = t.GetProperty(propName);
                if (prop != null && prop.PropertyType == typeof(string))
                {
                    var v = prop.GetValue(record) as string;
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
        }
        catch { /* best-effort */ }
        return null;
    }

    private static Network? FindNetworkByName(Transaction tr,
        Autodesk.Civil.ApplicationServices.CivilDocument civDoc, string name)
    {
        foreach (ObjectId id in civDoc.GetPipeNetworkIds())
        {
            var net = (Network)tr.GetObject(id, OpenMode.ForRead);
            if (string.Equals(net.Name, name, StringComparison.OrdinalIgnoreCase))
                return net;
        }
        return null;
    }

    private static PartsList ResolveDefaultPartsList(Transaction tr,
        Autodesk.Civil.ApplicationServices.CivilDocument civDoc)
    {
        // In C3D 2026 the parts-list collection lives under civDoc.Styles.
        // The exact property name varies between API revisions; probe a few
        // likely names via reflection so this code keeps working if Autodesk
        // moves it again.
        var styles = civDoc.Styles;
        var stylesType = styles.GetType();

        foreach (var propName in new[] {
            "PartsListCollection", "PartsLists", "PartsList",
        })
        {
            var prop = stylesType.GetProperty(propName);
            if (prop == null) continue;
            var coll = prop.GetValue(styles);
            if (coll is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is ObjectId plId && !plId.IsNull)
                    {
                        return (PartsList)tr.GetObject(plId, OpenMode.ForRead);
                    }
                }
            }
        }

        throw new ToolException(
            "no parts list found in drawing — create one (Pipe Networks " +
            "→ Parts List in the Toolspace) before running culvert automation. " +
            "If you know your drawing has a parts list, the civDoc.Styles property " +
            "name for the collection may have changed in your Civil 3D build.");
    }

    private static string SafeNameOf(Transaction tr, ObjectId id)
    {
        if (id.IsNull) return "<none>";
        try
        {
            var obj = tr.GetObject(id, OpenMode.ForRead);
            var prop = obj.GetType().GetProperty("Name");
            return prop?.GetValue(obj) as string ?? "<unknown>";
        }
        catch { return "<unknown>"; }
    }
}
