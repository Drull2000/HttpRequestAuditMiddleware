using System.Diagnostics;
using System.Text.Json;

namespace WebApplication4.Middleware;

public sealed class HttpRequestAuditMiddleware
{
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _environment;

    public HttpRequestAuditMiddleware(RequestDelegate next, IWebHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        Exception? exception = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            exception = ex;

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }

            throw;
        }
        finally
        {
            stopwatch.Stop();

            var logEntry = new RequestLogEntry
            {
                Timestamp = startedAt,
                Method = context.Request.Method,
                Path = context.Request.Path.Value ?? string.Empty,
                StatusCode = context.Response.StatusCode,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Message = exception?.Message ?? BuildMessage(context.Response.StatusCode)
            };

            await WriteLogAsync(logEntry);
        }
    }

    private async Task WriteLogAsync(RequestLogEntry logEntry)
    {
        var logDirectory = Path.Combine(_environment.ContentRootPath, "Logs");
        Directory.CreateDirectory(logDirectory);

        var logFilePath = Path.Combine(logDirectory, "requests.log");
        var logLine = JsonSerializer.Serialize(logEntry) + Environment.NewLine;

        await FileLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(logFilePath, logLine);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private static string BuildMessage(int statusCode)
    {
        return statusCode switch
        {
            >= 200 and < 300 => "Request completed successfully",
            >= 300 and < 400 => "Request redirected",
            >= 400 and < 500 => "Client error",
            >= 500 => "Server error",
            _ => "Request completed"
        };
    }

    private sealed class RequestLogEntry
    {
        public DateTimeOffset Timestamp { get; init; }
        public string Method { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public int StatusCode { get; init; }
        public long DurationMs { get; init; }
        public string Message { get; init; } = string.Empty;
    }
}
