using Apptrack.Data;
using Microsoft.EntityFrameworkCore;

namespace Apptrack.Services;

public readonly record struct WatchRefreshOutcome(int Succeeded, int Failed);

public sealed class GmailWatchRefresh(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<GmailWatchRefresh> logger)
{
    public async Task<WatchRefreshOutcome> RefreshDueAsync(CancellationToken ct = default)
    {
        List<Guid> userIds;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deadline = time.GetUtcNow().UtcDateTime.AddHours(24);
            userIds = await db.GmailWatches
                .Where(w => w.WatchExpiration <= deadline)
                .Where(w => db.Users.Any(u => u.Id == w.UserId))
                .Where(w => db.GoogleAccessTokens.Any(t => t.UserId == w.UserId))
                .Select(w => w.UserId)
                .ToListAsync(ct);
        }

        var succeeded = 0;
        var failed = 0;
        foreach (var userId in userIds)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<GmailSyncService>();
                await sync.StartWatchAsync(userId, ct);
                succeeded++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex, "Gmail watch refresh failed for user {UserId}.", userId);
            }
        }

        logger.LogInformation(
            "Gmail watch refresh finished. Succeeded {Succeeded}, failed {Failed}.",
            succeeded,
            failed);
        return new WatchRefreshOutcome(succeeded, failed);
    }
}
