using Apptrack.Services;

namespace Apptrack.Entities;

public sealed class ApplicationNotice
{
    public const int MessageIdMaxLength = 255;
    public const int StatusMaxLength = 32;

    private ApplicationNotice() { }

    /// <summary>
    /// Trusts <see cref="CompanyName"/>, <see cref="RoleName"/>, <see cref="NoticeStatus"/>, and <see cref="UtcInstant"/>.
    /// The id and <see cref="CreatedAt"/> both come from <paramref name="createdAt"/>.
    /// </summary>
    internal ApplicationNotice(
        Guid userId,
        string messageId,
        CompanyName company,
        RoleName? role,
        NoticeStatus status,
        UtcInstant sentAt,
        UtcInstant createdAt,
        string content)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("A notice requires a person.", nameof(userId));
        if (string.IsNullOrWhiteSpace(messageId) || messageId.Length > MessageIdMaxLength)
            throw new ArgumentException("A notice requires a message id.", nameof(messageId));
        if (content is null)
            throw new ArgumentException("A notice requires message content.", nameof(content));

        Id = Guid.CreateVersion7(new DateTimeOffset(createdAt.Value));
        UserId = userId;
        MessageId = messageId;
        Company = company.Value;
        Role = role?.Value;
        Status = status.Label;
        SentAt = sentAt.Value;
        Content = content;
        CreatedAt = createdAt.Value;
    }

    /// <summary>Changes the stage. Recorded time and Sent at stay as they are.</summary>
    public void MoveTo(NoticeStatus status) => Status = status.Label;

    public Guid Id { get; private set; }
    public string MessageId { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public string Company { get; private set; } = null!;
    public string? Role { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTime SentAt { get; private set; }
    public string Content { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }
}
