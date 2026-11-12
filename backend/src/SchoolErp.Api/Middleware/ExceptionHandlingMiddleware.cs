using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolErp.Domain.Common;

namespace SchoolErp.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException ex)
        {
            await WriteAsync(context, ex.StatusCode, ex.Code, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await WriteAsync(context, StatusCodes.Status409Conflict, "concurrency_conflict",
                "This record was changed by someone else. Reload it and try again.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Database update failed for {Path}", context.Request.Path);
            var message = ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
                ? "A record with the same unique value already exists."
                : "The change could not be saved. Check the submitted values.";
            await WriteAsync(context, StatusCodes.Status409Conflict, "conflict", message);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected; nothing useful to write.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method,
                context.Request.Path);
            await WriteAsync(context, StatusCodes.Status500InternalServerError, "server_error",
                "An unexpected error occurred. Use the trace id when contacting support.");
        }
    }

    private static async Task WriteAsync(HttpContext context, int statusCode, string code, string message)
    {
        if (context.Response.HasStarted) return;

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = code,
            Detail = message,
            Instance = context.Request.Path,
            Extensions =
            {
                ["code"] = code,
                ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier
            }
        });
    }
}
