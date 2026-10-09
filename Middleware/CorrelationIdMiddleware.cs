using System.Diagnostics;

namespace DistributionApp.Middleware;

public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> log)
{
    public const string Header = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var id = ctx.Request.Headers[Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
            id = Guid.NewGuid().ToString("N");

        ctx.Items[Header] = id;
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });

        using (log.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            var sw = Stopwatch.StartNew();
            await next(ctx);
            sw.Stop();
            log.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {Elapsed} ms",
                ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
        }
    }
}