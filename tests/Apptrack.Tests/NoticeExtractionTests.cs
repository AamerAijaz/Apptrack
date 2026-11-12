using System.ClientModel;
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
    public async Task A_notice_without_a_sent_time_falls_back_to_the_current_time()
    {
        var content = "From: Acme Careers <jobs@acme.com>\nSubject: Thanks for applying\n\nWe received your application.";
        var completion = """{"company":"Acme","role":null,"status":"Application Sent"}""";
        var extractor = Extractor(_ => JsonResponse(completion));
        var before = DateTimeOffset.UtcNow;

        var notice = await extractor.ExtractAsync("msg-1", content, CancellationToken.None);

        Assert.NotNull(notice);
        Assert.InRange(notice.SentAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task An_unparseable_date_header_falls_back_to_the_current_time()
    {
        var content = """
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: yesterday

            We received your application.
            """;
        var completion = """{"company":"Acme","role":null,"status":"Application Sent"}""";
        var extractor = Extractor(_ => JsonResponse(completion));
        var before = DateTimeOffset.UtcNow;

        var notice = await extractor.ExtractAsync("msg-1", content, CancellationToken.None);

        Assert.NotNull(notice);
        Assert.InRange(notice.SentAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task A_readable_date_header_wins_over_the_current_time()
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
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 14, 30, 0, TimeSpan.Zero), notice.SentAt);
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
    public async Task An_offer_is_an_accepted_notice()
    {
        var content = """
            From: Acme <jobs@acme.com>
            Subject: Offer
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We are pleased to offer you the role at Acme.
            """;
        var completion = """{"company":"Acme","role":"Engineer","status":"Accepted"}""";
        var extractor = Extractor(_ => JsonResponse(completion));

        var notice = await extractor.ExtractAsync("msg-1", content, CancellationToken.None);

        Assert.NotNull(notice);
        Assert.Equal(ApplicationStatus.Accepted, notice.Status);
        Assert.Equal("Acme", notice.Company);
        Assert.Equal(ApplicationRole.Named("Engineer"), notice.Role);
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
        var line = Assert.Single(logger.Lines, entry => entry.Contains("Rejected application notice", StringComparison.Ordinal));
        Assert.Contains("msg-9", line);
        Assert.Contains("The status is not an application notice status.", line);
        Assert.Contains("Company: Acme", line);
        Assert.Contains("Role: Engineer", line);
        Assert.Contains("Status: Offer", line);
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

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.PaymentRequired)]
    public async Task A_usage_limited_model_hands_later_calls_to_the_next_model(HttpStatusCode limit)
    {
        const string first = "https://llm-a.example/v1/";
        const string second = "https://llm-b.example/v1/";
        var calls = new List<Uri>();
        var extractor = Extractor(
            request =>
            {
                calls.Add(request.RequestUri!);
                if (request.RequestUri!.AbsoluteUri.StartsWith(first, StringComparison.Ordinal))
                    return new HttpResponseMessage(limit);
                return JsonResponse("""{"company":"Acme","role":"Engineer","status":"Application Sent"}""");
            },
            TwoModels(first, second));
        const string content = """
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We received your application for Engineer.
            """;

        var firstNotice = await extractor.ExtractAsync("msg-1", content, CancellationToken.None);
        var secondNotice = await extractor.ExtractAsync("msg-2", content, CancellationToken.None);

        Assert.NotNull(firstNotice);
        Assert.Equal("Acme", firstNotice.Company);
        Assert.NotNull(secondNotice);
        Assert.Equal(
            new[]
            {
                new Uri($"{first}chat/completions"),
                new Uri($"{second}chat/completions"),
                new Uri($"{second}chat/completions"),
            },
            calls);
    }

    [Fact]
    public async Task Every_usage_limited_model_is_unavailable()
    {
        const string first = "https://llm-a.example/v1/";
        const string second = "https://llm-b.example/v1/";
        var calls = new List<Uri>();
        var extractor = Extractor(
            request =>
            {
                calls.Add(request.RequestUri!);
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            },
            TwoModels(first, second));

        var ex = await Assert.ThrowsAsync<NoticeExtractionUnavailableException>(() =>
            extractor.ExtractAsync("msg-1", "From: Acme\n\nThanks", CancellationToken.None));

        Assert.Equal("Every configured language model is usage-limited.", ex.Message);
        var limit = Assert.IsType<ClientResultException>(ex.InnerException);
        Assert.Equal(429, limit.Status);
        Assert.Equal(
            new[]
            {
                new Uri($"{first}chat/completions"),
                new Uri($"{second}chat/completions"),
            },
            calls);
    }

    [Fact]
    public async Task A_usage_limited_model_is_unavailable_when_it_is_the_only_one()
    {
        var calls = 0;
        var extractor = Extractor(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        });

        var ex = await Assert.ThrowsAsync<NoticeExtractionUnavailableException>(() =>
            extractor.ExtractAsync("msg-1", "From: Acme\n\nThanks", CancellationToken.None));

        Assert.Equal("The language model returned 429.", ex.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_failed_model_does_not_hand_off_to_the_next_model()
    {
        const string first = "https://llm-a.example/v1/";
        const string second = "https://llm-b.example/v1/";
        var firstCalls = 0;
        var secondCalls = 0;
        var extractor = Extractor(
            request =>
            {
                if (request.RequestUri!.AbsoluteUri.StartsWith(second, StringComparison.Ordinal))
                    secondCalls++;
                else
                    firstCalls++;
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            },
            TwoModels(first, second));

        var ex = await Assert.ThrowsAsync<NoticeExtractionUnavailableException>(() =>
            extractor.ExtractAsync("msg-1", "From: Acme\n\nThanks", CancellationToken.None));

        Assert.Equal("The language model returned 502.", ex.Message);
        Assert.Equal(0, secondCalls);
        Assert.True(firstCalls >= 1);
    }

    [Fact]
    public async Task A_rejected_classification_does_not_try_the_next_model()
    {
        const string first = "https://llm-a.example/v1/";
        const string second = "https://llm-b.example/v1/";
        var calls = new List<Uri>();
        var content = """
            From: <noreply@globex.com>
            Subject: Update
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We moved forward with other candidates.
            """;
        var extractor = Extractor(
            request =>
            {
                calls.Add(request.RequestUri!);
                return JsonResponse("""{"company":"Globex","role":null,"status":"Rejected"}""");
            },
            TwoModels(first, second));

        var notice = await extractor.ExtractAsync("msg-1", content, CancellationToken.None);

        Assert.Null(notice);
        Assert.Equal(new[] { new Uri($"{first}chat/completions") }, calls);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("02")]
    public async Task An_invalid_model_slot_fails_before_the_request(string key)
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
                ["Ai:Model"] = "configured-model",
                ["Ai:BaseUrl"] = Route,
                [$"Ai:{key}:Model"] = "extra",
            });

        var ex = await Assert.ThrowsAsync<NoticeExtractionUnavailableException>(() =>
            extractor.ExtractAsync("msg-1", "From: Acme\n\nThanks", CancellationToken.None));

        Assert.Equal($"Ai:{key} is not a model slot. Extra models are Ai:2, Ai:3, and so on.", ex.Message);
        Assert.False(called);
    }

    [Fact]
    public async Task A_partial_extra_model_fails_before_the_request()
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
                ["Ai:Model"] = "configured-model",
                ["Ai:BaseUrl"] = Route,
                ["Ai:2:ApiKey"] = "key-2",
            });

        var ex = await Assert.ThrowsAsync<NoticeExtractionUnavailableException>(() =>
            extractor.ExtractAsync("msg-1", "From: Acme\n\nThanks", CancellationToken.None));

        Assert.Equal("Ai:2:Model is not configured.", ex.Message);
        Assert.False(called);
    }

    [Fact]
    public async Task Root_settings_match_when_their_names_differ_in_case()
    {
        var content = """
            From: Acme Careers <jobs@acme.com>
            Subject: Thanks for applying
            Date: Tue, 03 Nov 2026 14:30:00 +0000

            We received your application for Engineer.
            """;
        var called = false;
        var extractor = Extractor(
            _ =>
            {
                called = true;
                return JsonResponse("""{"company":"Acme","role":"Engineer","status":"Application Sent"}""");
            },
            new Dictionary<string, string?>
            {
                ["Ai:apikey"] = "test-key",
                ["Ai:model"] = "configured-model",
                ["Ai:baseurl"] = Route,
            });

        var notice = await extractor.ExtractAsync("msg-1", content, CancellationToken.None);

        Assert.NotNull(notice);
        Assert.Equal("Acme", notice.Company);
        Assert.True(called);
    }

    private static Dictionary<string, string?> TwoModels(string first, string second) => new()
    {
        ["Ai:ApiKey"] = "test-key",
        ["Ai:Model"] = "first-model",
        ["Ai:BaseUrl"] = first,
        ["Ai:2:ApiKey"] = "key-2",
        ["Ai:2:Model"] = "second-model",
        ["Ai:2:BaseUrl"] = second,
    };

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
