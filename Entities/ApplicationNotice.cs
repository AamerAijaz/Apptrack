using Apptrack.Services;

namespace Apptrack.Entities;

public sealed class ApplicationNotice
{
    public const int MessageIdMaxLength = 255;
    public const int StatusMaxLength = 32;

    private ApplicationNotice() { }

    public ApplicationNotice(
        Guid userId,
        string messageId,
        string company,
        string? role,
        string status,
        DateTime sentAtUtc,
        string content)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("A notice requires a person.", nameof(userId));
        if (string.IsNullOrWhiteSpace(messageId) || messageId.Length > MessageIdMaxLength)
            throw new ArgumentException("A notice requires a Gmail message id.", nameof(messageId));
        if (string.IsNullOrWhiteSpace(company))
            throw new ArgumentException("A notice requires a company.", nameof(company));
        if (NoticeStatus.FromLabel(status) is null)
            throw new ArgumentException("A notice requires a known status.", nameof(status));
        if (sentAtUtc == default)
            throw new ArgumentException("A notice requires the time the mail was sent.", nameof(sentAtUtc));
        if (content is null)
            throw new ArgumentException("A notice requires message content.", nameof(content));

        Id = Guid.CreateVersion7();
        UserId = userId;
        MessageId = messageId;
        Company = company.Trim();
        Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
        Status = status;
        SentAt = DateTime.SpecifyKind(sentAtUtc, DateTimeKind.Utc);
        Content = content;
        CreatedAt = DateTime.UtcNow;
    }

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
