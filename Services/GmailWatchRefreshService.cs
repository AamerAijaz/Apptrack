using Microsoft.Extensions.Hosting;

namespace Apptrack.Services;

public sealed class GmailWatchRefreshService(
    GmailWatchRefresh refresh,
    TimeProvider time,
    ILogger<GmailWatchRefreshService> logger) : BackgroundService
{
    private readonly TimeZoneInfo _zone = India();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = time.GetUtcNow();
            var delay = NextMidnight(now, _zone) - now;
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                logger.LogInformation("Start watch refresh!");
                await refresh.RefreshDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Gmail watch refresh failed.");
            }
        }
    }

    public static DateTimeOffset NextMidnight(DateTimeOffset utcNow, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var nextLocal = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
        return new DateTimeOffset(nextLocal, zone.GetUtcOffset(nextLocal)).ToUniversalTime();
    }

    private static TimeZoneInfo India()
    {
        foreach (var id in new[] { "Asia/Kolkata", "India Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
                return zone;
        }

        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST");
    }
}
