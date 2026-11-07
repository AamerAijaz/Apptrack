using Apptrack.Services.NoticeExtraction;
using Xunit;

namespace Apptrack.Tests;

public sealed class StatusContractTests
{
    [Fact]
    public void Display_names_are_the_three_statuses_in_order()
    {
        var names = Enum.GetValues<ApplicationStatus>().Select(s => s.DisplayName());

        Assert.Equal(["Application Sent", "Interviewing", "Rejected"], names);
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
    public void Stored_notice_rejects_other_statuses()
    {
        Assert.Throws<ArgumentException>(() => new Entities.ApplicationNotice(
            Guid.CreateVersion7(), "m1", "Acme", null, "Offer", DateTime.UtcNow, "body"));
    }
}
