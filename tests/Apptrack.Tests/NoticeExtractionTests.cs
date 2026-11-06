using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Apptrack.Services.NoticeExtraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Apptrack.Tests;

public sealed class LlmNoticeExtractorTests
{
    private const string Route = "https://llm.example/v1/";

    [Fact]
    public async Task A_grounded_completion_is_an_application_notice_posted_to_the_configured_route()
    {
        var content = """
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We received your application for Engineer.
            """;
        var completion = """{"company":"Acme","role":"Engineer","status":"Application Sent"}""";
        HttpRequestMessage? sent = null;
        string? body = null;
        var extractor = Extractor(request =>
        {
            sent = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(completion);
        });

        var notice = await extractor.ExtractAsync("msg-1", content, CancellationToken.None);

        Assert.NotNull(notice);
        Assert.Equal("Acme", notice.Company);
        Assert.Equal(ApplicationRole.Named("Engineer"), notice.Role);
        Assert.Equal(ApplicationStatus.ApplicationSent, notice.Status);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 14, 30, 0, TimeSpan.Zero), notice.SentAt);
        Assert.Equal(new Uri($"{Route}chat/completions"), sent!.RequestUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "test-key"), sent.Headers.Authorization);
        Assert.Contains("json_schema", body, StringComparison.Ordinal);
        Assert.Contains("configured-model", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_role_is_unnamed()
    {
        var content = """
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We received your application.
            """;
        var completion = """{"company":"Acme","role":null,"status":"Application Sent"}""";
        var extractor = Extractor(_ => JsonResponse(completion));

        var notice = await extractor.ExtractAsync("msg-1", content, CancellationToken.None);

        Assert.NotNull(notice);
        Assert.Equal(ApplicationRole.Unnamed, notice.Role);
    }

    [Fact]
    public async Task A_notice_without_a_sent_time_is_not_returned()
    {
        var content = "From: Acme Careers <jobs@acme.com>\nSubject: Thanks for applying\n\nWe received your application.";
        var completion = """{"company":"Acme","role":null,"status":"Application Sent"}""";
        var extractor = Extractor(_ => JsonResponse(completion));

        var notice = await extractor.ExtractAsync("msg-1", content, CancellationToken.None);

        Assert.Null(notice);
    }

    [Fact]
    public async Task A_company_that_appears_only_as_a_domain_is_not_a_notice()
    {
        var content = """
            From: <noreply@globex.com>
            Subject: Update
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We moved forward with other candidates.
            """;
        var completion = """{"company":"Globex","role":null,"status":"Rejected"}""";
        var extractor = Extractor(_ => JsonResponse(completion));

        var notice = await extractor.ExtractAsync("msg-1", content, CancellationToken.None);

        Assert.Null(notice);
    }

    [Fact]
    public async Task Two_candidacies_are_not_narrowed_to_one()
    {
        var content = """
            From: Acme <jobs@acme.com>
            Subject: Applications
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            Engineer and Designer at Acme.
            """;
        var completion = """
            {"candidacies":[
              {"company":"Acme","role":"Engineer","status":"Application Sent"},
              {"company":"Acme","role":"Designer","status":"Application Sent"}
            ]}
            """;
        var extractor = Extractor(_ => JsonResponse(completion));

        var notice = await extractor.ExtractAsync("msg-1", content, CancellationToken.None);

        Assert.Null(notice);
    }

    [Fact]
    public async Task An_offer_status_is_not_a_notice()
    {
        var content = """
            From: Acme <jobs@acme.com>
            Subject: Offer
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We are pleased to offer you the role at Acme.
            """;
        var completion = """{"company":"Acme","role":"Engineer","status":"Offer"}""";
        var extractor = Extractor(_ => JsonResponse(completion));

        var notice = await extractor.ExtractAsync("msg-1", content, CancellationToken.None);

        Assert.Null(notice);
    }

    [Fact]
    public async Task A_rejected_classification_is_logged_with_the_message_and_the_reason()
    {
        var content = """
            From: Acme <jobs@acme.com>
            Subject: Offer
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We are pleased to offer you the Engineer role at Acme.
            """;
        var completion = """{"company":"Acme","role":"Engineer","status":"Offer"}""";
        var logger = new CollectingLogger();
        var extractor = Extractor(_ => JsonResponse(completion), logger: logger);

        var notice = await extractor.ExtractAsync("msg-9", content, CancellationToken.None);

        Assert.Null(notice);
        var line = Assert.Single(logger.Lines);
        Assert.Contains("msg-9", line);
        Assert.Contains("The status is not an application notice status.", line);
        Assert.Contains("Company: Acme", line);
        Assert.Contains("Role: Engineer", line);
        Assert.Contains("Status: Offer", line);
        Assert.Contains(content, line);
    }

    [Fact]
    public async Task A_completion_that_is_not_json_is_unavailable()
    {
        var extractor = Extractor(_ => JsonResponse("not json"));

        var ex = await Assert.ThrowsAsync<NoticeExtractionUnavailableException>(() =>
            extractor.ExtractAsync("msg-1", "From: Acme\n\nThanks", CancellationToken.None));

        Assert.Equal("The classification was not JSON.", ex.Message);
    }

    [Fact]
    public async Task A_failed_response_is_unavailable()
    {
        var extractor = Extractor(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        await Assert.ThrowsAsync<NoticeExtractionUnavailableException>(() =>
            extractor.ExtractAsync("msg-1", "From: Acme\n\nThanks", CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_model_fails_before_the_request()
    {
        var called = false;
        var extractor = Extractor(
            _ =>
            {
                called = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            new Dictionary<string, string?>
            {
                ["Ai:ApiKey"] = "test-key",
                ["Ai:BaseUrl"] = Route,
            });

        var ex = await Assert.ThrowsAsync<NoticeExtractionUnavailableException>(() =>
            extractor.ExtractAsync("msg-1", "From: Acme\n\nThanks", CancellationToken.None));

        Assert.Equal("Ai:Model is not configured.", ex.Message);
        Assert.False(called);
    }

    private static AiNoticeExtractor Extractor(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        Dictionary<string, string?>? settings = null,
        ILogger<AiNoticeExtractor>? logger = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>
            {
                ["Ai:ApiKey"] = "test-key",
                ["Ai:Model"] = "configured-model",
                ["Ai:BaseUrl"] = Route,
            })
            .Build();
        return new AiNoticeExtractor(configuration, new StubHandler(respond), logger);
    }

    private sealed class CollectingLogger : ILogger<AiNoticeExtractor>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
        }
    }

    private static HttpResponseMessage JsonResponse(string completion)
    {
        var body = "{\"choices\":[{\"message\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(completion) + "}}]}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
