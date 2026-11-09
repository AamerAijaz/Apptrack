using System.Text.Json;
using Apptrack.Services;
using Apptrack.Services.NoticeExtraction;
using Xunit;

namespace Apptrack.Tests;

public sealed class StatusContractTests
{
    [Fact]
    public void Display_names_are_the_three_statuses_in_order()
    {
        var names = Enum.GetValues<ApplicationStatus>().Select(s => s.DisplayName());

        Assert.Equal(["Application Sent", "Interviewing", "Accepted", "Rejected"], names);
        Assert.True(NoticeStatus.MatchesEnum());
    }

    [Fact]
    public void Stored_notice_accepts_every_display_name()
    {
        foreach (var status in Enum.GetValues<ApplicationStatus>())
        {
            var notice = new Entities.ApplicationNotice(
                Guid.CreateVersion7(), "m1", "Acme", null, status.DisplayName(), DateTime.UtcNow, "body");

            Assert.Equal(status.DisplayName(), notice.Status);
        }
    }

    [Fact]
    public void Notice_status_labels_match_the_shared_contract()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contracts", "notice-statuses.json")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.FullName, "contracts", "notice-statuses.json")));
        var labels = json.RootElement.EnumerateArray().Select(item => item.GetProperty("label").GetString()).ToArray();

        Assert.Equal(NoticeStatus.All.Select(status => status.Label), labels);
    }

    [Fact]
    public void Stored_notice_rejects_other_statuses()
    {
        Assert.Throws<ArgumentException>(() => new Entities.ApplicationNotice(
            Guid.CreateVersion7(), "m1", "Acme", null, "Offer", DateTime.UtcNow, "body"));
    }
}
