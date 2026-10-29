using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3DMcpBridge;

internal sealed class BridgeServer
{
    private readonly HttpListener _listener;
    private readonly MainThreadDispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    /// <summary>Default per-call ceiling; callers may pass timeoutMs (max 30 min).</summary>
    private const int DefaultTimeoutMs = 300_000;

    public string UrlPrefix { get; }
    public int Port { get; }

    public BridgeServer(int port, MainThreadDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Port = port;
        UrlPrefix = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(UrlPrefix);
    }

    public void Start()
    {
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
        InstanceRegistry.Write(Port);
    }

    public void Stop()
    {
        InstanceRegistry.Remove();
        try
        {
            _cts.Cancel();
            if (_listener.IsListening) _listener.Stop();
            _acceptLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }
        finally { _listener.Close(); }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }

            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    /// <summary>
    /// Browser-borne requests are refused: a web page could otherwise POST to
    /// 127.0.0.1 and drive run_command. Native MCP clients send no Origin
    /// header. DNS-rebinding is blocked by requiring a loopback Host header.
    /// </summary>
    private static string? RejectReason(HttpListenerRequest req)
    {
        var origin = req.Headers["Origin"];
        if (!string.IsNullOrEmpty(origin) && origin != "null"
            && Environment.GetEnvironmentVariable("CIVIL3D_BRIDGE_ALLOW_BROWSER") != "1")
            return "cross-origin browser requests are not accepted";
        var host = (req.Headers["Host"] ?? "").Split(':')[0].Trim().ToLowerInvariant();
        if (host is not ("127.0.0.1" or "localhost" or "[::1]" or ""))
            return $"unexpected Host header '{host}'";
        return null;
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var method = ctx.Request.HttpMethod;

            if (RejectReason(ctx.Request) is { } reason)
            {
                await WriteJsonAsync(ctx, 403, new { ok = false, error = reason });
                return;
            }

            if (method == "GET" && path == "/health")
            {
                await WriteJsonAsync(ctx, 200, await HealthPayloadAsync());
                return;
            }

            if (method == "POST" && path == "/invoke")
            {
                await HandleInvokeAsync(ctx).ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(ctx, 404, new { ok = false, error = $"unknown route: {method} {path}" });
        }
        catch (Exception ex)
        {
            try { await WriteJsonAsync(ctx, 500, new { ok = false, error = $"bridge error: {ex.Message}" }); }
            catch { }
        }
    }

    private async Task<object> HealthPayloadAsync()
    {
        // Document info must be read on the main thread; don't block health on it.
        object? docs = null;
        try
        {
            using var cts = new CancellationTokenSource(1500);
            var task = _dispatcher.RunOnMainThreadAsync(DrawingContext.DescribeDocuments, cts.Token);
            if (await Task.WhenAny(task, Task.Delay(1500)) == task && task.IsCompletedSuccessfully)
                docs = task.Result;
            else cts.Cancel();
        }
        catch { }

        return new
        {
            ok = true,
            version = InstanceRegistry.BridgeVersion,
            release = InstanceRegistry.ReleaseYear,
            acadVersion = InstanceRegistry.AcadVersion,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            pid = Environment.ProcessId,
            port = Port,
            busy = docs == null,
            pendingCalls = _dispatcher.PendingCount,
            documents = docs,
            toolCount = ToolRegistry.Count,
            tools = ToolRegistry.Names,
        };
    }

    private async Task HandleInvokeAsync(HttpListenerContext ctx)
    {
        string body;
        using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        InvokeRequest? req;
        try { req = JsonSerializer.Deserialize<InvokeRequest>(body, Json.Options); }
        catch (JsonException ex)
        {
            await WriteJsonAsync(ctx, 400, new { ok = false, error = $"invalid JSON: {ex.Message}" });
            return;
        }

        if (req is null || string.IsNullOrWhiteSpace(req.Tool))
        {
            await WriteJsonAsync(ctx, 400, new { ok = false, error = "missing 'tool' field" });
            return;
        }

        if (!ToolRegistry.TryGet(req.Tool, out var handler))
        {
            await WriteJsonAsync(ctx, 404, new
            {
                ok = false,
                error = $"unknown tool: {req.Tool}",
                hint = $"available tools: {string.Join(", ", ToolRegistry.Names)}",
            });
            return;
        }

        var args = req.Args ?? JsonDocument.Parse("{}").RootElement;
        var timeoutMs = Math.Clamp(req.TimeoutMs ?? DefaultTimeoutMs, 1_000, 1_800_000);
        var document = string.IsNullOrWhiteSpace(req.Document) ? null : req.Document;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var cts = new CancellationTokenSource();

        try
        {
            var work = _dispatcher.RunOnMainThreadAsync(() =>
            {
                DrawingContext.TargetDocument = document;
                try { return handler!(args); }
                finally { DrawingContext.TargetDocument = null; }
            }, cts.Token);

            if (await Task.WhenAny(work, Task.Delay(timeoutMs)).ConfigureAwait(false) != work)
            {
                cts.Cancel();
                sw.Stop();
                var msg = $"timed out after {timeoutMs / 1000}s waiting for Civil 3D. "
                          + "If a command, dialog or grip edit is active, finish or cancel it (Esc) and retry.";
                InvocationLog.Record(req.Tool!, sw.Elapsed.TotalMilliseconds, false, msg);
                await WriteJsonAsync(ctx, 504, new { ok = false, error = msg });
                return;
            }

            var result = await work.ConfigureAwait(false);
            sw.Stop();
            InvocationLog.Record(req.Tool!, sw.Elapsed.TotalMilliseconds, true, null);
            await WriteJsonAsync(ctx, 200, new { ok = true, result, elapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds) });
        }
        catch (ToolException tex)
        {
            sw.Stop();
            InvocationLog.Record(req.Tool!, sw.Elapsed.TotalMilliseconds, false, tex.Message);
            await WriteJsonAsync(ctx, 400, new { ok = false, error = tex.Message });
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            await WriteJsonAsync(ctx, 504, new { ok = false, error = "call cancelled before Civil 3D could run it" });
        }
        catch (Exception ex)
        {
            sw.Stop();
            var inner = ex is AggregateException ae && ae.InnerExceptions.Count == 1 ? ae.InnerExceptions[0] : ex;
            InvocationLog.Record(req.Tool!, sw.Elapsed.TotalMilliseconds, false, $"{inner.GetType().Name}: {inner.Message}");
            await WriteJsonAsync(ctx, 500, new { ok = false, error = $"{inner.GetType().Name}: {inner.Message}" });
        }
    }

    private static async Task WriteJsonAsync(HttpListenerContext ctx, int status, object payload)
    {
        var json = JsonSerializer.Serialize(payload, Json.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        ctx.Response.OutputStream.Close();
    }

    private sealed class InvokeRequest
    {
        public string? Tool { get; set; }
        public JsonElement? Args { get; set; }
        /// <summary>Optional: target an open drawing by file name or full path instead of the active one.</summary>
        public string? Document { get; set; }
        public int? TimeoutMs { get; set; }
    }
}
