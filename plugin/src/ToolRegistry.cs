using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Civil3DMcpBridge;

public sealed class ToolException : Exception
{
    public ToolException(string message) : base(message) { }
}

internal static class ToolRegistry
{
    private static readonly ConcurrentDictionary<string, Func<JsonElement, object?>> _tools = new();

    public static int Count => _tools.Count;

    public static IReadOnlyList<string> Names =>
        _tools.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();

    public static void Register(string name, Func<JsonElement, object?> handler)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("tool name required", nameof(name));
        _tools[name] = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public static bool TryGet(string name, out Func<JsonElement, object?>? handler)
        => _tools.TryGetValue(name, out handler);
}
