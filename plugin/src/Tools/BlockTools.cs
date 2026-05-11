using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;

namespace Civil3DMcpBridge.Tools;

/// <summary>
/// Block-reference insertion along alignments. Most road furniture in
/// AutoCAD-based deliverables is represented by blocks (signs, kerb
/// stones, KM posts, lamp posts, drainage gully markers). This family
/// lets Claude place them at a chainage + offset with the right
/// rotation, individually or in batch.
/// </summary>
internal static class BlockTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_list_blocks", ListBlocks);
        ToolRegistry.Register("civil3d_insert_block_at_chainage", InsertBlockAtChainage);
        ToolRegistry.Register("civil3d_insert_blocks_batch", InsertBlocksBatch);
        ToolRegistry.Register("civil3d_create_signpost_at_chainage", CreateSignpostAtChainage);
        ToolRegistry.Register("civil3d_create_km_post_series", CreateKmPostSeries);
    }

    /// <summary>
    /// List block definitions in the drawing (skipping anonymous layout blocks).
    /// </summary>
    private static object ListBlocks(JsonElement args)
    {
        var filter = args.GetOptionalString("name_contains");
        var (doc, _) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        var results = new List<object>();
        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (btr.IsAnonymous || btr.IsLayout) continue;
            if (!string.IsNullOrEmpty(filter) &&
                btr.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            results.Add(new
            {
                name = btr.Name,
                comments = btr.Comments,
                isDynamic = btr.IsDynamicBlock,
            });
        }
        tr.Commit();
        return new { count = results.Count, blocks = results };
    }

    /// <summary>
    /// Insert a block reference at (alignment, station, offset) with a
    /// rotation either matching the alignment direction or perpendicular
    /// to it.
    ///
    /// Args:
    /// {
    ///   "block": "string" (block definition name),
    ///   "alignment": "string",
    ///   "station": number,
    ///   "offset": number (right-positive looking down-station),
    ///   "rotation": "along" | "perpendicular" | "absolute_degrees",
    ///   "absolute_degrees": number (only when rotation="absolute_degrees"),
    ///   "elevation": number (optional, default 0),
    ///   "profile_for_elevation": "string" (optional - if set, Z is taken from profile),
    ///   "scale": number (default 1.0),
    ///   "layer": "string" (default "0"),
    ///   "attributes": { "TAG1": "value", ... } (optional - sets block attribute values)
    /// }
    /// </summary>
    private static object InsertBlockAtChainage(JsonElement args)
    {
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var inserted = InsertOne(tr, doc.Database, civDoc, args);
        tr.Commit();
        return inserted;
    }

    /// <summary>
    /// Bulk version. Pass an "items" array where each item is the same
    /// shape as InsertBlockAtChainage args (minus the structural fields
    /// that can be specified at the top level as defaults).
    ///
    /// Args: { "items": [...], "alignment": "string" (default), "block": "string" (default), ... }
    /// </summary>
    private static object InsertBlocksBatch(JsonElement args)
    {
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var results = new List<object>();
        var errors = new List<object>();
        var idx = -1;

        foreach (var item in args.GetRequiredArray("items"))
        {
            idx++;
            // Merge defaults from top-level args.
            var merged = MergeArgs(args, item);
            try
            {
                results.Add(InsertOne(tr, doc.Database, civDoc, merged));
            }
            catch (Exception ex)
            {
                if (errors.Count < 20) errors.Add(new { index = idx, error = ex.Message });
            }
        }

        tr.Commit();
        return new { inserted = results.Count, errors = errors.Count, items = results, firstErrors = errors };
    }

    /// <summary>
    /// Opinionated wrapper for road signs. Defaults rotation to
    /// perpendicular (sign face toward oncoming traffic) and elevation
    /// from EG/FG profile if provided.
    /// </summary>
    private static object CreateSignpostAtChainage(JsonElement args)
    {
        // Synthesize the args for InsertBlockAtChainage from the signpost spec.
        var blockName = args.GetOptionalString("block") ?? "SIGN_GENERIC";
        var alignment = args.GetRequiredString("alignment");
        var station = args.GetRequiredDouble("station");
        var side = (args.GetOptionalString("side") ?? "right").ToLowerInvariant();
        var lateralOffset = args.GetOptionalDouble("offset", 3.0); // 3 m from CL is typical Ugandan shoulder
        var offset = side == "left" ? -lateralOffset : lateralOffset;
        var rotation = args.GetOptionalString("rotation") ?? "perpendicular";
        var profile = args.GetOptionalString("profile");
        var scale = args.GetOptionalDouble("scale", 1.0);
        var layer = args.GetOptionalString("layer") ?? "C-ROAD-SIGN";
        var legend = args.GetOptionalString("legend") ?? "";
        var signCode = args.GetOptionalString("sign_code") ?? "";

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        // Build a synthetic args element by serialising / reparsing.
        var payload = JsonSerializer.SerializeToElement(new
        {
            block = blockName,
            alignment,
            station,
            offset,
            rotation,
            profile_for_elevation = profile,
            scale,
            layer,
            attributes = new Dictionary<string, string>
            {
                ["LEGEND"] = legend,
                ["CODE"] = signCode,
                ["CHAINAGE"] = $"{station:F2}",
            },
        }, Json.Options);

        var result = InsertOne(tr, doc.Database, civDoc, payload);
        tr.Commit();
        return result;
    }

    /// <summary>
    /// Place kilometre / chainage posts at fixed station intervals along
    /// an alignment. Defaults to a 1000 m interval (KM posts) but accepts
    /// any spacing (e.g. 100 m for hectometre posts).
    ///
    /// Args:
    /// {
    ///   "alignment": "string",
    ///   "block": "string" (default "KM_POST"),
    ///   "interval": number (default 1000),
    ///   "side": "left" | "right" (default "right"),
    ///   "offset": number (default 3.0),
    ///   "layer": "string" (default "C-ROAD-KMPOST"),
    ///   "attribute_tag": "string" (default "KM") - written with the kilometre value,
    ///   "start_station": number (optional, default alignment start),
    ///   "end_station": number (optional)
    /// }
    /// </summary>
    private static object CreateKmPostSeries(JsonElement args)
    {
        var alignmentName = args.GetRequiredString("alignment");
        var blockName = args.GetOptionalString("block") ?? "KM_POST";
        var interval = args.GetOptionalDouble("interval", 1000.0);
        var side = (args.GetOptionalString("side") ?? "right").ToLowerInvariant();
        var lateralOffset = args.GetOptionalDouble("offset", 3.0);
        var offset = side == "left" ? -lateralOffset : lateralOffset;
        var layer = args.GetOptionalString("layer") ?? "C-ROAD-KMPOST";
        var attributeTag = args.GetOptionalString("attribute_tag") ?? "KM";

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = AlignmentTools.FindAlignmentByName(tr, civDoc, alignmentName)
            ?? throw new ToolException($"alignment '{alignmentName}' not found");

        var start = args.GetOptionalDouble("start_station", al.StartingStation);
        var end = args.GetOptionalDouble("end_station", al.EndingStation);

        // Round start up to next interval.
        var first = Math.Ceiling(start / interval) * interval;

        var placed = new List<object>();
        for (var s = first; s <= end + 1e-6; s += interval)
        {
            var kmValue = s / 1000.0;
            var payload = JsonSerializer.SerializeToElement(new
            {
                block = blockName,
                alignment = alignmentName,
                station = s,
                offset,
                rotation = "perpendicular",
                layer,
                attributes = new Dictionary<string, string>
                {
                    [attributeTag] = $"{kmValue:F1}",
                    ["STATION"] = $"{s:F2}",
                },
            }, Json.Options);

            try
            {
                placed.Add(InsertOne(tr, doc.Database, civDoc, payload));
            }
            catch (Exception ex)
            {
                placed.Add(new { station = s, error = ex.Message });
            }
        }

        tr.Commit();
        return new
        {
            alignment = alignmentName,
            block = blockName,
            placed = placed.Count,
            interval,
            items = placed,
        };
    }

    // ----------------------------------------------------------------

    /// <summary>
    /// Core single-block insertion. Used by all the public tools.
    /// </summary>
    private static object InsertOne(
        Transaction tr,
        Database db,
        Autodesk.Civil.ApplicationServices.CivilDocument civDoc,
        JsonElement args)
    {
        var blockName = args.GetRequiredString("block");
        var alignmentName = args.GetRequiredString("alignment");
        var station = args.GetRequiredDouble("station");
        var offset = args.GetOptionalDouble("offset", 0);
        var rotationMode = args.GetOptionalString("rotation") ?? "along";
        var absoluteDeg = args.GetOptionalDouble("absolute_degrees", 0);
        var elevation = args.GetOptionalDouble("elevation", 0);
        var profileName = args.GetOptionalString("profile_for_elevation");
        var scale = args.GetOptionalDouble("scale", 1.0);
        var layer = args.GetOptionalString("layer") ?? "0";

        var al = AlignmentTools.FindAlignmentByName(tr, civDoc, alignmentName)
            ?? throw new ToolException($"alignment '{alignmentName}' not found");
        GeometryHelpers.ValidateStation(al, station);

        // Compute insertion point.
        double easting = 0, northing = 0;
        al.PointLocation(station, offset, ref easting, ref northing);
        double z = elevation;
        if (!string.IsNullOrEmpty(profileName))
        {
            foreach (ObjectId pid in al.GetProfileIds())
            {
                var p = (Profile)tr.GetObject(pid, OpenMode.ForRead);
                if (string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase))
                {
                    try { z = p.ElevationAt(station); } catch { }
                    break;
                }
            }
        }
        var insertPoint = new Point3d(easting, northing, z);

        // Compute rotation.
        double rotationRad = rotationMode.ToLowerInvariant() switch
        {
            "along" => GeometryHelpers.AlongAlignmentRotation(al, station),
            "perpendicular" => GeometryHelpers.PerpendicularToAlignmentRotation(al, station),
            "absolute_degrees" => absoluteDeg * Math.PI / 180.0,
            _ => throw new ToolException($"unknown rotation mode: {rotationMode}"),
        };

        // Resolve block definition.
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (!bt.Has(blockName))
            throw new ToolException(
                $"block '{blockName}' not found in drawing. Insert the block " +
                "manually first (INSERT) or load it from your tool palette.");

        var blockId = bt[blockName];
        var ms = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

        var blockRef = new BlockReference(insertPoint, blockId)
        {
            Rotation = rotationRad,
            ScaleFactors = new Scale3d(scale, scale, scale),
            Layer = LayerOrDefault(tr, db, layer),
        };
        ms.AppendEntity(blockRef);
        tr.AddNewlyCreatedDBObject(blockRef, true);

        // Append attributes from the block definition.
        var btrDef = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
        if (btrDef.HasAttributeDefinitions)
        {
            // Collect attribute values from args.
            var attrMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (args.TryGetProperty("attributes", out var attrProp) && attrProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in attrProp.EnumerateObject())
                    attrMap[p.Name] = p.Value.ToString();
            }

            foreach (ObjectId entId in btrDef)
            {
                if (tr.GetObject(entId, OpenMode.ForRead) is not AttributeDefinition attDef) continue;
                if (attDef.Constant) continue;
                var attRef = new AttributeReference();
                attRef.SetAttributeFromBlock(attDef, blockRef.BlockTransform);
                if (attrMap.TryGetValue(attDef.Tag, out var value))
                    attRef.TextString = value;
                blockRef.AttributeCollection.AppendAttribute(attRef);
                tr.AddNewlyCreatedDBObject(attRef, true);
            }
        }

        return new
        {
            block = blockName,
            alignment = alignmentName,
            station,
            offset,
            easting,
            northing,
            elevation = z,
            rotationDegrees = rotationRad * 180.0 / Math.PI,
            handle = blockRef.Handle.Value.ToString("x"),
        };
    }

    private static string LayerOrDefault(Transaction tr, Database db, string layerName)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        if (lt.Has(layerName)) return layerName;
        // Create the layer on the fly with a reasonable default colour.
        var newLayer = new LayerTableRecord { Name = layerName };
        lt.Add(newLayer);
        tr.AddNewlyCreatedDBObject(newLayer, true);
        return layerName;
    }

    /// <summary>
    /// Merge a per-item arg object with the top-level defaults from the
    /// batch call: per-item values win.
    /// </summary>
    private static JsonElement MergeArgs(JsonElement defaults, JsonElement item)
    {
        var merged = new Dictionary<string, JsonElement>();
        foreach (var p in defaults.EnumerateObject())
        {
            if (p.NameEquals("items")) continue;
            merged[p.Name] = p.Value;
        }
        foreach (var p in item.EnumerateObject())
        {
            merged[p.Name] = p.Value;
        }
        return JsonSerializer.SerializeToElement(merged, Json.Options);
    }
}
