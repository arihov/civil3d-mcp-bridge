using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3DMcpBridge;

/// <summary>
/// Marshals delegates from HTTP worker threads onto Civil 3D's main UI thread.
///
/// v0.4 changes (Civil 3D 2027 hardening):
///  * Wake-up: Application.Idle only fires when the message queue drains, so a
///    request arriving while Civil 3D sits idle could wait until the mouse
///    moved. We now also post a message to a hidden control created on the
///    main thread, which pumps the queue immediately.
///  * Quiescence: work only runs when AutoCAD is quiescent (no command,
///    grip edit or jig in progress). Otherwise it stays queued and the HTTP
///    layer returns a "busy" error once its timeout elapses.
///  * Cancellation: a request whose HTTP caller timed out is skipped rather
///    than executed late against a drawing the user has moved on from.
/// </summary>
internal sealed class MainThreadDispatcher : IDisposable
{
    private sealed record Item(Action Run, Action Cancel, CancellationToken Token);

    private readonly ConcurrentQueue<Item> _queue = new();
    private readonly EventHandler _idleHandler;
    private readonly System.Windows.Forms.Control _pump;
    private int _disposed;
    private int _pumping;

    /// <summary>Must be constructed on the main thread (IExtensionApplication.Initialize or a command).</summary>
    public MainThreadDispatcher()
    {
        _idleHandler = (_, _) => Drain();
        AcadApp.Idle += _idleHandler;
        _pump = new System.Windows.Forms.Control();
        _pump.CreateControl();
        _ = _pump.Handle; // force the HWND on this (main) thread
    }

    public int PendingCount => _queue.Count;

    public Task<T> RunOnMainThreadAsync<T>(Func<T> work, CancellationToken token = default)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return Task.FromException<T>(new ObjectDisposedException(nameof(MainThreadDispatcher)));

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(new Item(
            () =>
            {
                try { tcs.TrySetResult(work()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            },
            () => tcs.TrySetCanceled(),
            token));
        Wake();
        return tcs.Task;
    }

    private void Wake()
    {
        try
        {
            if (_pump.IsHandleCreated && !_pump.IsDisposed)
                _pump.BeginInvoke(new Action(Drain));
        }
        catch { /* Idle remains as the fallback */ }
    }

    private static bool IsQuiescent()
    {
        try
        {
            if (!AcadApp.IsQuiescent) return false;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            return doc == null || string.IsNullOrEmpty(doc.CommandInProgress);
        }
        catch { return true; }
    }

    private void Drain()
    {
        // Re-entrancy guard: a tool that pumps messages must not recurse in here.
        if (Interlocked.Exchange(ref _pumping, 1) == 1) return;
        try
        {
            const int maxPerTick = 8;
            for (var i = 0; i < maxPerTick; i++)
            {
                if (!_queue.TryPeek(out var head)) return;
                if (head.Token.IsCancellationRequested)
                {
                    if (_queue.TryDequeue(out var dead)) dead.Cancel();
                    continue;
                }
                if (!IsQuiescent()) return; // stay queued; Idle will retry
                if (!_queue.TryDequeue(out var item)) return;
                try { item.Run(); }
                catch { /* routed through the TCS */ }
            }
            if (!_queue.IsEmpty) Wake();
        }
        finally { Volatile.Write(ref _pumping, 0); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        AcadApp.Idle -= _idleHandler;
        while (_queue.TryDequeue(out var leftover))
        {
            try { leftover.Cancel(); } catch { }
        }
        try { _pump.Dispose(); } catch { }
    }
}
