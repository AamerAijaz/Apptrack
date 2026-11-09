using System.Net.Http.Headers;
using System.Text.Json;

namespace Apptrack.Auth;

public static class GoogleProfileImage
{
    public const string Scope = "https://www.googleapis.com/auth/userinfo.profile";

    /// <summary>
    /// The picture URL from Google's userinfo response, or null when Google has none or cannot be reached.
    /// A missing picture does not fail Gmail connect.
    /// </summary>
    public static async Task<string?> TryFetchAsync(HttpClient http, string accessToken)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("picture", out var picture) || picture.ValueKind != JsonValueKind.String)
                return null;

            return picture.GetString();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }
}
