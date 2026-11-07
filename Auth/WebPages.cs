namespace Apptrack.Auth;

/// <summary>
/// Absolute pages on the web origin that the API redirects the browser to.
/// </summary>
public sealed class WebPages
{
    private WebPages(Uri origin)
    {
        Notices = new Uri(origin, "/notices");
        ConnectFailed = new Uri(origin, "/?connect=failed");
    }

    public Uri Notices { get; }
    public Uri ConnectFailed { get; }

    public static WebPages FromConfiguration(IConfiguration configuration) => Parse(configuration["Web:Origin"]);

    /// <summary>
    /// Accepts an absolute http(s) origin with no path, query, or fragment. Throws otherwise.
    /// </summary>
    public static WebPages Parse(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            throw new InvalidOperationException("Web:Origin is not configured.");
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.AbsolutePath != "/"
            || origin.Contains('?')
            || origin.Contains('#')
            || uri.UserInfo.Length > 0)
            throw new InvalidOperationException($"Web:Origin must be an absolute http(s) origin with no path, query, or fragment: {origin}");

        return new WebPages(uri);
    }
}
