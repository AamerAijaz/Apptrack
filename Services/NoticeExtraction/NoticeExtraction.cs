namespace Apptrack.Services.NoticeExtraction;

/// <summary>
/// The only statuses an application notice can carry.
/// </summary>
public enum ApplicationStatus
{
    ApplicationSent,
    Interviewing,
    Rejected,
}

public static class ApplicationStatusText
{
    public static string DisplayName(this ApplicationStatus status) => status switch
    {
        ApplicationStatus.ApplicationSent => "Application Sent",
        ApplicationStatus.Interviewing => "Interviewing",
        ApplicationStatus.Rejected => "Rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryFromDisplayName(string? label, out ApplicationStatus status)
    {
        foreach (var candidate in Enum.GetValues<ApplicationStatus>())
        {
            if (string.Equals(candidate.DisplayName(), label, StringComparison.Ordinal))
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }
}

/// <summary>
/// The position on an application notice. Unnamed when the mail does not specify it.
/// </summary>
public readonly record struct ApplicationRole
{
    private ApplicationRole(string? title) => Title = title;

    public string? Title { get; }

    public static ApplicationRole Unnamed { get; } = new(null);

    public static ApplicationRole Named(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A named role requires a title.", nameof(title));
        return new(title.Trim());
    }
}

/// <summary>
/// One application notice read from a message. Company and the time the mail was sent are required.
/// </summary>
public sealed record ApplicationNotice
{
    public ApplicationNotice(string company, ApplicationRole role, ApplicationStatus status, DateTimeOffset sentAt)
    {
        if (string.IsNullOrWhiteSpace(company))
            throw new ArgumentException("A notice requires a company.", nameof(company));
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (sentAt == default)
            throw new ArgumentException("A notice requires the time the mail was sent.", nameof(sentAt));

        Company = company.Trim();
        Role = role;
        Status = status;
        SentAt = sentAt;
    }

    public string Company { get; }
    public ApplicationRole Role { get; }
    public ApplicationStatus Status { get; }
    public DateTimeOffset SentAt { get; }
}

public interface IApplicationNoticeExtractor
{
    /// <summary>
    /// Reads one application notice from a message content string.
    /// Returns null when the content is not exactly one notice.
    /// When the content has no readable Date header, the sent time falls back to the current time.
    /// Throws <see cref="NoticeExtractionUnavailableException"/> when the vendor cannot return a trustworthy answer.
    /// </summary>
    Task<ApplicationNotice?> ExtractAsync(string messageId, string content, CancellationToken ct);
}

public sealed class NoticeExtractionUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
