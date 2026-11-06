using System.Net;
using System.Text;
using Apptrack.Auth;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;

namespace Apptrack.Services;

public sealed class GmailMailboxClientFactory(ApptrackAuthorizationCodeFlow flow) : IMailboxClientFactory
{
    public const string ApplicationName = "Apptrack";

    public async Task<IMailboxClient?> OpenAsync(Guid userId, CancellationToken ct)
    {
        var token = await flow.LoadTokenAsync(userId.ToString(), ct);
        if (token is null)
            return null;

        var credential = new UserCredential(flow, userId.ToString(), token);
        var gmail = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName,
        });
        return new GmailMailboxClient(gmail);
    }
}

public sealed class GmailMailboxClient(GmailService gmail) : IMailboxClient
{
    private static readonly string[] WatchedLabels = ["INBOX", "SENT"];

    public async Task<WatchRegistration> WatchInboxAsync(string topicName, CancellationToken ct)
    {
        var response = await gmail.Users.Watch(new WatchRequest
        {
            LabelIds = [.. WatchedLabels],
            LabelFilterBehavior = "INCLUDE",
            TopicName = topicName,
        }, "me").ExecuteAsync(ct);

        if (response.HistoryId is not ulong historyId || historyId == 0 || response.Expiration is not long expiration)
            throw new InvalidOperationException("Watch response is missing a history id or expiration.");

        return new WatchRegistration(historyId, DateTimeOffset.FromUnixTimeMilliseconds(expiration).UtcDateTime);
    }

    public async Task<HistoryBatch> ListAddedSinceAsync(ulong startHistoryId, CancellationToken ct)
    {
        var ids = new List<string>();
        ulong? latest = null;
        string? pageToken = null;

        do
        {
            var request = gmail.Users.History.List("me");
            request.StartHistoryId = startHistoryId;
            request.PageToken = pageToken;
            request.HistoryTypes = UsersResource.HistoryResource.ListRequest.HistoryTypesEnum.MessageAdded;

            ListHistoryResponse response;
            try
            {
                response = await request.ExecuteAsync(ct);
            }
            catch (GoogleApiException ex)
            {
                throw new GmailSyncRetryException("Gmail history list failed.", ex);
            }

            if (response.HistoryId is not ulong pageHistoryId || pageHistoryId == 0)
                throw new GmailSyncRetryException("Gmail history list returned no history id.");

            latest = pageHistoryId;
            foreach (var record in response.History ?? [])
            {
                foreach (var added in record.MessagesAdded ?? [])
                {
                    var id = added.Message?.Id;
                    if (!string.IsNullOrEmpty(id) && !ids.Contains(id))
                        ids.Add(id);
                }
            }

            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return new HistoryBatch(latest!.Value, ids);
    }

    public async Task<MailMessage?> ReadAsync(string messageId, CancellationToken ct)
    {
        Message message;
        try
        {
            message = await ExecuteWithBackoffAsync(async () =>
            {
                var request = gmail.Users.Messages.Get("me", messageId);
                request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
                return await request.ExecuteAsync(ct);
            }, ct);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (GoogleApiException ex)
        {
            throw new GmailSyncRetryException($"Gmail message {messageId} could not be read.", ex);
        }

        var labels = message.LabelIds?.ToArray() ?? [];
        return new MailMessage(messageId, await ReadContentAsync(messageId, message, ct), labels);
    }

    private static async Task<T> ExecuteWithBackoffAsync<T>(Func<Task<T>> send, CancellationToken ct)
    {
        const int maxAttempts = 4;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await send();
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.TooManyRequests && attempt < maxAttempts - 1)
            {
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt));
                await Task.Delay(delay, ct);
            }
        }
    }

    private async Task<string> ReadContentAsync(string messageId, Message message, CancellationToken ct)
    {
        var headers = message.Payload?.Headers;
        string Header(string name) =>
            headers?.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

        var body = await FindTextAsync(messageId, message.Payload, ct);
        return $"From: {Header("From")}\nSubject: {Header("Subject")}\nDate: {Header("Date")}\n\n{body}";
    }

    private async Task<string> FindTextAsync(string messageId, MessagePart? part, CancellationToken ct)
    {
        var plain = await FindPartAsync(messageId, part, "text/plain", ct);
        if (!string.IsNullOrWhiteSpace(plain))
            return plain;

        return await FindPartAsync(messageId, part, "text/html", ct);
    }

    private async Task<string> FindPartAsync(string messageId, MessagePart? part, string mimeType, CancellationToken ct)
    {
        if (part is null)
            return "";

        if (string.Equals(part.MimeType, mimeType, StringComparison.OrdinalIgnoreCase))
        {
            var text = await ReadPartBodyAsync(messageId, part, ct);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        if (part.Parts is null)
            return "";

        foreach (var child in part.Parts)
        {
            var text = await FindPartAsync(messageId, child, mimeType, ct);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return "";
    }

    private async Task<string> ReadPartBodyAsync(string messageId, MessagePart part, CancellationToken ct)
    {
        if (part.Body?.Data is string data)
            return DecodeBase64Url(data);

        var attachmentId = part.Body?.AttachmentId;
        if (string.IsNullOrEmpty(attachmentId))
            return "";

        MessagePartBody attachment;
        try
        {
            attachment = await ExecuteWithBackoffAsync(
                () => gmail.Users.Messages.Attachments.Get("me", messageId, attachmentId).ExecuteAsync(ct),
                ct);
        }
        catch (GoogleApiException ex)
        {
            throw new GmailSyncRetryException($"Gmail message {messageId} could not be read.", ex);
        }

        return attachment.Data is null ? "" : DecodeBase64Url(attachment.Data);
    }

    private static string DecodeBase64Url(string data)
    {
        var padded = data.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded,
        };
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
