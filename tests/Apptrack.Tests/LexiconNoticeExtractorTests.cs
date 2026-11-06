using Apptrack.Services.NoticeExtraction;
using Xunit;

namespace Apptrack.Tests;

public sealed class LexiconNoticeExtractorTests
{
    private static readonly DateTimeOffset SentAt = new(2026, 11, 3, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Thanks_for_applying_is_application_sent_with_the_display_name_and_the_date_header()
    {
        var notice = await Read("""
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We will write soon.
            """);

        AssertNotice(notice, "Acme Careers", ApplicationRole.Unnamed, ApplicationStatus.ApplicationSent, SentAt);
    }

    [Theory]
    [InlineData("thank you for applying")]
    [InlineData("application received")]
    [InlineData("we received your application")]
    [InlineData("under review")]
    [InlineData("THANKS FOR APPLYING")]
    public async Task An_acknowledgement_phrase_is_application_sent(string phrase)
    {
        var notice = await Read($"""
            From: Acme Careers <jobs@acme.com>
            Subject: Update
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            {phrase}
            """);

        AssertNotice(notice, "Acme Careers", ApplicationRole.Unnamed, ApplicationStatus.ApplicationSent, SentAt);
    }

    [Theory]
    [InlineData("interview")]
    [InlineData("interviewing")]
    [InlineData("phone screen")]
    [InlineData("take-home")]
    [InlineData("assessment")]
    public async Task An_interview_phrase_is_interviewing(string phrase)
    {
        var notice = await Read($"""
            From: Acme Careers <jobs@acme.com>
            Subject: Update
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            Please see this {phrase} note.
            """);

        AssertNotice(notice, "Acme Careers", ApplicationRole.Unnamed, ApplicationStatus.Interviewing, SentAt);
    }

    [Theory]
    [InlineData("other candidates")]
    [InlineData("not moving forward")]
    [InlineData("will not be progressing")]
    public async Task A_rejection_phrase_is_rejected(string phrase)
    {
        var notice = await Read($"""
            From: Acme Careers <jobs@acme.com>
            Subject: Update
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We chose {phrase} for this candidacy.
            """);

        AssertNotice(notice, "Acme Careers", ApplicationRole.Unnamed, ApplicationStatus.Rejected, SentAt);
    }

    [Fact]
    public async Task A_word_that_only_contains_a_status_phrase_is_not_a_notice()
    {
        var notice = await Read("""
            From: Acme Careers <jobs@acme.com>
            Subject: Hello
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            The interviewer will call.
            """);

        Assert.Null(notice);
    }

    [Fact]
    public async Task Thanks_for_applying_with_a_rejection_is_rejected()
    {
        var notice = await Read("""
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We are not moving forward.
            """);

        AssertNotice(notice, "Acme Careers", ApplicationRole.Unnamed, ApplicationStatus.Rejected, SentAt);
    }

    [Fact]
    public async Task Thanks_for_applying_with_an_interview_is_interviewing()
    {
        var notice = await Read("""
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            Please join us for an interview.
            """);

        AssertNotice(notice, "Acme Careers", ApplicationRole.Unnamed, ApplicationStatus.Interviewing, SentAt);
    }

    [Fact]
    public async Task Interviewing_and_rejected_together_are_not_a_notice()
    {
        var notice = await Read("""
            From: Acme Careers <jobs@acme.com>
            Subject: Interview
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We are moving ahead with other candidates.
            """);

        Assert.Null(notice);
    }

    [Theory]
    [InlineData("offer")]
    [InlineData("newsletter")]
    [InlineData("verify your email")]
    [InlineData("job alert")]
    [InlineData("on hold")]
    [InlineData("withdrawn")]
    public async Task An_abstain_phrase_wins_over_an_acknowledgement(string phrase)
    {
        var notice = await Read($"""
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            This message is a {phrase} update.
            """);

        Assert.Null(notice);
    }

    [Fact]
    public async Task Ordinary_mail_is_not_a_notice()
    {
        var notice = await Read("""
            From: Acme Careers <jobs@acme.com>
            Subject: Lunch
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            Are you free tomorrow?
            """);

        Assert.Null(notice);
    }

    [Fact]
    public async Task A_quoted_display_name_is_the_company_in_full()
    {
        var notice = await Read("""
            From: "Acme Careers" <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We will write soon.
            """);

        AssertNotice(notice, "Acme Careers", ApplicationRole.Unnamed, ApplicationStatus.ApplicationSent, SentAt);
    }

    [Fact]
    public async Task A_from_line_that_is_only_an_address_does_not_invent_a_company_from_the_domain()
    {
        var notice = await Read("""
            From: <noreply@globex.com>
            Subject: Update
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We moved ahead with other candidates.
            """);

        Assert.Null(notice);
    }

    [Fact]
    public async Task A_display_name_that_disappears_once_addresses_are_removed_is_not_a_notice()
    {
        var notice = await Read("""
            From: Acme jobs@acme.com <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We will write soon.
            """);

        Assert.Null(notice);
    }

    [Theory]
    [InlineData("We received your application at Acme.")]
    [InlineData("We received your application from Acme.")]
    [InlineData("We received your application at the Acme.")]
    public async Task The_company_is_read_from_at_or_from_when_there_is_no_display_name(string body)
    {
        var notice = await Read($"""
            From: <jobs@acme.com>
            Subject: Update
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            {body}
            """);

        AssertNotice(notice, "Acme", ApplicationRole.Unnamed, ApplicationStatus.ApplicationSent, SentAt);
    }

    [Fact]
    public async Task The_longer_company_span_is_kept_when_one_sits_inside_the_other()
    {
        var notice = await Read("""
            From: <jobs@acme.com>
            Subject: at Acme
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We received your application at Acme Careers.
            """);

        AssertNotice(notice, "Acme Careers", ApplicationRole.Unnamed, ApplicationStatus.ApplicationSent, SentAt);
    }

    [Fact]
    public async Task Two_different_companies_are_not_a_notice()
    {
        var notice = await Read("""
            From: <jobs@acme.com>
            Subject: at Acme
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We received your application at Globex.
            """);

        Assert.Null(notice);
    }

    [Theory]
    [InlineData("We received your application for Engineer.", "Engineer")]
    [InlineData("Thanks for applying for Engineer.", "Engineer")]
    [InlineData("The position of Engineer.", "Engineer")]
    [InlineData("Position: Engineer", "Engineer")]
    [InlineData("The role of Engineer.", "Engineer")]
    [InlineData("Role: Engineer", "Engineer")]
    [InlineData("Engineer position", "Engineer")]
    public async Task A_single_title_pattern_names_the_role(string body, string title)
    {
        var notice = await Read($"""
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            {body}
            """);

        AssertNotice(notice, "Acme Careers", ApplicationRole.Named(title), ApplicationStatus.ApplicationSent, SentAt);
    }

    [Fact]
    public async Task An_interview_for_a_title_is_interviewing_with_that_role()
    {
        var notice = await Read("""
            From: Acme Careers <jobs@acme.com>
            Subject: Update
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We scheduled an interview for Engineer.
            """);

        AssertNotice(notice, "Acme Careers", ApplicationRole.Named("Engineer"), ApplicationStatus.Interviewing, SentAt);
    }

    [Fact]
    public async Task The_same_title_named_twice_is_one_role()
    {
        var notice = await Read("""
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We received your application for Engineer. The position of Engineer.
            """);

        AssertNotice(notice, "Acme Careers", ApplicationRole.Named("Engineer"), ApplicationStatus.ApplicationSent, SentAt);
    }

    [Fact]
    public async Task Two_different_titles_are_not_a_notice()
    {
        var notice = await Read("""
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We received your application for Engineer. The position of Designer.
            """);

        Assert.Null(notice);
    }

    [Fact]
    public async Task A_title_containing_and_is_not_a_notice()
    {
        var notice = await Read("""
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We received your application for Engineer and Designer.
            """);

        Assert.Null(notice);
    }

    [Fact]
    public async Task The_sent_time_keeps_the_offset_in_the_date_header()
    {
        var notice = await Read("""
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0530

            We will write soon.
            """);

        AssertNotice(
            notice,
            "Acme Careers",
            ApplicationRole.Unnamed,
            ApplicationStatus.ApplicationSent,
            new DateTimeOffset(2026, 11, 3, 14, 30, 0, TimeSpan.FromMinutes(330)));
    }

    [Fact]
    public async Task A_missing_date_header_is_not_a_notice()
    {
        var notice = await Read("""
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying

            Date: Tue, 03 Nov 2026 14:30:00 +0000
            We will write soon.
            """);

        Assert.Null(notice);
    }

    [Fact]
    public async Task An_unparseable_date_header_is_not_a_notice()
    {
        var notice = await Read("""
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: yesterday

            We will write soon.
            """);

        Assert.Null(notice);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_content_is_not_a_notice(string content)
    {
        var notice = await new LexiconNoticeExtractor().ExtractAsync("msg-1", content, CancellationToken.None);

        Assert.Null(notice);
    }

    [Fact]
    public async Task A_from_display_name_that_says_interview_does_not_set_the_status()
    {
        var notice = await Read("""
            From: Interview Team <jobs@acme.com>
            Subject: Hello
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            Your package is enclosed.
            """);

        Assert.Null(notice);
    }

    private static Task<ApplicationNotice?> Read(string content) =>
        new LexiconNoticeExtractor().ExtractAsync("msg-1", content, CancellationToken.None);

    private static void AssertNotice(
        ApplicationNotice? notice,
        string company,
        ApplicationRole role,
        ApplicationStatus status,
        DateTimeOffset sentAt)
    {
        Assert.NotNull(notice);
        Assert.Equal(company, notice.Company);
        Assert.Equal(role, notice.Role);
        Assert.Equal(status, notice.Status);
        Assert.Equal(sentAt, notice.SentAt);
    }
}
