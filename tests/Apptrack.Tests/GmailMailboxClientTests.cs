using System.Net;
using System.Text;
using Apptrack.Services;
using Google.Apis.Gmail.v1;
using Google.Apis.Http;
using Google.Apis.Services;
using Xunit;

namespace Apptrack.Tests;

public sealed class GmailMailboxClientTests
{
    private const string Letter = "Your application for Software Engineer was received and is being reviewed.";

    [Fact]
    public async Task Html_letter_is_read_when_the_plain_part_is_blank()
    {
        var html = $"<html><body><p>{Letter}</p><a href=\"https://example.com/letter\">View in Browser</a></body></html>";
        using var gmail = new GmailService(new BaseClientService.Initializer
        {
            HttpClientFactory = new ScriptedGmail(request =>
            {
                var path = request.RequestUri?.AbsolutePath ?? "";
                if (path.Contains("/messages/msg-1/attachments/html-body", StringComparison.Ordinal))
                    return Json(Attachment(html));

                if (path.Contains("/messages/msg-1", StringComparison.Ordinal))
                    return Json(Message());

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }),
        });
        var mailbox = new GmailMailboxClient(gmail);

        var message = await mailbox.ReadAsync("msg-1", CancellationToken.None);

        Assert.NotNull(message);
        Assert.Contains("From: Northwind Recruiting <jobs@northwind.example>", message.Content);
        Assert.Contains("Subject: Your recent job application for Software Engineer - 25019732", message.Content);
        Assert.Contains("Date: Wed, 04 Nov 2026 15:55:10 +0000 (UTC)", message.Content);
        Assert.Contains(Letter, message.Content);
    }

    [Fact]
    public async Task Html_letter_is_read_when_the_message_is_html_itself()
    {
        var html = $"<html><body><p>{Letter}</p></body></html>";
        var data = Base64Url(html);
        var json = """
            {
              "id": "msg-2",
              "labelIds": ["INBOX"],
              "payload": {
                "mimeType": "text/html",
                "headers": [
                  {"name": "From", "value": "Northwind Recruiting <jobs@northwind.example>"},
                  {"name": "Subject", "value": "Your recent job application for Software Engineer - 25019732"},
                  {"name": "Date", "value": "Wed, 04 Nov 2026 15:55:10 +0000 (UTC)"}
                ],
                "body": {"size": 80, "data": "DATA"}
              }
            }
            """.Replace("DATA", data, StringComparison.Ordinal);

        using var gmail = new GmailService(new BaseClientService.Initializer
        {
            HttpClientFactory = new ScriptedGmail(request =>
            {
                var path = request.RequestUri?.AbsolutePath ?? "";
                if (path.Contains("/messages/msg-2", StringComparison.Ordinal))
                    return Json(json);

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }),
        });
        var mailbox = new GmailMailboxClient(gmail);

        var message = await mailbox.ReadAsync("msg-2", CancellationToken.None);

        Assert.NotNull(message);
        Assert.Contains("From: Northwind Recruiting <jobs@northwind.example>", message.Content);
        Assert.Contains("Date: Wed, 04 Nov 2026 15:55:10 +0000 (UTC)", message.Content);
        Assert.Contains(Letter, message.Content);
    }

    private static string Message()
    {
        var blank = Base64Url(" \n");
        return """
            {
              "id": "msg-1",
              "labelIds": ["INBOX"],
              "payload": {
                "mimeType": "multipart/related",
                "headers": [
                  {"name": "From", "value": "Northwind Recruiting <jobs@northwind.example>"},
                  {"name": "Subject", "value": "Your recent job application for Software Engineer - 25019732"},
                  {"name": "Date", "value": "Wed, 04 Nov 2026 15:55:10 +0000 (UTC)"}
                ],
                "parts": [
                  {
                    "mimeType": "multipart/alternative",
                    "parts": [
                      {"mimeType": "text/plain", "body": {"size": 2, "data": "BLANK"}},
                      {"mimeType": "text/html", "body": {"size": 180, "attachmentId": "html-body"}}
                    ]
                  },
                  {"mimeType": "image/png", "body": {"size": 8, "attachmentId": "logo"}}
                ]
              }
            }
            """.Replace("BLANK", blank, StringComparison.Ordinal);
    }

    private static string Attachment(string text) =>
        """{"size": 180, "data": "DATA"}""".Replace("DATA", Base64Url(text), StringComparison.Ordinal);

    private static string Base64Url(string text)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class ScriptedGmail(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new ScriptedHandler(respond);

        private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(respond(request));
        }
    }
}
