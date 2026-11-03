using Apptrack.Entities;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Microsoft.EntityFrameworkCore;

namespace Apptrack.Data;

public sealed class GoogleTokenDataStore(IServiceScopeFactory scopes) : IDataStore
{
    public async Task StoreAsync<T>(string key, T value)
    {
        if (value is not TokenResponse token)
            return;

        var userId = ParseUserId(key);
        if (string.IsNullOrEmpty(token.AccessToken))
            throw new InvalidOperationException("Access token is required.");
        if (token.ExpiresInSeconds is not long expiresInSeconds)
            throw new InvalidOperationException("Token expiry is required.");

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!await db.Users.AnyAsync(u => u.Id == userId))
            throw new InvalidOperationException($"No user exists for id {userId}.");

        var existing = await db.GoogleAccessTokens.FirstOrDefaultAsync(t => t.UserId == userId);
        if (existing is null)
        {
            if (string.IsNullOrEmpty(token.RefreshToken))
                throw new InvalidOperationException("Refresh token is required.");

            db.GoogleAccessTokens.Add(new GoogleAccessToken(
                userId,
                token.AccessToken,
                token.RefreshToken,
                AsUtc(token.IssuedUtc),
                expiresInSeconds,
                DateTime.UtcNow));
        }
        else
        {
            existing.UpdateTokens(token.AccessToken, token.RefreshToken, AsUtc(token.IssuedUtc), expiresInSeconds);
        }

        await db.SaveChangesAsync();
    }

    public async Task<T> GetAsync<T>(string key)
    {
        if (typeof(T) != typeof(TokenResponse) || !Guid.TryParse(key, out var userId))
            return default!;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.GoogleAccessTokens.FirstOrDefaultAsync(t => t.UserId == userId);
        if (row is null)
            return default!;

        var token = new TokenResponse
        {
            AccessToken = row.AccessToken,
            RefreshToken = row.RefreshToken,
            IssuedUtc = row.IssuedUtc,
            ExpiresInSeconds = row.ExpiresInSeconds,
            TokenType = "Bearer",
        };

        return (T)(object)token;
    }

    public async Task DeleteAsync<T>(string key)
    {
        if (typeof(T) != typeof(TokenResponse))
            return;

        var userId = ParseUserId(key);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.GoogleAccessTokens.FirstOrDefaultAsync(t => t.UserId == userId);
        if (row is null)
            return;

        row.Delete(DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    public async Task ClearAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.GoogleAccessTokens.ToListAsync();
        var utcNow = DateTime.UtcNow;
        foreach (var row in rows)
            row.Delete(utcNow);

        await db.SaveChangesAsync();
    }

    private static Guid ParseUserId(string key)
    {
        if (!Guid.TryParse(key, out var userId))
            throw new InvalidOperationException($"Token store key '{key}' is not a user id.");

        return userId;
    }

    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
