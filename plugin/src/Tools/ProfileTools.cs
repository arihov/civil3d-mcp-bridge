using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

namespace Civil3DMcpBridge.Tools;

internal static class ProfileTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_list_profiles", ListProfiles);
        ToolRegistry.Register("civil3d_get_profile_info", GetProfileInfo);
        ToolRegistry.Register("civil3d_add_vertical_curve", AddVerticalCurve);
        ToolRegistry.Register("civil3d_create_profile_view", CreateProfileView);
        ToolRegistry.Register("civil3d_get_elevation_at_station", GetElevationAtStation);
        // create_profile_from_surface, create_layout_profile, add_pvi
        // are registered by ProfileEditTools.
    }

    // ---- Inspection ---------------------------------------------------------

    private static object ListProfiles(JsonElement args)
    {
        var alignmentName = args.GetRequiredString("alignment");
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = Helpers.RequireAlignment(tr, civDoc, alignmentName);
        var results = new List<object>();
        foreach (ObjectId pid in al.GetProfileIds())
        {
            var p = (Profile)tr.GetObject(pid, OpenMode.ForRead);
            results.Add(new
            {
                name = p.Name,
                profileType = p.ProfileType.ToString(),
                description = p.Description,
                style = p.StyleName,
                minStation = p.StartingStation,
                maxStation = p.EndingStation,
                minElevation = p.ElevationMin,
                maxElevation = p.ElevationMax,
                entityCount = p.Entities.Count,
                pviCount = p.PVIs.Count,
            });
        }
        tr.Commit();
        return new { alignment = al.Name, count = results.Count, profiles = results };
    }

    private static object GetProfileInfo(JsonElement args)
    {
        var alignmentName = args.GetRequiredString("alignment");
        var profileName = args.GetRequiredString("profile");
        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var p = FindProfile(tr, civDoc, alignmentName, profileName);

        var entities = new List<object>();
        foreach (ProfileEntity ent in p.Entities)
        {
            double grade = ent is ProfileTangent t ? t.Grade : 0;
            entities.Add(new
            {
                entityId = ent.EntityId,
                type = ent.EntityType.ToString(),
                startStation = ent.StartStation,
                endStation = ent.EndStation,
                startElevation = ent.StartElevation,
                endElevation = ent.EndElevation,
                length = ent.Length,
                grade,
            });
        }

        var pvis = new List<object>();
        foreach (ProfilePVI pvi in p.PVIs)
        {
            pvis.Add(new
            {
                station = pvi.Station,
                elevation = pvi.Elevation,
                gradeIn = pvi.GradeIn,
                gradeOut = pvi.GradeOut,
                pviType = pvi.PVIType.ToString(),
            });
        }

        tr.Commit();
        return new
        {
            alignment = alignmentName,
            name = p.Name,
            profileType = p.ProfileType.ToString(),
            startStation = p.StartingStation,
            endStation = p.EndingStation,
            elevationMin = p.ElevationMin,
            elevationMax = p.ElevationMax,
            entities,
            pvis,
        };
    }

    /// <summary>Sample station + elevation off a profile (e.g. for placing culvert IL).</summary>
    private static object GetElevationAtStation(JsonElement args)
    {
        var alignmentName = args.GetRequiredString("alignment");
        var profileName = args.GetRequiredString("profile");
        var station = args.GetRequiredDouble("station");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var p = FindProfile(tr, civDoc, alignmentName, profileName);
        double elevation;
        try
        {
            elevation = p.ElevationAt(station);
        }
        catch (Exception ex)
        {
            throw new ToolException($"could not sample elevation at station {station}: {ex.Message}");
        }

        tr.Commit();
        return new { alignment = alignmentName, profile = profileName, station, elevation };
    }

    // ---- Vertical curve insertion ------------------------------------------

    private static object AddVerticalCurve(JsonElement args)
    {
        // ProfileEntityCollection.AddFixedParabolaByLength does not exist in
        // C3D 2026 — the symmetric/asymmetric parabola factories take
        // different parameters. Stub until the new API is mapped.
        throw new ToolException("add_vertical_curve not ported to C3D 2026 API");
    }


    /// <summary>
    /// Create a profile view (band) for an alignment at a given XY origin.
    /// Args: { "alignment": "string", "view_name": "string", "x": n, "y": n,
    ///         "style": "string"?, "band_set": "string"? }
    /// </summary>
    private static object CreateProfileView(JsonElement args)
    {
        var alignmentName = args.GetRequiredString("alignment");
        var viewName = args.GetRequiredString("view_name");
        var x = args.GetRequiredDouble("x");
        var y = args.GetRequiredDouble("y");
        var styleName = args.GetOptionalString("style") ?? "Standard";
        var bandSetName = args.GetOptionalString("band_set") ?? "Standard";

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = Helpers.RequireAlignment(tr, civDoc, alignmentName);
        var styleId = ResolveProfileViewStyle(tr, civDoc, styleName);
        var bandSetId = ResolveProfileViewBandSet(tr, civDoc, bandSetName);

        var pvId = ProfileView.Create(
            al.ObjectId,
            new Point3d(x, y, 0),
            viewName,
            bandSetId,
            styleId);

        var pv = (ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
        tr.Commit();
        return new
        {
            name = pv.Name,
            alignment = alignmentName,
            origin = new { x, y },
            handle = pv.Handle.Value.ToString("x"),
        };
    }

    // ---- Lookups -----------------------------------------------------------

    internal static Profile FindProfile(Transaction tr, CivilDocument civDoc, string alignmentName, string profileName)
    {
        var al = Helpers.RequireAlignment(tr, civDoc, alignmentName);
        foreach (ObjectId pid in al.GetProfileIds())
        {
            var p = (Profile)tr.GetObject(pid, OpenMode.ForRead);
            if (string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        throw new ToolException($"profile '{profileName}' not found on alignment '{alignmentName}'");
    }

    private static ObjectId ResolveProfileStyle(Transaction tr, CivilDocument civDoc, string name)
    {
        foreach (ObjectId id in civDoc.Styles.ProfileStyles)
        {
            var s = (Autodesk.Civil.DatabaseServices.Styles.ProfileStyle)tr.GetObject(id, OpenMode.ForRead);
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
        }
        if (civDoc.Styles.ProfileStyles.Count > 0) return civDoc.Styles.ProfileStyles[0];
        throw new ToolException($"no profile styles in drawing (looked for '{name}')");
    }

    private static ObjectId ResolveProfileLabelSet(Transaction tr, CivilDocument civDoc, string name)
    {
        foreach (ObjectId id in civDoc.Styles.LabelSetStyles.ProfileLabelSetStyles)
        {
            var s = (Autodesk.Civil.DatabaseServices.Styles.ProfileLabelSetStyle)tr.GetObject(id, OpenMode.ForRead);
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
        }
        if (civDoc.Styles.LabelSetStyles.ProfileLabelSetStyles.Count > 0)
            return civDoc.Styles.LabelSetStyles.ProfileLabelSetStyles[0];
        throw new ToolException($"no profile label sets in drawing (looked for '{name}')");
    }

    private static ObjectId ResolveProfileViewStyle(Transaction tr, CivilDocument civDoc, string name)
    {
        foreach (ObjectId id in civDoc.Styles.ProfileViewStyles)
        {
            var s = (Autodesk.Civil.DatabaseServices.Styles.ProfileViewStyle)tr.GetObject(id, OpenMode.ForRead);
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
        }
        if (civDoc.Styles.ProfileViewStyles.Count > 0) return civDoc.Styles.ProfileViewStyles[0];
        throw new ToolException($"no profile view styles in drawing (looked for '{name}')");
    }

    private static ObjectId ResolveProfileViewBandSet(Transaction tr, CivilDocument civDoc, string name)
    {
        var sets = civDoc.Styles.ProfileViewBandSetStyles;
        foreach (ObjectId id in sets)
        {
            var s = (Autodesk.Civil.DatabaseServices.Styles.ProfileViewBandSetStyle)tr.GetObject(id, OpenMode.ForRead);
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
        }
        if (sets.Count > 0) return sets[0];
        throw new ToolException($"no profile view band sets in drawing (looked for '{name}')");
    }
}
