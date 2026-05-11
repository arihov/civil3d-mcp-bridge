using System;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace Civil3DMcpBridge.Tools;

internal static class ProfileEditTools
{
    public static void Register()
    {
        ToolRegistry.Register("civil3d_create_profile_from_surface", CreateFromSurface);
        ToolRegistry.Register("civil3d_create_layout_profile", CreateLayout);
        ToolRegistry.Register("civil3d_add_pvi", AddPvi);
    }

    /// <summary>
    /// Sample an existing-ground profile from a surface along an alignment.
    /// This is the standard "EG profile" creation workflow.
    ///
    /// Args:
    /// {
    ///   "alignment": "string",
    ///   "surface": "string",
    ///   "profile_name": "string",
    ///   "profile_style": "string" (optional),
    ///   "label_set_style": "string" (optional)
    /// }
    /// </summary>
    private static object CreateFromSurface(JsonElement args)
    {
        var alignmentName = args.GetRequiredString("alignment");
        var surfaceName = args.GetRequiredString("surface");
        var profileName = args.GetRequiredString("profile_name");
        var styleName = args.GetOptionalString("profile_style");
        var labelSetName = args.GetOptionalString("label_set_style");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = AlignmentTools.FindAlignmentByName(tr, civDoc, alignmentName)
            ?? throw new ToolException($"alignment '{alignmentName}' not found");

        ObjectId surfaceId = ObjectId.Null;
        foreach (ObjectId sid in civDoc.GetSurfaceIds())
        {
            var s = (Surface)tr.GetObject(sid, OpenMode.ForRead);
            if (string.Equals(s.Name, surfaceName, StringComparison.OrdinalIgnoreCase))
            {
                surfaceId = sid;
                break;
            }
        }
        if (surfaceId.IsNull)
            throw new ToolException($"surface '{surfaceName}' not found");

        var profileStyleId = ResolveProfileStyle(tr, civDoc, styleName);
        var labelSetId = ResolveProfileLabelSet(tr, civDoc, labelSetName);

        var profileId = Profile.CreateFromSurface(
            profileName,
            al.ObjectId,
            surfaceId,
            ObjectId.Null,            // layer (null = use current)
            profileStyleId,
            labelSetId);

        var p = (Profile)tr.GetObject(profileId, OpenMode.ForRead);
        var result = new
        {
            alignment = alignmentName,
            name = p.Name,
            sourceSurface = surfaceName,
            startStation = p.StartingStation,
            endStation = p.EndingStation,
            elevationMin = p.ElevationMin,
            elevationMax = p.ElevationMax,
        };
        tr.Commit();
        return result;
    }

    /// <summary>
    /// Create an empty layout (FG / design) profile on an alignment.
    /// Subsequent PVIs go through civil3d_add_pvi.
    /// Args: { "alignment": "string", "profile_name": "string", "profile_style": "string" (optional) }
    /// </summary>
    private static object CreateLayout(JsonElement args)
    {
        var alignmentName = args.GetRequiredString("alignment");
        var profileName = args.GetRequiredString("profile_name");
        var styleName = args.GetOptionalString("profile_style");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var al = AlignmentTools.FindAlignmentByName(tr, civDoc, alignmentName)
            ?? throw new ToolException($"alignment '{alignmentName}' not found");

        var profileStyleId = ResolveProfileStyle(tr, civDoc, styleName);
        var labelSetId = ResolveProfileLabelSet(tr, civDoc, null);

        var profileId = Profile.CreateByLayout(
            profileName,
            al.ObjectId,
            ObjectId.Null,            // layer (null = use current)
            profileStyleId,
            labelSetId);

        var p = (Profile)tr.GetObject(profileId, OpenMode.ForRead);
        var result = new { alignment = alignmentName, name = p.Name, profileId = profileId.Handle.Value.ToString("x") };
        tr.Commit();
        return result;
    }

    /// <summary>
    /// Add a PVI to a layout profile at (station, elevation).
    /// Args: { "alignment": "string", "profile": "string", "station": number, "elevation": number }
    /// </summary>
    private static object AddPvi(JsonElement args)
    {
        var alignmentName = args.GetRequiredString("alignment");
        var profileName = args.GetRequiredString("profile");
        var station = args.GetRequiredDouble("station");
        var elevation = args.GetRequiredDouble("elevation");

        var (doc, civDoc) = DrawingContext.RequireActive();
        using var docLock = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();

        var pRead = ProfileTools.FindProfile(tr, civDoc, alignmentName, profileName);
        var profile = (Profile)tr.GetObject(pRead.ObjectId, OpenMode.ForWrite);

        // PVIs can be added via the entity collection's AddPVI(...) helper
        // on a layout profile. This wires up the surrounding tangents.
        profile.PVIs.AddPVI(station, elevation);

        var result = new
        {
            alignment = alignmentName,
            profile = profileName,
            station,
            elevation,
            totalPvis = profile.PVIs.Count,
        };
        tr.Commit();
        return result;
    }

    private static ObjectId ResolveProfileStyle(
        Transaction tr,
        Autodesk.Civil.ApplicationServices.CivilDocument civDoc,
        string? name)
    {
        ObjectId first = ObjectId.Null;
        foreach (ObjectId sid in civDoc.Styles.ProfileStyles)
        {
            if (first.IsNull) first = sid;
            if (string.IsNullOrEmpty(name)) continue;
            var obj = (Autodesk.Civil.DatabaseServices.Styles.ProfileStyle)tr.GetObject(sid, OpenMode.ForRead);
            if (string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
                return sid;
        }
        if (first.IsNull) throw new ToolException("no profile styles in this drawing");
        return first;
    }

    private static ObjectId ResolveProfileLabelSet(
        Transaction tr,
        Autodesk.Civil.ApplicationServices.CivilDocument civDoc,
        string? name)
    {
        ObjectId first = ObjectId.Null;
        foreach (ObjectId sid in civDoc.Styles.LabelSetStyles.ProfileLabelSetStyles)
        {
            if (first.IsNull) first = sid;
            if (string.IsNullOrEmpty(name)) continue;
            var obj = tr.GetObject(sid, OpenMode.ForRead);
            var prop = obj.GetType().GetProperty("Name");
            if (string.Equals(prop?.GetValue(obj) as string, name, StringComparison.OrdinalIgnoreCase))
                return sid;
        }
        if (first.IsNull) throw new ToolException("no profile label set styles in this drawing");
        return first;
    }
}
