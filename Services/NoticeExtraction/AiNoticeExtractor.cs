using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace Apptrack.Services.NoticeExtraction;

/// <summary>
/// Reads one application notice from message content with a language model.
/// Chat completions go through the OpenAI SDK. <c>Ai:BaseUrl</c> is the base URL,
/// so the same client can call OpenAI or Anthropic's compatible endpoint.
/// The sent time is taken from the content's Date header, not from the model.
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

    private ChatClient? _chat;

    public async Task<ApplicationNotice?> ExtractAsync(string messageId, string content, CancellationToken ct)
    {
        if (!AiSettings.TryRead(configuration, out var settings, out var missing) || settings is null)
            throw new NoticeExtractionUnavailableException($"{missing} is not configured.");

        ChatCompletion completion;
        try
        {
            completion = await ClientFor(settings).CompleteChatAsync(
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
        catch (ClientResultException ex)
        {
            throw new NoticeExtractionUnavailableException($"The language model returned {ex.Status}.", ex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new NoticeExtractionUnavailableException("The language model request timed out.");
        }

        var text = completion.Content.Count > 0 ? completion.Content[0].Text : null;
        if (string.IsNullOrWhiteSpace(text))
            throw new NoticeExtractionUnavailableException("The language model returned no classification.");

        var notice = Classification.Interpret(content, text, out var rejection);
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

    private ChatClient ClientFor(AiSettings settings)
    {
        if (_chat is not null)
            return _chat;

        var options = new OpenAIClientOptions
        {
            Endpoint = BaseEndpoint(settings.BaseUrl),
            NetworkTimeout = RequestTimeout,
        };
        if (handler is not null)
            options.Transport = new HttpClientPipelineTransport(new HttpClient(handler, disposeHandler: false));
        if (settings.WorkspaceId is not null)
            options.AddPolicy(new WorkspaceHeaderPolicy(settings.WorkspaceId), PipelinePosition.PerCall);

        _chat = new ChatClient(settings.Model, new ApiKeyCredential(settings.ApiKey), options);
        return _chat;
    }

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

    private sealed record AiSettings(string ApiKey, string Model, Uri BaseUrl, string? WorkspaceId)
    {
        public static bool TryRead(IConfiguration configuration, out AiSettings? settings, out string? missingKey)
        {
            var apiKey = configuration["Ai:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                settings = null;
                missingKey = "Ai:ApiKey";
                return false;
            }

            var model = configuration["Ai:Model"];
            if (string.IsNullOrWhiteSpace(model))
            {
                settings = null;
                missingKey = "Ai:Model";
                return false;
            }

            var route = configuration["Ai:BaseUrl"];
            if (string.IsNullOrWhiteSpace(route) || !Uri.TryCreate(route.Trim(), UriKind.Absolute, out var baseUrl))
            {
                settings = null;
                missingKey = "Ai:BaseUrl";
                return false;
            }

            var workspaceId = configuration["Ai:WorkspaceId"];
            settings = new AiSettings(
                apiKey.Trim(),
                model.Trim(),
                baseUrl,
                string.IsNullOrWhiteSpace(workspaceId) ? null : workspaceId.Trim());
            missingKey = null;
            return true;
        }
    }

    private static partial class Classification
    {
        private static readonly Regex EmailAddress = EmailPattern();
        public static string SystemPrompt { get; } = BuildPrompt();

        public readonly record struct Rejection(string Reason, string? Company, string? Role, string? Status);

        public static ApplicationNotice? Interpret(string content, string modelJson, out Rejection rejection)
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
                    return Reject(ref rejection, "The sent time could not be read from the Date header.", company, role, statusText);

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
                Rejected means the employer has ended the candidacy.
                Job alerts, newsletters, verification codes, offers, on hold, and withdrawn are not application notices.
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
