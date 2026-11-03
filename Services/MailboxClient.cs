namespace Apptrack.Services;

public sealed record WatchRegistration(ulong HistoryId, DateTime ExpiresAtUtc);

public sealed record HistoryBatch(ulong HistoryId, IReadOnlyList<string> MessageIds);

public sealed record MailMessage(string MessageId, string Content, IReadOnlyList<string> LabelIds);

public interface IMailboxClient
{
    Task<WatchRegistration> WatchInboxAsync(string topicName, CancellationToken ct);
    Task<HistoryBatch> ListAddedSinceAsync(ulong startHistoryId, CancellationToken ct);
    Task<MailMessage?> ReadAsync(string messageId, CancellationToken ct);
}

public interface IMailboxClientFactory
{
    Task<IMailboxClient?> OpenAsync(Guid userId, CancellationToken ct);
}
