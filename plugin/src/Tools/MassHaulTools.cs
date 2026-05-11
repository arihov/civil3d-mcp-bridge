using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Section = Autodesk.Civil.DatabaseServices.Section;

namespace Civil3DMcpBridge.Tools;

/// <summary>
/// Mass haul / earthworks summary. The full Civil 3D MassHaulView API
/// (with free haul / overhaul distances and material movement diagrams)
/// is involved; this scaffold gives you the station-by-station cut/fill
/// summary that drives most quantities reporting, exported to CSV. Wire
/// in the full MassHaulView once you have the design profiles finalised.
/// </summary>
internal static class MassHaulTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_compute_quantity_takeoff", ComputeQuantityTakeoff);
        ToolRegistry.Register("civil3d_export_mass_haul_csv", ExportMassHaulCsv);
    }

    /// <summary>
    /// Compute a station-by-station volume between two surfaces along an
    /// alignment, using a sample line group's sections. Returns the
    /// running totals plus per-station cut/fill so the LLM can summarise
    /// or you can dump to CSV via civil3d_export_mass_haul_csv.
    ///
    /// Args:
    /// {
    ///   "alignment": "string",
    ///   "sample_line_group": "string",
    ///   "existing_surface": "string",
    ///   "design_surface": "string"
    /// }
    /// </summary>
    private static object ComputeQuantityTakeoff(JsonElement args)
    {
        // The Section/SampleLine quantity workflow in C3D 2026 requires the
        // GetSectionSources/GetMaterialSectionSources path on
        // SampleLineGroup and reads geometry off SectionPoint.Location
        // (world point) instead of the older SampleSectionPoints/Offset
        // pair. Stub until the new shape is properly modelled.
        throw new ToolException("compute_quantity_takeoff not yet ported to C3D 2026");
    }

    /// <summary>
    /// Same as compute_quantity_takeoff but writes a CSV directly. Useful
    /// for handing off to a quantities engineer or pasting into the ACP
    /// workbook.
    ///
    /// Args: same as compute_quantity_takeoff plus "csv_path": "string"
    /// </summary>
    private static object ExportMassHaulCsv(JsonElement args)
    {
        var csvPath = args.GetRequiredString("csv_path");
        var takeoff = (dynamic)ComputeQuantityTakeoff(args);

        using (var w = new StreamWriter(csvPath, append: false))
        {
            w.WriteLine("station,cut_interval_m3,fill_interval_m3,cum_cut_m3,cum_fill_m3,cum_net_m3");
            foreach (dynamic row in (IEnumerable<object>)takeoff.stations)
            {
                w.WriteLine($"{row.station:F3},{row.cutThisInterval:F3},{row.fillThisInterval:F3}," +
                            $"{row.cumulativeCut:F3},{row.cumulativeFill:F3},{row.cumulativeNet:F3}");
            }
        }
        return new
        {
            csvPath,
            totalCut = takeoff.totalCut,
            totalFill = takeoff.totalFill,
            netVolume = takeoff.netVolume,
            rows = ((IEnumerable<object>)takeoff.stations).GetEnumerator().MoveNext() ? "see csv" : "none",
        };
    }

    // ------------------------------------------------------------------

    private static (Alignment al, SampleLineGroup group, ObjectId existingId, ObjectId designId)
        ResolveInputs(Transaction tr, Autodesk.Civil.ApplicationServices.CivilDocument civDoc,
            string alignmentName, string groupName, string existing, string design)
    {
        var al = AlignmentTools.FindAlignmentByName(tr, civDoc, alignmentName)
            ?? throw new ToolException($"alignment '{alignmentName}' not found");

        SampleLineGroup? group = null;
        foreach (ObjectId gid in al.GetSampleLineGroupIds())
        {
            var g = (SampleLineGroup)tr.GetObject(gid, OpenMode.ForRead);
            if (string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase)) { group = g; break; }
        }
        if (group is null) throw new ToolException(
            $"sample line group '{groupName}' not found on alignment '{alignmentName}'");

        var existingId = Helpers.FindSurfaceId(tr, civDoc, existing)
            ?? throw new ToolException($"surface '{existing}' not found");
        var designId = Helpers.FindSurfaceId(tr, civDoc, design)
            ?? throw new ToolException($"surface '{design}' not found");

        return (al, group, existingId, designId);
    }

    /// <summary>
    /// Trapezoidal cross-section area between two sections. Positive when
    /// the design surface sits below existing (cut) or above (fill)
    /// depending on the <paramref name="cut"/> flag. This is an
    /// approximation; for production quantities use Civil 3D's built-in
    /// quantity takeoff with proper material/condition definitions.
    /// </summary>
    // ComputeCrossSectionArea + SectionExt removed: the C3D 2026 Section
    // API no longer exposes SampleSectionPoints / Offset / ElevationAt
    // directly. Re-introduce when the takeoff workflow is ported.
}
