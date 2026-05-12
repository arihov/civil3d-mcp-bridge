using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Civil3DMcpBridge;

internal sealed class BridgeServer
{
    private readonly HttpListener _listener;
    private readonly MainThreadDispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    public string UrlPrefix { get; }

    public BridgeServer(int port, MainThreadDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        UrlPrefix = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(UrlPrefix);
    }

    public void Start()
    {
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public void Stop()
    {
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

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var method = ctx.Request.HttpMethod;

            if (method == "GET" && path == "/health")
            {
                await WriteJsonAsync(ctx, 200, new
                {
                    ok = true,
                    version = "0.2.0",
                    toolCount = ToolRegistry.Count,
                    tools = ToolRegistry.Names,
                });
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
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await _dispatcher.RunOnMainThreadAsync(() => handler!(args)).ConfigureAwait(false);
            sw.Stop();
            InvocationLog.Record(req.Tool!, sw.Elapsed.TotalMilliseconds, true, null);
            await WriteJsonAsync(ctx, 200, new { ok = true, result });
        }
        catch (ToolException tex)
        {
            sw.Stop();
            InvocationLog.Record(req.Tool!, sw.Elapsed.TotalMilliseconds, false, tex.Message);
            await WriteJsonAsync(ctx, 400, new { ok = false, error = tex.Message });
        }
        catch (Exception ex)
        {
            sw.Stop();
            InvocationLog.Record(req.Tool!, sw.Elapsed.TotalMilliseconds, false, $"{ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(ctx, 500, new { ok = false, error = $"{ex.GetType().Name}: {ex.Message}" });
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
    }
}
