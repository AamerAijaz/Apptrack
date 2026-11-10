using System.Data.Common;
using System.Text.Json;
using Apptrack.Auth;
using Apptrack.Data;
using Apptrack.Entities;
using Apptrack.Endpoints;
using Apptrack.Services;
using Apptrack.Services.NoticeExtraction;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Apptrack.Tests;

public sealed class NoticesServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly CommandLog _commands = new();
    private readonly NoticesService _notices;
    private readonly SessionCookie _session = new(new EphemeralDataProtectionProvider(), TimeProvider.System);

    public NoticesServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_commands)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _notices = new NoticesService(_db, new AdvancingTime(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task Unknown_person_is_null()
    {
        Assert.Null(await _notices.GetNotices(Guid.CreateVersion7(), NoticeStatus.ApplicationSent, 12, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Soft_deleted_person_is_null()
    {
        var userId = await SeedUser("ada@example.com");
        await SeedNotice(userId, "inbox-1", "Acme", "Engineer", "Application Sent", At(1));
        (await _db.Users.SingleAsync()).Delete(DateTime.UtcNow);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.Null(await _notices.GetNotices(userId, NoticeStatus.ApplicationSent, 12, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Person_without_notices_gets_their_address_and_an_empty_list()
    {
        var userId = await SeedUser("ada@example.com");

        var page = await _notices.GetNotices(userId, NoticeStatus.ApplicationSent, 12, 0, CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal("ada@example.com", page.Email);
        Assert.Empty(page.Notices);
    }

    [Fact]
    public async Task Notices_are_projected_newest_first_with_utc_sent_at()
    {
        var userId = await SeedUser("ada@example.com");
        await SeedNotice(userId, "inbox-1", "Acme", "Engineer", "Application Sent", At(3));
        await SeedNotice(userId, "inbox-2", "Beta", null, "Application Sent", At(1));
        await SeedNotice(userId, "inbox-3", "Northwind Freight", "Data Analyst", "Interviewing", At(2));
        _db.ChangeTracker.Clear();

        var page = await _notices.GetNotices(userId, NoticeStatus.ApplicationSent, 12, 0, CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal(["Beta", "Acme"], page.Notices.Select(n => n.Company));
        var beta = page.Notices[0];
        Assert.Null(beta.Role);
        Assert.Equal(At(1), beta.SentAt);
        Assert.Equal(DateTimeKind.Utc, beta.SentAt.Kind);
        Assert.All(page.Notices, n => Assert.Equal(DateTimeKind.Utc, n.SentAt.Kind));

        var interviewing = await _notices.GetNotices(userId, NoticeStatus.Interviewing, 12, 0, CancellationToken.None);
        var northwind = Assert.Single(interviewing!.Notices);
        Assert.Equal("Northwind Freight", northwind.Company);
        Assert.Equal("Data Analyst", northwind.Role);
    }

    [Fact]
    public async Task Notices_sent_at_the_same_time_are_ordered_by_id_descending()
    {
        var userId = await SeedUser("ada@example.com");
        for (var i = 1; i <= 5; i++)
            await SeedNotice(userId, $"inbox-{i}", $"Company {i}", null, "Application Sent", At(1));
        _db.ChangeTracker.Clear();

        var first = await _notices.GetNotices(userId, NoticeStatus.ApplicationSent, 2, 0, CancellationToken.None);
        var second = await _notices.GetNotices(userId, NoticeStatus.ApplicationSent, 2, 2, CancellationToken.None);
        var third = await _notices.GetNotices(userId, NoticeStatus.ApplicationSent, 2, 4, CancellationToken.None);

        var expected = await _db.ApplicationNotices
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Select(n => n.Id)
            .ToListAsync();
        var paged = first!.Notices.Concat(second!.Notices).Concat(third!.Notices).Select(n => n.Id);
        Assert.Equal(expected, paged);
        Assert.Equal(2, first.Notices.Count);
        Assert.Single(third.Notices);
    }

    [Fact]
    public async Task Only_the_persons_live_notices_are_returned()
    {
        var ada = await SeedUser("ada@example.com");
        var grace = await SeedUser("grace@example.com");
        await SeedNotice(ada, "inbox-1", "Acme", "Engineer", "Application Sent", At(1));
        await SeedNotice(grace, "inbox-1", "Globex", "Designer", "Rejected", At(2));
        await SeedNotice(ada, "inbox-2", "Deleted Co", null, "Application Sent", At(3));
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE application_notices SET DeletedAt = {0} WHERE Company = 'Deleted Co'", DateTime.UtcNow);
        _db.ChangeTracker.Clear();

        var page = await _notices.GetNotices(ada, NoticeStatus.ApplicationSent, 12, 0, CancellationToken.None);

        Assert.Equal("ada@example.com", page!.Email);
        Assert.Equal(["Acme"], page.Notices.Select(n => n.Company));
    }

    [Fact]
    public async Task Content_is_not_read()
    {
        var userId = await SeedUser("ada@example.com");
        await SeedNotice(userId, "inbox-1", "Acme", "Engineer", "Application Sent", At(1));
        _db.ChangeTracker.Clear();
        _commands.Texts.Clear();

        await _notices.GetNotices(userId, NoticeStatus.ApplicationSent, 12, 0, CancellationToken.None);

        Assert.NotEmpty(_commands.Texts);
        Assert.All(_commands.Texts, sql => Assert.DoesNotContain("Content", sql));
    }

    [Fact]
    public async Task Wire_json_has_only_the_contract_fields()
    {
        var userId = await SeedUser("ada@example.com");
        await SeedNotice(userId, "inbox-1", "Acme", null, "Interviewing", new DateTime(2026, 11, 3, 14, 22, 5, DateTimeKind.Utc));
        _db.ChangeTracker.Clear();

        var ctx = WithSession(userId);
        var result = await NoticesEndpoint.GetAsync(ctx, _session, _notices, CancellationToken.None);
        var ok = Assert.IsType<Ok<NoticeBoardDto>>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var root = json.RootElement;
        Assert.Equal(["email", "lanes"], root.EnumerateObject().Select(p => p.Name));
        var lanes = root.GetProperty("lanes").EnumerateArray().ToArray();
        Assert.Equal(["Application Sent", "Interviewing", "Accepted", "Rejected"], lanes.Select(lane => lane.GetProperty("status").GetString()));
        var interviewing = lanes[1];
        Assert.Equal(["status", "notices", "limit"], interviewing.EnumerateObject().Select(p => p.Name));
        Assert.Equal(12, interviewing.GetProperty("limit").GetInt32());
        var notice = Assert.Single(interviewing.GetProperty("notices").EnumerateArray());
        Assert.Equal(["id", "company", "role", "sentAt"], notice.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Null, notice.GetProperty("role").ValueKind);
        Assert.Equal("2026-11-03T14:22:05Z", notice.GetProperty("sentAt").GetString());
        Assert.Equal(ok.Value!.Lanes[1].Notices[0].Id.ToString("D"), notice.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Endpoint_without_a_session_is_401_and_leaves_cookies_alone()
    {
        var ctx = new DefaultHttpContext();

        var result = await NoticesEndpoint.GetAsync(ctx, _session, _notices, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task Endpoint_with_an_unreadable_session_is_401_and_leaves_cookies_alone()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = "apptrack_session=forged";

        var result = await NoticesEndpoint.GetAsync(ctx, _session, _notices, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task Endpoint_for_a_missing_person_is_401_and_clears_the_session()
    {
        var ctx = WithSession(Guid.CreateVersion7());

        var result = await NoticesEndpoint.GetAsync(ctx, _session, _notices, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        var header = Assert.Single(ctx.Response.Headers.SetCookie.ToArray())!;
        Assert.StartsWith("apptrack_session=;", header);
        Assert.Contains("path=/api", header);
    }

    [Fact]
    public async Task Endpoint_for_a_live_person_is_200_and_not_cached()
    {
        var userId = await SeedUser("ada@example.com");
        var ctx = WithSession(userId);

        var result = await NoticesEndpoint.GetAsync(ctx, _session, _notices, CancellationToken.None);

        var ok = Assert.IsType<Ok<NoticeBoardDto>>(result);
        Assert.Equal("ada@example.com", ok.Value!.Email);
        Assert.Equal(4, ok.Value.Lanes.Count);
        Assert.All(ok.Value.Lanes, lane => Assert.Empty(lane.Notices));
        Assert.Equal("no-store", ctx.Response.Headers.CacheControl.ToString());
        Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task Lane_with_a_bad_status_or_page_is_400_and_leaves_the_cookie()
    {
        var userId = await SeedUser("ada@example.com");
        var ctx = WithSession(userId);

        var result = await NoticesEndpoint.GetLaneAsync(
            ctx, _session, _notices, "Hired", 12, 0, CancellationToken.None);

        Assert.IsType<BadRequest>(result);
        Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);

        var negative = await NoticesEndpoint.GetLaneAsync(
            WithSession(userId), _session, _notices, "Accepted", 0, -1, CancellationToken.None);
        Assert.IsType<BadRequest>(negative);
    }

    [Fact]
    public async Task Lane_for_a_missing_person_is_401_and_clears_the_session()
    {
        var ctx = WithSession(Guid.CreateVersion7());

        var result = await NoticesEndpoint.GetLaneAsync(
            ctx, _session, _notices, "Accepted", 12, 0, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        Assert.Contains("path=/api", Assert.Single(ctx.Response.Headers.SetCookie.ToArray())!);
    }

    [Fact]
    public async Task Adding_a_notice_puts_the_newest_card_first_and_replay_keeps_the_first_body()
    {
        var userId = await SeedUser("ada@example.com");
        var firstKey = Guid.CreateVersion7();
        var ctx = HandContext(userId, firstKey, "Acme", "Engineer", "Interviewing");
        var created = await NoticesEndpoint.AddAsync(ctx, _session, _notices, CancellationToken.None);
        var ok = Assert.IsType<Ok<AddedNoticeDto>>(created);
        Assert.Equal("Interviewing", ok.Value!.Status);
        Assert.Equal("Acme", ok.Value.Notice.Company);
        Assert.Equal("no-store", ctx.Response.Headers.CacheControl.ToString());

        var replay = await AddHand(userId, firstKey, "Other", "", "Interviewing");
        var again = Assert.IsType<Ok<AddedNoticeDto>>(replay);
        Assert.Equal(ok.Value.Notice.Id, again.Value!.Notice.Id);
        Assert.Equal("Acme", again.Value.Notice.Company);

        await AddHand(userId, Guid.CreateVersion7(), "Beta", "", "Interviewing");
        var page = await _notices.GetNotices(userId, NoticeStatus.Interviewing, 12, 0, CancellationToken.None);
        Assert.Equal(["Beta", "Acme"], page!.Notices.Select(n => n.Company));
    }

    [Fact]
    public async Task A_blank_company_is_400_and_leaves_the_session()
    {
        var userId = await SeedUser("ada@example.com");
        var ctx = HandContext(userId, Guid.CreateVersion7(), "  ", null, "Interviewing");

        var result = await NoticesEndpoint.AddAsync(ctx, _session, _notices, CancellationToken.None);

        Assert.IsType<BadRequest>(result);
        Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task A_retired_key_is_409_and_leaves_the_session()
    {
        var userId = await SeedUser("ada@example.com");
        var key = Guid.CreateVersion7();
        await AddHand(userId, key, "Acme", null, "Accepted");
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE application_notices SET DeletedAt = {0} WHERE Company = 'Acme'", DateTime.UtcNow);
        _db.ChangeTracker.Clear();

        var ctx = HandContext(userId, key, "Acme", null, "Accepted");
        var result = await NoticesEndpoint.AddAsync(ctx, _session, _notices, CancellationToken.None);

        Assert.IsType<Conflict>(result);
        Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task Add_for_a_missing_person_is_401_and_clears_the_session()
    {
        var ctx = HandContext(Guid.CreateVersion7(), Guid.CreateVersion7(), "Acme", null, "Rejected");

        var result = await NoticesEndpoint.AddAsync(ctx, _session, _notices, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        Assert.Contains("path=/api", Assert.Single(ctx.Response.Headers.SetCookie.ToArray())!);
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

    private Task<IResult> AddHand(Guid userId, Guid key, string company, string? role, string status) =>
        NoticesEndpoint.AddAsync(
            HandContext(userId, key, company, role, status), _session, _notices, CancellationToken.None);

    private DefaultHttpContext HandContext(Guid userId, Guid key, string company, string? role, string status)
    {
        var ctx = WithSession(userId);
        var json = JsonSerializer.Serialize(
            new { company, role, status, key = key.ToString() },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        ctx.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        ctx.Request.ContentType = "application/json";
        return ctx;
    }

    private static DateTime At(int day) => new(2026, 11, day, 9, 0, 0, DateTimeKind.Utc);

    private async Task<Guid> SeedUser(string email)
    {
        var id = Guid.CreateVersion7();
        _db.Users.Add(new User(id, email, "gmail", DateTime.UtcNow));
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task SeedNotice(Guid userId, string messageId, string company, string? role, string status, DateTime sentAt)
    {
        Assert.True(ApplicationStatusText.TryFromDisplayName(status, out var parsed));
        var extracted = new Apptrack.Services.NoticeExtraction.ApplicationNotice(
            company,
            role is null ? ApplicationRole.Unnamed : ApplicationRole.Named(role),
            parsed,
            new DateTimeOffset(sentAt));
        var incoming = IncomingNotice.FromMailbox(messageId, extracted, "body");
        var stored = await _notices.Add(userId, incoming!, CancellationToken.None);
        Assert.IsType<AddNoticeResult.Stored>(stored);
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

internal sealed class AdvancingTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        var current = _now;
        _now += TimeSpan.FromSeconds(1);
        return current;
    }
}
