using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3DMcpBridge;

/// <summary>
/// Marshals delegates from worker threads onto Civil 3D's main UI thread
/// via Application.Idle. See docs/ARCHITECTURE.md for rationale.
/// </summary>
internal sealed class MainThreadDispatcher : IDisposable
{
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly EventHandler _idleHandler;
    private int _disposed;

    public MainThreadDispatcher()
    {
        _idleHandler = OnIdle;
        AcadApp.Idle += _idleHandler;
    }

    public Task<T> RunOnMainThreadAsync<T>(Func<T> work)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return Task.FromException<T>(new ObjectDisposedException(nameof(MainThreadDispatcher)));

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(() =>
        {
            try { tcs.TrySetResult(work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }

    private void OnIdle(object? sender, EventArgs e)
    {
        const int maxPerTick = 4;
        for (var i = 0; i < maxPerTick; i++)
        {
            if (!_queue.TryDequeue(out var action)) return;
            try { action(); }
            catch { /* per-action exceptions are routed through the TCS */ }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        AcadApp.Idle -= _idleHandler;
        while (_queue.TryDequeue(out var leftover))
        {
            try { leftover(); } catch { }
        }
    }
}
