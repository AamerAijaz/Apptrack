using Apptrack.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Apptrack.Tests;

public sealed class SessionCookieTests
{
    private readonly EphemeralDataProtectionProvider _protection = new();
    private readonly ManualTime _time = new(DateTimeOffset.UtcNow);
    private readonly SessionCookie _session;

    public SessionCookieTests()
    {
        _session = new SessionCookie(_protection, _time);
    }

    [Fact]
    public void Issued_session_reads_back_the_person()
    {
        var userId = Guid.CreateVersion7();

        var cookie = Issue(userId);

        Assert.Equal(userId, _session.Read(RequestWith(cookie)));
    }

    [Fact]
    public void Issued_cookie_carries_the_shared_attributes_and_fourteen_days()
    {
        var header = IssueHeader(Guid.CreateVersion7());

        Assert.StartsWith("apptrack_session=", header);
        var attributes = Attributes(header);
        Assert.Contains("httponly", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("samesite=lax", attributes);
        Assert.Contains("path=/api", attributes);
        Assert.Contains("max-age=1209600", attributes);
        Assert.DoesNotContain(attributes, a => a.StartsWith("domain", StringComparison.Ordinal));
    }

    [Fact]
    public void Payload_is_the_person_id_in_n_format_with_the_session_purpose()
    {
        var userId = Guid.CreateVersion7();

        var cookie = Issue(userId);

        var protector = _protection.CreateProtector("Apptrack.Session.v1").ToTimeLimitedDataProtector();
        var payload = protector.Unprotect(cookie, out var expiration);
        Assert.Equal(userId.ToString("N"), payload);
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromDays(14), expiration, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Missing_cookie_reads_null()
    {
        Assert.Null(_session.Read(new DefaultHttpContext().Request));
        Assert.Null(_session.Read(RequestWith("")));
    }

    [Fact]
    public void Tampered_cookie_reads_null()
    {
        var cookie = Issue(Guid.CreateVersion7());
        var middle = cookie.Length / 2;
        var tampered = cookie[..middle] + (cookie[middle] == 'A' ? 'B' : 'A') + cookie[(middle + 1)..];

        Assert.Null(_session.Read(RequestWith(tampered)));
        Assert.Null(_session.Read(RequestWith("not a protected value!")));
    }

    [Fact]
    public void Expired_session_reads_null()
    {
        var cookie = Issue(Guid.CreateVersion7());

        _time.Advance(TimeSpan.FromDays(14) - TimeSpan.FromSeconds(1));
        Assert.NotNull(_session.Read(RequestWith(cookie)));

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(_session.Read(RequestWith(cookie)));
    }

    [Fact]
    public void Value_protected_for_another_purpose_reads_null()
    {
        var other = _protection.CreateProtector("Apptrack.Other").ToTimeLimitedDataProtector();
        var cookie = other.Protect(Guid.CreateVersion7().ToString("N"), _time.GetUtcNow().AddDays(1));

        Assert.Null(_session.Read(RequestWith(cookie)));
    }

    [Fact]
    public void Payload_that_is_not_an_n_format_guid_reads_null()
    {
        var protector = _protection.CreateProtector("Apptrack.Session.v1").ToTimeLimitedDataProtector();
        var expires = _time.GetUtcNow().AddDays(1);

        Assert.Null(_session.Read(RequestWith(protector.Protect(Guid.CreateVersion7().ToString("D"), expires))));
        Assert.Null(_session.Read(RequestWith(protector.Protect("ada@example.com", expires))));
    }

    [Fact]
    public void Clear_expires_the_cookie_with_the_same_attributes()
    {
        var ctx = new DefaultHttpContext();

        _session.Clear(ctx.Response);

        var header = Assert.Single(ctx.Response.Headers.SetCookie.ToArray())!;
        Assert.StartsWith("apptrack_session=;", header);
        var attributes = Attributes(header);
        Assert.Contains("expires=thu, 01 jan 1970 00:00:00 gmt", attributes);
        Assert.Contains("httponly", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("samesite=lax", attributes);
        Assert.Contains("path=/api", attributes);
    }

    [Fact]
    public void Shared_options_match_the_session_attributes()
    {
        var options = SessionCookie.Options(TimeSpan.FromMinutes(10));

        Assert.True(options.HttpOnly);
        Assert.True(options.Secure);
        Assert.Equal(SameSiteMode.Lax, options.SameSite);
        Assert.Equal("/api", options.Path);
        Assert.Null(options.Domain);
        Assert.Equal(TimeSpan.FromMinutes(10), options.MaxAge);

        var delete = SessionCookie.DeleteOptions();
        Assert.True(delete.HttpOnly);
        Assert.True(delete.Secure);
        Assert.Equal(SameSiteMode.Lax, delete.SameSite);
        Assert.Equal("/api", delete.Path);
        Assert.Null(delete.Domain);
    }

    private string IssueHeader(Guid userId)
    {
        var ctx = new DefaultHttpContext();
        _session.Issue(ctx.Response, userId);
        return Assert.Single(ctx.Response.Headers.SetCookie.ToArray())!;
    }

    private string Issue(Guid userId)
    {
        var header = IssueHeader(userId);
        var pair = header.Split(';')[0];
        return Uri.UnescapeDataString(pair["apptrack_session=".Length..]);
    }

    private static string[] Attributes(string header) =>
        header.Split(';').Skip(1).Select(a => a.Trim().ToLowerInvariant()).ToArray();

    private static HttpRequest RequestWith(string cookie)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = $"apptrack_session={cookie}";
        return ctx.Request;
    }
}

internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
