using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Civil3DMcpBridge;

/// <summary>
/// Process-wide ring buffer of recent tool invocations plus per-tool
/// aggregate stats. Drives the palette UI inside Civil 3D.
/// </summary>
internal static class InvocationLog
{
    private const int Capacity = 200;
    private static readonly ConcurrentQueue<Entry> _entries = new();
    private static readonly ConcurrentDictionary<string, Stats> _stats = new();
    private static long _totalCalls;
    private static long _totalErrors;

    public static event Action? Changed;

    public static void Record(string tool, double durationMs, bool ok, string? error)
    {
        var entry = new Entry
        {
            Timestamp = DateTime.Now,
            Tool = tool,
            DurationMs = durationMs,
            Ok = ok,
            Error = error,
        };
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity) _entries.TryDequeue(out _);

        _stats.AddOrUpdate(tool,
            _ => new Stats { Calls = 1, Errors = ok ? 0 : 1, TotalMs = durationMs },
            (_, s) =>
            {
                s.Calls++;
                if (!ok) s.Errors++;
                s.TotalMs += durationMs;
                return s;
            });

        Interlocked.Increment(ref _totalCalls);
        if (!ok) Interlocked.Increment(ref _totalErrors);

        // Fire event on a thread-pool thread so UI subscribers can marshal to
        // the main thread themselves without blocking the request handler.
        try { Changed?.Invoke(); } catch { /* never let UI bugs kill the bridge */ }
    }

    public static IReadOnlyList<Entry> Recent()
    {
        // Snapshot newest-first.
        return _entries.ToArray().Reverse().ToList();
    }

    public static IReadOnlyDictionary<string, Stats> Aggregates()
    {
        // Shallow copy so callers can iterate safely.
        return _stats.ToDictionary(kv => kv.Key, kv => new Stats
        {
            Calls = kv.Value.Calls,
            Errors = kv.Value.Errors,
            TotalMs = kv.Value.TotalMs,
        });
    }

    public static long TotalCalls => Interlocked.Read(ref _totalCalls);
    public static long TotalErrors => Interlocked.Read(ref _totalErrors);

    public sealed class Entry
    {
        public DateTime Timestamp;
        public string Tool = "";
        public double DurationMs;
        public bool Ok;
        public string? Error;
    }

    public sealed class Stats
    {
        public long Calls;
        public long Errors;
        public double TotalMs;
        public double MeanMs => Calls == 0 ? 0 : TotalMs / Calls;
        public double ErrorRate => Calls == 0 ? 0 : (double)Errors / Calls;
    }
}
