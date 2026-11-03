namespace Apptrack.Entities;

public sealed class GmailWatch
{
    private GmailWatch() { }

    public GmailWatch(Guid userId, ulong historyId, DateTime watchExpirationUtc, DateTime createdAtUtc)
    {
        UserId = userId;
        HistoryId = historyId;
        WatchExpiration = watchExpirationUtc;
        CreatedAt = createdAtUtc;
    }

    public Guid UserId { get; private set; }
    public ulong HistoryId { get; internal set; }
    public DateTime WatchExpiration { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public void RefreshExpiration(DateTime watchExpirationUtc) => WatchExpiration = watchExpirationUtc;
}
