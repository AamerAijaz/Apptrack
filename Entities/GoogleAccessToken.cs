namespace Apptrack.Entities;

public sealed class GoogleAccessToken
{
    private GoogleAccessToken() { }

    public GoogleAccessToken(
        Guid userId,
        string accessToken,
        string refreshToken,
        DateTime issuedUtc,
        long expiresInSeconds,
        DateTime createdAtUtc)
    {
        UserId = userId;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        IssuedUtc = issuedUtc;
        ExpiresInSeconds = expiresInSeconds;
        CreatedAt = createdAtUtc;
    }

    public int Id { get; private set; }
    public Guid UserId { get; private set; }
    public string AccessToken { get; private set; } = null!;
    public string RefreshToken { get; private set; } = null!;
    public DateTime IssuedUtc { get; private set; }
    public long ExpiresInSeconds { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    public void UpdateTokens(string accessToken, string? refreshToken, DateTime issuedUtc, long expiresInSeconds)
    {
        AccessToken = accessToken;
        if (!string.IsNullOrEmpty(refreshToken))
            RefreshToken = refreshToken;
        IssuedUtc = issuedUtc;
        ExpiresInSeconds = expiresInSeconds;
    }
    public void Delete(DateTime utcNow) => DeletedAt ??= utcNow;
    public void Restore() => DeletedAt = null;
}
