using System.Diagnostics;

namespace Apptrack;

public static class RequestLog
{
    public static void UseRequestLog(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Apptrack.RequestLog");
        app.Use(async (HttpContext context, RequestDelegate next) =>
            await InvokeAsync(context, next, logger));
    }

    public static async Task InvokeAsync(HttpContext context, RequestDelegate next, ILogger logger)
    {
        var start = Stopwatch.GetTimestamp();
        var status = StatusCodes.Status500InternalServerError;
        try
        {
            await next(context);
            status = context.Response.StatusCode;
        }
        catch (Exception)
        {
            status = context.Response.HasStarted
                ? context.Response.StatusCode
                : StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            var elapsedMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            logger.LogInformation(
                "{Method} {Path} {StatusCode} {ElapsedMs}ms",
                context.Request.Method,
                context.Request.Path.Value,
                status,
                elapsedMs);
        }
    }
}
