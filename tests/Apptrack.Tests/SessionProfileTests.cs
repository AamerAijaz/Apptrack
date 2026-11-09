using System.Data.Common;
using System.Text.Json;
using Apptrack.Auth;
using Apptrack.Data;
using Apptrack.Endpoints;
using Apptrack.Entities;
using Apptrack.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Apptrack.Tests;

public sealed class SessionProfileTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly CommandLog _commands = new();
    private readonly SessionProfileService _profiles;
    private readonly SessionCookie _session = new(new EphemeralDataProtectionProvider(), TimeProvider.System);

    public SessionProfileTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_commands)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _profiles = new SessionProfileService(_db);
    }

    [Fact]
    public async Task Endpoint_without_a_session_is_401_and_leaves_cookies_alone()
    {
        var ctx = new DefaultHttpContext();

        var result = await SessionProfileEndpoint.GetAsync(ctx, _session, _profiles, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task Endpoint_with_an_unreadable_session_is_401_and_leaves_cookies_alone()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = "apptrack_session=forged";

        var result = await SessionProfileEndpoint.GetAsync(ctx, _session, _profiles, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task Endpoint_for_a_missing_person_is_401_and_clears_the_session()
    {
        var ctx = WithSession(Guid.CreateVersion7());

        var result = await SessionProfileEndpoint.GetAsync(ctx, _session, _profiles, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        var header = Assert.Single(ctx.Response.Headers.SetCookie.ToArray())!;
        Assert.StartsWith("apptrack_session=;", header);
        Assert.Contains("path=/api", header);
    }

    [Fact]
    public async Task Soft_deleted_person_is_signed_out()
    {
        var userId = await SeedUser("ada@example.com");
        (await _db.Users.SingleAsync()).Delete(DateTime.UtcNow);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.Null(await _profiles.LoadAsync(userId, CancellationToken.None));
    }

    [Fact]
    public async Task Endpoint_for_a_live_person_is_their_email_and_no_picture_and_not_cached()
    {
        var userId = await SeedUser("ada@example.com");
        var ctx = WithSession(userId);

        var result = await SessionProfileEndpoint.GetAsync(ctx, _session, _profiles, CancellationToken.None);

        var ok = Assert.IsType<Ok<SessionProfile>>(result);
        Assert.Equal("ada@example.com", ok.Value!.Email);
        Assert.Null(ok.Value.Picture);
        Assert.Equal("no-store", ctx.Response.Headers.CacheControl.ToString());
        Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task Wire_json_names_only_email_and_picture()
    {
        var userId = await SeedUser("ada@example.com");

        var profile = await _profiles.LoadAsync(userId, CancellationToken.None);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(profile, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var root = json.RootElement;
        Assert.Equal(["email", "picture"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("ada@example.com", root.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("picture").ValueKind);
    }

    [Fact]
    public async Task A_google_picture_is_what_the_session_read_returns()
    {
        var userId = await SeedUser("ada@example.com");
        var user = await _db.Users.SingleAsync();
        user.RememberPicture("https://lh3.googleusercontent.com/a/ada");
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var profile = await _profiles.LoadAsync(userId, CancellationToken.None);

        Assert.Equal("https://lh3.googleusercontent.com/a/ada", profile!.Picture);
    }

    [Fact]
    public async Task A_later_picture_replaces_the_stored_one()
    {
        var userId = await SeedUser("ada@example.com");
        var user = await _db.Users.SingleAsync();
        user.RememberPicture("https://lh3.googleusercontent.com/a/ada");
        await _db.SaveChangesAsync();
        user.RememberPicture("https://lh3.googleusercontent.com/a/ada-new");
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var profile = await _profiles.LoadAsync(userId, CancellationToken.None);

        Assert.Equal("https://lh3.googleusercontent.com/a/ada-new", profile!.Picture);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://lh3.googleusercontent.com/a/ada")]
    [InlineData("javascript:alert(1)")]
    public async Task An_unsafe_or_empty_picture_leaves_the_person_without_one(string? candidate)
    {
        var userId = await SeedUser("ada@example.com");
        var user = await _db.Users.SingleAsync();
        user.RememberPicture("https://lh3.googleusercontent.com/a/ada");
        await _db.SaveChangesAsync();
        user.RememberPicture(candidate);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var profile = await _profiles.LoadAsync(userId, CancellationToken.None);

        Assert.Null(profile!.Picture);
    }

    [Fact]
    public async Task A_picture_past_the_length_bound_leaves_the_person_without_one()
    {
        var userId = await SeedUser("ada@example.com");
        var user = await _db.Users.SingleAsync();
        user.RememberPicture("https://lh3.googleusercontent.com/a/ada");
        await _db.SaveChangesAsync();
        user.RememberPicture("https://lh3.googleusercontent.com/a/" + new string('a', ProfilePicture.MaxLength));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var profile = await _profiles.LoadAsync(userId, CancellationToken.None);

        Assert.Null(profile!.Picture);
    }

    [Fact]
    public async Task Session_read_does_not_query_notices()
    {
        var userId = await SeedUser("ada@example.com");
        _db.ApplicationNotices.Add(new ApplicationNotice(
            userId, "inbox-1", "Acme", "Engineer", "Application Sent", DateTime.UtcNow, "body"));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _commands.Texts.Clear();

        await _profiles.LoadAsync(userId, CancellationToken.None);

        Assert.NotEmpty(_commands.Texts);
        Assert.All(_commands.Texts, sql => Assert.DoesNotContain("application_notices", sql));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private DefaultHttpContext WithSession(Guid userId)
    {
        var issued = new DefaultHttpContext();
        _session.Issue(issued.Response, userId);
        var pair = issued.Response.Headers.SetCookie.ToString().Split(';')[0];

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = pair;
        return ctx;
    }

    private async Task<Guid> SeedUser(string email)
    {
        var id = Guid.CreateVersion7();
        _db.Users.Add(new User(id, email, "gmail", DateTime.UtcNow));
        await _db.SaveChangesAsync();
        return id;
    }

    private sealed class CommandLog : DbCommandInterceptor
    {
        public List<string> Texts { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Texts.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
