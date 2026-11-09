namespace Apptrack.Entities;

public static class ProfilePicture
{
    public const int MaxLength = 2048;

    /// <summary>
    /// An https URL short enough to store, or null when the candidate is missing or unsafe.
    /// </summary>
    public static string? Accept(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        var trimmed = candidate.Trim();
        if (trimmed.Length > MaxLength)
            return null;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return null;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return null;

        return trimmed;
    }
}
