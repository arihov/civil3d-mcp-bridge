using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace Civil3DMcpBridge;

/// <summary>Shared v0.4 helpers: handle parsing, surface lookup, CSV writing, rounding.</summary>
internal static class Common
{
    public static ObjectId RequireHandle(Database db, string handleHex)
    {
        var h = handleHex.Trim();
        if (h.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) h = h[2..];
        if (!long.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new ToolException($"'{handleHex}' is not a valid hex handle");
        if (!db.TryGetObjectId(new Handle(v), out var id) || id.IsErased)
            throw new ToolException($"no live object with handle {handleHex}");
        return id;
    }

    public static string HandleOf(Autodesk.AutoCAD.DatabaseServices.DBObject o) => o.Handle.Value.ToString("x");

    public static T RequireSurface<T>(Transaction tr, CivilDocument civ, string name, OpenMode mode = OpenMode.ForRead)
        where T : Surface
    {
        var id = Helpers.FindSurfaceId(tr, civ, name) ?? throw new ToolException($"surface '{name}' not found");
        var obj = tr.GetObject(id, mode);
        return obj as T ?? throw new ToolException($"surface '{name}' is a {obj.GetType().Name}, expected {typeof(T).Name}");
    }

    /// <summary>Surface elevation or null when (x,y) is outside the surface or in a hole.</summary>
    public static double? TryElevation(Surface s, double x, double y)
    {
        try
        {
            var z = s.FindElevationAtXY(x, y);
            return double.IsNaN(z) ? null : z;
        }
        catch { return null; }
    }

    public static double R(double v, int nd = 3) => Math.Round(v, nd, MidpointRounding.AwayFromZero);

    public static string Csv(IEnumerable<string> headers, IEnumerable<IEnumerable<object?>> rows)
    {
        static string Esc(object? o)
        {
            var s = o switch
            {
                null => "",
                double d => d.ToString("0.###", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => o.ToString() ?? "",
            };
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers));
        foreach (var r in rows)
        {
            var cells = new List<string>();
            foreach (var c in r) cells.Add(Esc(c));
            sb.AppendLine(string.Join(",", cells));
        }
        return sb.ToString();
    }

    public static void WriteText(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    public static ObjectId EnsureLayer(Transaction tr, Database db, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return db.Clayer;
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name)) return lt[name];
        lt.UpgradeOpen();
        var rec = new LayerTableRecord { Name = name };
        var id = lt.Add(rec);
        tr.AddNewlyCreatedDBObject(rec, true);
        return id;
    }

    public static BlockTableRecord ModelSpace(Transaction tr, Database db, OpenMode mode = OpenMode.ForWrite)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        return (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], mode);
    }

    public static IEnumerable<Point3d> ReadPoints(System.Text.Json.JsonElement args, string name, bool requireZ = false)
    {
        foreach (var p in args.GetRequiredArray(name))
        {
            double x, y, z = 0;
            if (p.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var a = new List<double>();
                foreach (var v in p.EnumerateArray()) a.Add(v.GetDouble());
                if (a.Count < 2) throw new ToolException($"each '{name}' entry needs at least [x, y]");
                x = a[0]; y = a[1]; z = a.Count > 2 ? a[2] : 0;
                if (requireZ && a.Count < 3) throw new ToolException($"each '{name}' entry needs [x, y, z]");
            }
            else
            {
                x = p.GetRequiredDouble("x"); y = p.GetRequiredDouble("y");
                z = p.GetOptionalDouble("z", 0);
            }
            yield return new Point3d(x, y, z);
        }
    }
}
