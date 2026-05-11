using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

namespace Civil3DMcpBridge.Tools;

internal static class CorridorTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_list_corridors", ListCorridors);
        ToolRegistry.Register("civil3d_get_corridor_info", GetCorridorInfo);
        ToolRegistry.Register("civil3d_rebuild_corridor", RebuildCorridor);
        ToolRegistry.Register("civil3d_set_region_assembly", SetRegionAssembly);
        ToolRegistry.Register("civil3d_create_corridor", CreateCorridor);
        ToolRegistry.Register("civil3d_add_baseline_region", AddBaselineRegion);
        ToolRegistry.Register("civil3d_set_region_target_surface", SetRegionTargetSurface);
        ToolRegistry.Register("civil3d_list_assemblies", ListAssemblies);
        ToolRegistry.Register("civil3d_export_corridor_sections", ExportCorridorSections);
    }

    // ---- Helpers -----------------------------------------------------------

    private static object ListAssemblies(JsonElement args)
    {
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var results = new List<object>();
        foreach (ObjectId id in civDoc.AssemblyCollection)
        {
            var a = (Assembly)tr.GetObject(id, OpenMode.ForRead);
            results.Add(new
            {
                name = a.Name,
                description = a.Description,
                handle = a.Handle.Value.ToString("x"),
            });
        }
        tr.Commit();
        return new { count = results.Count, assemblies = results };
    }

    // ---- Inspection --------------------------------------------------------

    private static object ListCorridors(JsonElement args)
    {
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var results = new List<object>();
        foreach (ObjectId cid in civDoc.CorridorCollection)
        {
            var c = (Corridor)tr.GetObject(cid, OpenMode.ForRead);
            results.Add(new
            {
                name = c.Name,
                description = c.Description,
                baselineCount = c.Baselines.Count,
                isOutOfDate = c.IsOutOfDate,
                handle = c.Handle.Value.ToString("x"),
            });
        }
        tr.Commit();
        return new { count = results.Count, corridors = results };
    }

    private static object GetCorridorInfo(JsonElement args)
    {
        var name = args.GetRequiredString("name");
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var corridorId = Helpers.FindCorridorId(tr, civDoc, name)
            ?? throw new ToolException($"corridor '{name}' not found");
        var corridor = (Corridor)tr.GetObject(corridorId, OpenMode.ForRead);

        var baselines = new List<object>();
        for (var bi = 0; bi < corridor.Baselines.Count; bi++)
        {
            var baseline = corridor.Baselines[bi];
            string alignmentName = "<unknown>";
            try { alignmentName = ((Alignment)tr.GetObject(baseline.AlignmentId, OpenMode.ForRead)).Name; }
            catch { /* could be feature-line based */ }

            string profileName = "<none>";
            if (!baseline.ProfileId.IsNull)
                try { profileName = ((Profile)tr.GetObject(baseline.ProfileId, OpenMode.ForRead)).Name; }
                catch { /* swallow */ }

            var regions = new List<object>();
            for (var ri = 0; ri < baseline.BaselineRegions.Count; ri++)
            {
                var region = baseline.BaselineRegions[ri];
                var assyName = "<unknown>";
                try { assyName = ((Assembly)tr.GetObject(region.AssemblyId, OpenMode.ForRead)).Name; }
                catch { /* swallow */ }
                regions.Add(new
                {
                    index = ri,
                    name = region.Name,
                    startStation = region.StartStation,
                    endStation = region.EndStation,
                    assembly = assyName,
                });
            }
            baselines.Add(new
            {
                index = bi,
                name = baseline.Name,
                alignment = alignmentName,
                profile = profileName,
                startStation = baseline.StartStation,
                endStation = baseline.EndStation,
                regionCount = baseline.BaselineRegions.Count,
                regions,
            });
        }

        tr.Commit();
        return new
        {
            name = corridor.Name,
            description = corridor.Description,
            isOutOfDate = corridor.IsOutOfDate,
            baselineCount = corridor.Baselines.Count,
            baselines,
        };
    }

    // ---- Mutation ----------------------------------------------------------

    private static object RebuildCorridor(JsonElement args)
    {
        var name = args.GetRequiredString("name");
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var corridorId = Helpers.FindCorridorId(tr, civDoc, name)
            ?? throw new ToolException($"corridor '{name}' not found");
        var corridor = (Corridor)tr.GetObject(corridorId, OpenMode.ForWrite);
        corridor.Rebuild();
        tr.Commit();
        return new { name = corridor.Name, rebuilt = true, isOutOfDate = corridor.IsOutOfDate };
    }

    private static object SetRegionAssembly(JsonElement args)
    {
        var corridorName = args.GetRequiredString("corridor");
        var baselineIndex = args.GetOptionalInt("baseline_index", -1);
        var regionIndex = args.GetOptionalInt("region_index", -1);
        var assemblyName = args.GetRequiredString("assembly");
        var rebuild = !args.TryGetProperty("rebuild", out var r) || r.ValueKind != JsonValueKind.False;

        if (baselineIndex < 0) throw new ToolException("'baseline_index' required (>= 0)");
        if (regionIndex < 0) throw new ToolException("'region_index' required (>= 0)");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var corridorId = Helpers.FindCorridorId(tr, civDoc, corridorName)
            ?? throw new ToolException($"corridor '{corridorName}' not found");
        var corridor = (Corridor)tr.GetObject(corridorId, OpenMode.ForWrite);

        if (baselineIndex >= corridor.Baselines.Count)
            throw new ToolException(
                $"baseline_index {baselineIndex} out of range (corridor has {corridor.Baselines.Count} baselines)");
        var baseline = corridor.Baselines[baselineIndex];
        if (regionIndex >= baseline.BaselineRegions.Count)
            throw new ToolException(
                $"region_index {regionIndex} out of range (baseline has {baseline.BaselineRegions.Count} regions)");

        var assyId = Helpers.FindAssemblyId(tr, civDoc, assemblyName)
            ?? throw new ToolException($"assembly '{assemblyName}' not found");

        var region = baseline.BaselineRegions[regionIndex];
        region.AssemblyId = assyId;
        if (rebuild) corridor.Rebuild();
        tr.Commit();

        return new
        {
            corridor = corridorName,
            baselineIndex,
            regionIndex,
            newAssembly = assemblyName,
            rebuilt = rebuild,
        };
    }

    /// <summary>
    /// Create a new corridor with one baseline + one region by default.
    /// Args:
    /// { "name": "string", "alignment": "string", "profile": "string",
    ///   "assembly": "string", "start_station": n?, "end_station": n? }
    /// </summary>
    private static object CreateCorridor(JsonElement args)
    {
        var name = args.GetRequiredString("name");
        var alignmentName = args.GetRequiredString("alignment");
        var profileName = args.GetRequiredString("profile");
        var assemblyName = args.GetRequiredString("assembly");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = Helpers.RequireAlignment(tr, civDoc, alignmentName);
        var profile = ProfileTools.FindProfile(tr, civDoc, alignmentName, profileName);
        var assyId = Helpers.FindAssemblyId(tr, civDoc, assemblyName)
            ?? throw new ToolException($"assembly '{assemblyName}' not found");

        var corridorId = civDoc.CorridorCollection.Add(name);
        var corridor = (Corridor)tr.GetObject(corridorId, OpenMode.ForWrite);

        var bl = corridor.Baselines.Add(name + "-BL1", al.ObjectId, profile.ObjectId);

        var startStation = args.GetOptionalDouble("start_station", al.StartingStation);
        var endStation = args.GetOptionalDouble("end_station", al.EndingStation);
        bl.BaselineRegions.Add("Region-1", assyId, startStation, endStation);

        corridor.Rebuild();
        tr.Commit();

        return new
        {
            name = corridor.Name,
            baseline = name + "-BL1",
            alignment = alignmentName,
            profile = profileName,
            assembly = assemblyName,
            startStation,
            endStation,
            handle = corridor.Handle.Value.ToString("x"),
        };
    }

    /// <summary>
    /// Append an extra region to an existing baseline. Useful for splitting
    /// a corridor by chainage so different assemblies can apply.
    /// </summary>
    private static object AddBaselineRegion(JsonElement args)
    {
        var corridorName = args.GetRequiredString("corridor");
        var baselineIndex = args.GetOptionalInt("baseline_index", 0);
        var regionName = args.GetRequiredString("region_name");
        var assemblyName = args.GetRequiredString("assembly");
        var startStation = args.GetRequiredDouble("start_station");
        var endStation = args.GetRequiredDouble("end_station");
        var rebuild = !args.TryGetProperty("rebuild", out var r) || r.ValueKind != JsonValueKind.False;

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var corridorId = Helpers.FindCorridorId(tr, civDoc, corridorName)
            ?? throw new ToolException($"corridor '{corridorName}' not found");
        var corridor = (Corridor)tr.GetObject(corridorId, OpenMode.ForWrite);
        var bl = corridor.Baselines[baselineIndex];

        var assyId = Helpers.FindAssemblyId(tr, civDoc, assemblyName)
            ?? throw new ToolException($"assembly '{assemblyName}' not found");

        bl.BaselineRegions.Add(regionName, assyId, startStation, endStation);
        if (rebuild) corridor.Rebuild();
        tr.Commit();

        return new
        {
            corridor = corridorName,
            baselineIndex,
            regionName,
            assembly = assemblyName,
            startStation,
            endStation,
        };
    }

    /// <summary>
    /// Wire a target surface (e.g. existing ground) into a region so daylight
    /// subassemblies can find it. Civil 3D normally requires this step
    /// before a corridor model will build correctly.
    /// </summary>
    private static object SetRegionTargetSurface(JsonElement args)
    {
        var corridorName = args.GetRequiredString("corridor");
        var baselineIndex = args.GetOptionalInt("baseline_index", 0);
        var regionIndex = args.GetOptionalInt("region_index", 0);
        var surfaceName = args.GetRequiredString("surface");
        var rebuild = !args.TryGetProperty("rebuild", out var r) || r.ValueKind != JsonValueKind.False;

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var corridorId = Helpers.FindCorridorId(tr, civDoc, corridorName)
            ?? throw new ToolException($"corridor '{corridorName}' not found");
        var corridor = (Corridor)tr.GetObject(corridorId, OpenMode.ForWrite);

        var bl = corridor.Baselines[baselineIndex];
        var region = bl.BaselineRegions[regionIndex];
        var surfaceId = Helpers.FindSurfaceId(tr, civDoc, surfaceName)
            ?? throw new ToolException($"surface '{surfaceName}' not found");

        var targets = region.GetTargets();
        var surfaceTargets = new List<SubassemblyTargetInfo>();
        foreach (SubassemblyTargetInfo info in targets)
        {
            if (info.TargetType == SubassemblyLogicalNameType.Surface)
                surfaceTargets.Add(info);
        }
        if (surfaceTargets.Count == 0)
            throw new ToolException(
                "no surface targets exist on this region's subassemblies — " +
                "check the assembly contains a daylight/grading subassembly");

        // Assign the surface to every surface-typed target on the region.
        int matched = 0;
        foreach (var t in surfaceTargets)
        {
            var ids = new ObjectIdCollection { surfaceId };
            t.TargetIds = ids;
            matched++;
        }
        region.SetTargets(targets);

        if (rebuild) corridor.Rebuild();
        tr.Commit();
        return new
        {
            corridor = corridorName,
            baselineIndex,
            regionIndex,
            surface = surfaceName,
            targetsBound = matched,
            rebuilt = rebuild,
        };
    }

    /// <summary>
    /// Export every corridor cross-section point to CSV. Iterates each
    /// baseline and corridor station, then enumerates the calculated
    /// shape points for each subassembly side so a downstream tool
    /// (or a human) can compute earthworks/quantities.
    ///
    /// Args:
    /// {
    ///   "corridor": "string",
    ///   "output_path": "string (.csv)",
    ///   "baseline_index": int? (default 0; -1 for all baselines),
    ///   "station_interval": number? (default = corridor frequency)
    /// }
    /// </summary>
    private static object ExportCorridorSections(JsonElement args)
    {
        // Baseline.CalculatedStationList does not exist in C3D 2026; the
        // replacement API (AppliedAssembly / GetAppliedAssemblyAtStation)
        // exposes calculated points per station but has a different shape.
        // Stub until that path is mapped.
        throw new ToolException("export_corridor_sections not ported to C3D 2026 API");
    }
}
