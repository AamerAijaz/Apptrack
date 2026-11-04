using System.Text;
using System.Text.Json;
using Apptrack.Data;
using Microsoft.EntityFrameworkCore;

namespace Apptrack.Services;

public sealed record PubSubPush(PubSubMessage Message, string Subscription);
public sealed record PubSubMessage(string Data, string MessageId, DateTime PublishTime);

public sealed class GmailSyncRetryException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class GmailSyncService(
    AppDbContext db,
    IMailboxClientFactory mailboxes,
    IConfiguration configuration,
    ILogger<GmailSyncService> logger)
{
    public const string Provider = "gmail";
    private static readonly string[] KeptLabels = ["INBOX", "SENT"];

    public async Task ProcessPushAsync(PubSubPush push, CancellationToken ct = default)
    {
        if (!TryDecode(push, out var email, out var noticeHistoryId))
            return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email && u.Provider == Provider, ct);
        if (user is null)
            return;

        var hasToken = await db.GoogleAccessTokens.AnyAsync(t => t.UserId == user.Id, ct);
        if (!hasToken)
            return;

        var watch = await db.GmailWatches.FirstOrDefaultAsync(w => w.UserId == user.Id, ct);
        if (watch is null || watch.HistoryId >= noticeHistoryId)
            return;

        var mailbox = await mailboxes.OpenAsync(user.Id, ct);
        if (mailbox is null)
            return;

        var batch = await mailbox.ListAddedSinceAsync(watch.HistoryId, ct);
        await Parallel.ForEachAsync(
            batch.MessageIds,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (messageId, token) =>
            {
                var message = await mailbox.ReadAsync(messageId, token);
                if (message is null || !message.LabelIds.Any(label => KeptLabels.Contains(label)))
                    return;
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Gmail message {MessageId} for {Email}\n{Content}",
                        message.MessageId,
                        email,
                        message.Content);
                }
            });

        await db.GmailWatches
            .Where(w => w.UserId == user.Id && w.HistoryId < batch.HistoryId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(w => w.HistoryId, batch.HistoryId), ct);
    }

    public async Task StartWatchAsync(Guid userId, CancellationToken ct = default)
    {
        var topicName = configuration["Google:TopicName"];
        if (string.IsNullOrWhiteSpace(topicName))
            throw new InvalidOperationException("Google:TopicName is required.");

        var mailbox = await mailboxes.OpenAsync(userId, ct)
            ?? throw new InvalidOperationException($"No Gmail token is stored for user {userId}.");
        var registration = await mailbox.WatchInboxAsync(topicName, ct);
        var existing = await db.GmailWatches.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (existing is null)
        {
            db.GmailWatches.Add(new Entities.GmailWatch(
                userId,
                registration.HistoryId,
                registration.ExpiresAtUtc,
                DateTime.UtcNow));
        }
        else
        {
            existing.RefreshExpiration(registration.ExpiresAtUtc);
        }

        await db.SaveChangesAsync(ct);
    }

    private static bool TryDecode(PubSubPush push, out string email, out ulong historyId)
    {
        email = "";
        historyId = 0;

        if (push.Message?.Data is not string data || data.Length == 0)
            return false;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(data));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (Property(root, "emailAddress") is not JsonElement emailProperty)
                return false;

            email = emailProperty.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(email))
                return false;

            if (Property(root, "historyId") is not JsonElement historyProperty)
                return false;

            if (historyProperty.ValueKind == JsonValueKind.String)
            {
                if (!ulong.TryParse(historyProperty.GetString(), out historyId) || historyId == 0)
                    return false;
            }
            else if (historyProperty.ValueKind == JsonValueKind.Number)
            {
                if (!historyProperty.TryGetUInt64(out historyId) || historyId == 0)
                    return false;
            }
            else
            {
                return false;
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static JsonElement? Property(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }
}
