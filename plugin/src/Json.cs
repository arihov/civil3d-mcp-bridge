using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Civil3DMcpBridge;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = false,
    };

    public static string GetRequiredString(this JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            throw new ToolException($"missing required string argument '{name}'");
        if (prop.ValueKind != JsonValueKind.String)
            throw new ToolException($"argument '{name}' must be a string");
        var value = prop.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ToolException($"argument '{name}' must not be empty");
        return value;
    }

    public static string? GetOptionalString(this JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
            return null;
        return prop.GetString();
    }

    public static double GetRequiredDouble(this JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var prop))
            throw new ToolException($"missing required number argument '{name}'");
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetDouble(),
            JsonValueKind.String when double.TryParse(prop.GetString(), out var v) => v,
            _ => throw new ToolException($"argument '{name}' must be a number"),
        };
    }

    public static double GetOptionalDouble(this JsonElement args, string name, double defaultValue)
    {
        if (!args.TryGetProperty(name, out var prop)) return defaultValue;
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetDouble(),
            JsonValueKind.String when double.TryParse(prop.GetString(), out var v) => v,
            _ => defaultValue,
        };
    }

    public static int GetRequiredInt(this JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var prop))
            throw new ToolException($"missing required integer argument '{name}'");
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt32(),
            JsonValueKind.String when int.TryParse(prop.GetString(), out var v) => v,
            _ => throw new ToolException($"argument '{name}' must be an integer"),
        };
    }

    public static int GetOptionalInt(this JsonElement args, string name, int defaultValue)
    {
        if (!args.TryGetProperty(name, out var prop)) return defaultValue;
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt32(),
            JsonValueKind.String when int.TryParse(prop.GetString(), out var v) => v,
            _ => defaultValue,
        };
    }

    public static bool GetOptionalBool(this JsonElement args, string name, bool defaultValue)
    {
        if (!args.TryGetProperty(name, out var prop)) return defaultValue;
        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(prop.GetString(), out var v) => v,
            _ => defaultValue,
        };
    }

    /// <summary>
    /// Iterate a required JSON array argument. Throws if missing or not an array.
    /// </summary>
    public static JsonElement.ArrayEnumerator GetRequiredArray(this JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var prop))
            throw new ToolException($"missing required array argument '{name}'");
        if (prop.ValueKind != JsonValueKind.Array)
            throw new ToolException($"argument '{name}' must be an array");
        return prop.EnumerateArray();
    }

    /// <summary>
    /// Iterate an optional JSON array. Returns empty enumerator if absent.
    /// </summary>
    public static IEnumerable<JsonElement> GetOptionalArray(this JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var item in prop.EnumerateArray()) yield return item;
    }
}
