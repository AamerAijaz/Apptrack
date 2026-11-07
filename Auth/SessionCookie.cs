using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Apptrack.Auth;

public sealed class SessionCookie(IDataProtectionProvider protection, TimeProvider time)
{
    public const string Name = "apptrack_session";
    public const string Purpose = "Apptrack.Session.v1";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    private readonly ITimeLimitedDataProtector _protector = protection.CreateProtector(Purpose).ToTimeLimitedDataProtector();
    private readonly TimeProvider _time = time;

    /// <summary>
    /// Cookie attributes shared by every cookie the API writes: Secure, HttpOnly, SameSite=Lax, Path=/api.
    /// </summary>
    public static CookieOptions Options(TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/api",
        MaxAge = maxAge,
    };

    /// <summary>
    /// Browsers ignore a delete unless it repeats the attributes the cookie was written with.
    /// </summary>
    public static CookieOptions DeleteOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/api",
    };

    public void Issue(HttpResponse response, Guid userId)
    {
        var value = _protector.Protect(userId.ToString("N"), _time.GetUtcNow() + Lifetime);
        response.Cookies.Append(Name, value, Options(Lifetime));
    }

    /// <summary>
    /// The person the session names, or null when the cookie is missing, tampered, expired, or not a session.
    /// </summary>
    public Guid? Read(HttpRequest request)
    {
        var value = request.Cookies[Name];
        if (string.IsNullOrEmpty(value))
            return null;

        string payload;
        DateTimeOffset expiration;
        try
        {
            payload = _protector.Unprotect(value, out expiration);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }

        return expiration > _time.GetUtcNow()
         ? Guid.TryParseExact(payload, "N", out var userId) ? userId : null
         : null;
    }

    public void Clear(HttpResponse response) => response.Cookies.Delete(Name, DeleteOptions());
}
