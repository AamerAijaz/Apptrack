using Apptrack.Services.NoticeExtraction;

namespace Apptrack.Services;

/// <summary>
/// The ordered kanban statuses. The label is the value stored on a notice.
/// </summary>
public sealed record NoticeStatus
{
    private NoticeStatus(string label) => Label = label;

    public string Label { get; }

    public static NoticeStatus ApplicationSent { get; } = new("Application Sent");
    public static NoticeStatus Interviewing { get; } = new("Interviewing");
    public static NoticeStatus Accepted { get; } = new("Accepted");
    public static NoticeStatus Rejected { get; } = new("Rejected");

    /// <summary>Lane order. The check constraint, the entity, and the board read this list.</summary>
    public static IReadOnlyList<NoticeStatus> All { get; } =
        [ApplicationSent, Interviewing, Accepted, Rejected];

    public static NoticeStatus? FromLabel(string? label) =>
        All.FirstOrDefault(status => status.Label == label);

    public static string CheckConstraintSql { get; } =
        "\"Status\" IN (" + string.Join(", ", All.Select(status => $"'{status.Label}'")) + ")";

    public static bool MatchesEnum()
    {
        var names = Enum.GetValues<ApplicationStatus>().Select(status => status.DisplayName()).ToArray();
        return names.SequenceEqual(All.Select(status => status.Label));
    }
}
