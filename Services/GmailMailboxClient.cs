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
        var request = gmail.Users.Messages.Get("me", messageId);
        request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;

        Message message;
        try
        {
            message = await request.ExecuteAsync(ct);
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
        return new MailMessage(messageId, ReadContent(message), labels);
    }

    private static string ReadContent(Message message)
    {
        var headers = message.Payload?.Headers;
        string Header(string name) =>
            headers?.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

        var body = FindText(message.Payload);
        return $"From: {Header("From")}\nSubject: {Header("Subject")}\n\n{body}";
    }

    private static string FindText(MessagePart? part)
    {
        if (part is null)
            return "";

        if (string.Equals(part.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase) && part.Body?.Data is not null)
            return DecodeBase64Url(part.Body.Data);

        if (part.Parts is null)
            return "";

        foreach (var child in part.Parts)
        {
            var text = FindText(child);
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        foreach (var child in part.Parts)
        {
            if (string.Equals(child.MimeType, "text/html", StringComparison.OrdinalIgnoreCase) && child.Body?.Data is not null)
                return DecodeBase64Url(child.Body.Data);
        }

        return "";
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
