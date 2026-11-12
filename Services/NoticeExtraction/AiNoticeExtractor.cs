using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace Apptrack.Services.NoticeExtraction;

/// <summary>
/// Reads one application notice from message content with a language model.
/// Chat completions go through the OpenAI SDK. The first model is <c>Ai:BaseUrl</c>,
/// <c>Ai:Model</c>, and <c>Ai:ApiKey</c>. Further models are <c>Ai:2</c>, <c>Ai:3</c>, and so on.
/// A usage-limit response sticks later calls on the next model.
/// The sent time is taken from the content's Date header, not from the model, and falls back
/// to the current time when that header is missing or cannot be read.
/// </summary>
public sealed partial class AiNoticeExtractor(
    IConfiguration configuration,
    HttpMessageHandler? handler = null,
    ILogger<AiNoticeExtractor>? logger = null) : IApplicationNoticeExtractor
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly BinaryData NoticeSchema = BinaryData.FromString("""
        {
          "type": "object",
          "properties": {
            "company": { "anyOf": [{ "type": "string" }, { "type": "null" }] },
            "role": { "anyOf": [{ "type": "string" }, { "type": "null" }] },
            "status": { "anyOf": [{ "type": "string" }, { "type": "null" }] }
          },
          "required": ["company", "role", "status"],
          "additionalProperties": false
        }
        """);

    // A successful read stays cached. A failed read does not, so a later attempt can see corrected settings.
    private readonly Lazy<ModelSlot[]> _slots = new(
        () => ModelSlot.ReadAll(configuration, handler, logger),
        LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// The first model to try is this value modulo the slot count.
    /// A call walks forward from the value it read. Only <see cref="Advance"/> writes it,
    /// and only when the cursor is still the slot that call just found limited.
    /// </summary>
    private long _cursor;

    public async Task<ApplicationNotice?> ExtractAsync(string messageId, string content, CancellationToken ct)
    {
        var completion = await ParseContentAsync(content, ct);
        var text = completion.Content.Count > 0 ? completion.Content[0].Text : null;
        if (string.IsNullOrWhiteSpace(text))
            throw new NoticeExtractionUnavailableException("The language model returned no classification.");

        var notice = Classification.Interpret(content, text, DateTimeOffset.UtcNow, out var rejection);
        if (notice is null)
        {
            logger?.LogInformation(
                "Rejected application notice {MessageId}: {Reason} | Company: {Company} | Role: {Role} | Status: {Status}",
                messageId,
                rejection.Reason,
                rejection.Company ?? "null",
                rejection.Role ?? "null",
                rejection.Status ?? "null");
        }

        return notice;
    }

    /// <summary>
    /// Attempts a model call to parse the email content.
    /// Retries if an exception is thrown due to usage limits and more than one model has been configured.
    /// </summary>
    private async Task<ChatCompletion> ParseContentAsync(string content, CancellationToken ct)
    {
        var slots = _slots.Value;
        var start = Volatile.Read(ref _cursor);
        ClientResultException? lastLimit = null;
        for (var attempt = 0; attempt < slots.Length; attempt++)
        {
            var observed = start + attempt;
            var slot = slots[(int)(observed % slots.Length)];
            try
            {
                return await slot.Chat.CompleteChatAsync(
                    [
                        new SystemChatMessage(Classification.SystemPrompt),
                        new UserChatMessage(content),
                    ],
                    new ChatCompletionOptions
                    {
                        Temperature = 0,
                        ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                            "application_notice",
                            NoticeSchema,
                            jsonSchemaIsStrict: true),
                    },
                    ct);
            }
            catch (ClientResultException ex) when (IsUsageLimit(ex.Status))
            {
                lastLimit = ex;
                Advance(observed);
            }
            catch (ClientResultException ex)
            {
                throw new NoticeExtractionUnavailableException($"The language model returned {ex.Status}.", ex);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new NoticeExtractionUnavailableException("The language model request timed out.");
            }
        }

        if (slots.Length == 1)
            throw new NoticeExtractionUnavailableException($"The language model returned {lastLimit!.Status}.", lastLimit);

        throw new NoticeExtractionUnavailableException(
            "Every configured language model is usage-limited.",
            lastLimit);
    }

    private void Advance(long observed) =>
        Interlocked.CompareExchange(ref _cursor, observed + 1, observed);

    private static bool IsUsageLimit(int status) =>
        status is (int)HttpStatusCode.TooManyRequests or (int)HttpStatusCode.PaymentRequired;

    private static Uri BaseEndpoint(Uri route)
    {
        var text = route.AbsoluteUri;
        return text.EndsWith('/') ? route : new Uri(text + "/");
    }

    private sealed class WorkspaceHeaderPolicy(string workspaceId) : PipelinePolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set("anthropic-workspace-id", workspaceId);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set("anthropic-workspace-id", workspaceId);
            return ProcessNextAsync(message, pipeline, currentIndex);
        }
    }

    private sealed record ModelSlot(string Section, string Model, ChatClient Chat)
    {
        public static ModelSlot[] ReadAll(
            IConfiguration configuration,
            HttpMessageHandler? handler,
            ILogger? logger)
        {
            try
            {
                var slots = Read(configuration, handler);
                logger?.LogInformation(
                    "Notice extraction models: {Models}",
                    string.Join(", ", slots.Select(slot => $"{slot.Section} {slot.Model}")));
                return slots;
            }
            catch (NoticeExtractionUnavailableException ex)
            {
                logger?.LogError("{Message}", ex.Message);
                throw;
            }
        }

        private static ModelSlot[] Read(IConfiguration configuration, HttpMessageHandler? handler)
        {
            var root = configuration.GetSection("Ai");
            var extras = new List<IConfigurationSection>();
            foreach (var child in root.GetChildren())
            {
                if (IsRootLeaf(child.Key))
                    continue;
                if (!IsExtraSlot(child.Key))
                    throw new NoticeExtractionUnavailableException(
                        $"Ai:{child.Key} is not a model slot. Extra models are Ai:2, Ai:3, and so on.");
                extras.Add(child);
            }

            extras.Sort((left, right) => SlotNumber(left.Key).CompareTo(SlotNumber(right.Key)));
            var slots = new ModelSlot[1 + extras.Count];
            slots[0] = Build(AiSettings.Read(root), handler);
            for (var i = 0; i < extras.Count; i++)
                slots[i + 1] = Build(AiSettings.Read(extras[i]), handler);
            return slots;
        }

        private static bool IsRootLeaf(string key) =>
            key.Equals("ApiKey", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Model", StringComparison.OrdinalIgnoreCase)
            || key.Equals("BaseUrl", StringComparison.OrdinalIgnoreCase)
            || key.Equals("WorkspaceId", StringComparison.OrdinalIgnoreCase);

        private static bool IsExtraSlot(string key) =>
            int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number >= 2
            && key == number.ToString(CultureInfo.InvariantCulture);

        private static int SlotNumber(string key) =>
            int.Parse(key, NumberStyles.None, CultureInfo.InvariantCulture);

        private static ModelSlot Build(AiSettings settings, HttpMessageHandler? handler)
        {
            var options = new OpenAIClientOptions
            {
                Endpoint = BaseEndpoint(settings.BaseUrl),
                NetworkTimeout = RequestTimeout,
                RetryPolicy = new UsageLimitRetryPolicy(),
            };
            if (handler is not null)
                options.Transport = new HttpClientPipelineTransport(new HttpClient(handler, disposeHandler: false));
            if (settings.WorkspaceId is not null)
                options.AddPolicy(new WorkspaceHeaderPolicy(settings.WorkspaceId), PipelinePosition.PerCall);

            return new ModelSlot(
                settings.Section,
                settings.Model,
                new ChatClient(settings.Model, new ApiKeyCredential(settings.ApiKey), options));
        }
    }

    /// <summary>
    /// Keeps the SDK retry budget, except a 429 or 402 is not retried.
    /// That includes a setup with one model. The caller retries the message.
    /// </summary>
    private sealed class UsageLimitRetryPolicy() : ClientRetryPolicy(3)
    {
        protected override bool ShouldRetry(PipelineMessage message, Exception? exception) =>
            !IsUsageLimit(message.Response) && base.ShouldRetry(message, exception);

        protected override ValueTask<bool> ShouldRetryAsync(PipelineMessage message, Exception? exception) =>
            IsUsageLimit(message.Response)
                ? new ValueTask<bool>(false)
                : base.ShouldRetryAsync(message, exception);

        private static bool IsUsageLimit(PipelineResponse? response) =>
            response is not null && AiNoticeExtractor.IsUsageLimit(response.Status);
    }

    private sealed record AiSettings(string Section, string ApiKey, string Model, Uri BaseUrl, string? WorkspaceId)
    {
        public static AiSettings Read(IConfigurationSection section)
        {
            var apiKey = Required(section, "ApiKey");
            var model = Required(section, "Model");
            var route = Required(section, "BaseUrl");
            if (!Uri.TryCreate(route, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme != Uri.UriSchemeHttps)
                throw new NoticeExtractionUnavailableException($"{section.Path}:BaseUrl must be an absolute https URL.");

            var workspaceId = section["WorkspaceId"];
            return new AiSettings(
                section.Path,
                apiKey,
                model,
                baseUrl,
                string.IsNullOrWhiteSpace(workspaceId) ? null : workspaceId.Trim());
        }

        private static string Required(IConfigurationSection section, string key)
        {
            var value = section[key];
            return !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : throw new NoticeExtractionUnavailableException($"{section.Path}:{key} is not configured.");
        }
    }

    private static partial class Classification
    {
        private static readonly Regex EmailAddress = EmailPattern();
        public static string SystemPrompt { get; } = BuildPrompt();

        public readonly record struct Rejection(string Reason, string? Company, string? Role, string? Status);

        public static ApplicationNotice? Interpret(
            string content,
            string modelJson,
            DateTimeOffset fallbackSentAt,
            out Rejection rejection)
        {
            rejection = new Rejection("The classification was rejected.", null, null, null);
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(modelJson);
            }
            catch (JsonException ex)
            {
                throw new NoticeExtractionUnavailableException("The classification was not JSON.", ex);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return Reject(ref rejection, "The message is not exactly one candidacy.", null, null, null);

                var company = JsonString(root, "company");
                var role = JsonString(root, "role");
                var statusText = JsonString(root, "status");
                if (root.TryGetProperty("candidacies", out _))
                    return Reject(ref rejection, "The message is not exactly one candidacy.", company, role, statusText);
                if (!ApplicationStatusText.TryFromDisplayName(statusText, out var status))
                    return Reject(ref rejection, "The status is not an application notice status.", company, role, statusText);
                if (string.IsNullOrWhiteSpace(company))
                    return Reject(ref rejection, "The company is missing.", company, role, statusText);
                if (!IsGrounded(company, content))
                    return Reject(ref rejection, "The company does not appear in the message.", company, role, statusText);
                if (!TryReadSentAt(content, out var sentAt))
                    sentAt = fallbackSentAt;

                var applicationRole = string.IsNullOrWhiteSpace(role)
                    ? ApplicationRole.Unnamed
                    : ApplicationRole.Named(role);
                return new ApplicationNotice(company, applicationRole, status, sentAt);
            }
        }

        private static ApplicationNotice? Reject(
            ref Rejection rejection,
            string reason,
            string? company,
            string? role,
            string? status)
        {
            rejection = new Rejection(reason, company, role, status);
            return null;
        }

        private static string? JsonString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
                return null;
            return property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
        }

        private static bool TryReadSentAt(string content, out DateTimeOffset sentAt)
        {
            sentAt = default;
            using var reader = new StringReader(content);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0)
                    return false;
                if (!line.StartsWith("Date:", StringComparison.OrdinalIgnoreCase))
                    continue;

                var raw = line["Date:".Length..].Trim();
                return DateTimeOffset.TryParse(
                    raw,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                    out sentAt);
            }

            return false;
        }

        private static bool IsGrounded(string company, string content)
        {
            var withoutAddresses = EmailAddress.Replace(content, " ");
            var normalizedContent = Collapse(withoutAddresses);
            var normalizedCompany = Collapse(company);
            return normalizedCompany.Length > 0
                && normalizedContent.Contains(normalizedCompany, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildPrompt()
        {
            var statuses = string.Join(" | ", Enum.GetValues<ApplicationStatus>().Select(status => status.DisplayName()));
            return $$"""
                You read one email and decide whether it reports a candidacy the recipient already has underway.
                Reply with one JSON object and no other text:
                {"company":"string","role":"string or null","status":"{{statuses}}"}
                The object is one application notice. When the message is not exactly one, reply {"company":null,"role":null,"status":null}. Do not pick one of several candidacies.
                Company text must appear in the subject, the body, or the From display name. Never invent a company from the email domain.
                Role is null when the position is not named.
                Status is exactly one of: {{statuses}}.
                Application Sent includes a submission acknowledgement, including "thanks for applying" and "your application is under review".
                Interviewing is a screen, an interview, or a task.
                Accepted means the employer has made an offer.
                Rejected means the employer has ended the candidacy.
                Job alerts, newsletters, verification codes, on hold, and withdrawn are not application notices.
                Do not include the time the mail was sent. That is read from the Date header.
                There is no sender filter and no list of recruiting systems.
                """;
        }

        private static string Collapse(string value) =>
            Whitespace().Replace(value, " ").Trim();

        [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex EmailPattern();

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();
    }
}
