namespace Apptrack.Entities;

/// <summary>
/// A mailbox message that updated an existing Application Notice.
/// The notice keeps its own message id.
/// </summary>
public sealed class AppliedMailboxMessage
{
    private AppliedMailboxMessage() { }

    internal AppliedMailboxMessage(Guid userId, string messageId, Guid noticeId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("A message requires a person.", nameof(userId));
        if (string.IsNullOrWhiteSpace(messageId) || messageId.Length > ApplicationNotice.MessageIdMaxLength)
            throw new ArgumentException("A message requires a message id.", nameof(messageId));
        if (noticeId == Guid.Empty)
            throw new ArgumentException("A message requires a notice.", nameof(noticeId));

        UserId = userId;
        MessageId = messageId;
        NoticeId = noticeId;
    }

    public Guid UserId { get; private set; }
    public string MessageId { get; private set; } = null!;
    public Guid NoticeId { get; private set; }
}
