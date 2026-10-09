using DistributionApp.Middleware;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DistributionApp.Exceptions;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var deadlock = IsSqlError(ex, 1205);

        var (status, title) = ex switch
        {
            BadRequestException => (400, "Bad Request"),
            NotFoundException => (404, "Not Found"),
            IdempotencyKeyReusedException => (422, "Idempotency Key Reused"),
            InsufficientStockException => (409, "Insufficient Stock"),
            InvalidStatusTransitionException => (409, "Invalid Status Transition"),
            DbUpdateConcurrencyException => (409, "Concurrency Conflict"),
            _ when deadlock => (409, "Conflict, Please Retry"),
            _ => (500, "Internal Server Error")
        };

        if (status == 500) log.LogError(ex, "Unhandled exception. TraceId {TraceId}", ctx.TraceIdentifier);
        else log.LogWarning("{Title}: {Message}. TraceId {TraceId}", title, ex.Message, ctx.TraceIdentifier);

        var detail = status == 500 ? "An unexpected error occurred."
                   : deadlock ? "The request conflicted with another one. Please retry."
                   : ex.Message;

        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["traceId"] = ctx.TraceIdentifier;
        problem.Extensions["correlationId"] = ctx.Items[CorrelationIdMiddleware.Header];

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }

    private static bool IsSqlError(Exception ex, int number) =>
        ex is SqlException a && a.Number == number ||
        ex.InnerException is SqlException b && b.Number == number;
}