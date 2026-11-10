using Apptrack.Data;
using Apptrack.Entities;
using Apptrack.Services;
using NoticeExtraction = Apptrack.Services.NoticeExtraction;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Apptrack.Tests;

public sealed class GmailWatchRefreshTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RenewedExpiration = new(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;
    private readonly FakeMailboxes _mailboxes = new();
    private readonly ManualTime _time = new(new DateTimeOffset(Now));

    public GmailWatchRefreshTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Google:TopicName"] = "projects/app/topics/gmail" })
            .Build();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<IMailboxClientFactory>(_mailboxes);
        services.AddSingleton<NoticeExtraction.IApplicationNoticeExtractor>(new IdleNotices());
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddScoped<NoticesService>();
        services.AddScoped<GmailSyncService>();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<GmailWatchRefresh>();

        _services = services.BuildServiceProvider();
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task Due_watch_is_renewed_and_keeps_history_id()
    {
        var userId = await SeedUser("ada@example.com");
        await SeedWatch(userId, historyId: 100, expiresAtUtc: Now.AddHours(12));
        _mailboxes.Renewals[userId] = new WatchRegistration(999, RenewedExpiration);

        var outcome = await _services.GetRequiredService<GmailWatchRefresh>().RefreshDueAsync();

        Assert.Equal(new WatchRefreshOutcome(1, 0), outcome);
        using var scope = _services.CreateScope();
        var watch = await scope.ServiceProvider.GetRequiredService<AppDbContext>().GmailWatches.SingleAsync();
        Assert.Equal(100ul, watch.HistoryId);
        Assert.Equal(RenewedExpiration, watch.WatchExpiration);
        Assert.Equal([userId], _mailboxes.Watched);
    }

    [Fact]
    public async Task Watch_expiring_after_24_hours_is_left_alone()
    {
        var userId = await SeedUser("ada@example.com");
        var expires = Now.AddHours(24).AddTicks(1);
        await SeedWatch(userId, historyId: 100, expires);

        var outcome = await Refresh();

        Assert.Equal(new WatchRefreshOutcome(0, 0), outcome);
        Assert.Empty(_mailboxes.Watched);
        await AssertExpiration(userId, expires);
    }

    [Fact]
    public async Task Watch_expiring_exactly_24_hours_from_now_is_renewed()
    {
        var userId = await SeedUser("ada@example.com");
        await SeedWatch(userId, historyId: 100, Now.AddHours(24));
        _mailboxes.Renewals[userId] = new WatchRegistration(999, RenewedExpiration);

        var outcome = await Refresh();

        Assert.Equal(new WatchRefreshOutcome(1, 0), outcome);
        await AssertExpiration(userId, RenewedExpiration);
    }

    [Fact]
    public async Task Already_expired_watch_is_renewed()
    {
        var userId = await SeedUser("ada@example.com");
        await SeedWatch(userId, historyId: 40, Now.AddHours(-3));
        _mailboxes.Renewals[userId] = new WatchRegistration(999, RenewedExpiration);

        var outcome = await Refresh();

        Assert.Equal(new WatchRefreshOutcome(1, 0), outcome);
        using var scope = _services.CreateScope();
        var watch = await scope.ServiceProvider.GetRequiredService<AppDbContext>().GmailWatches.SingleAsync();
        Assert.Equal(40ul, watch.HistoryId);
        Assert.Equal(RenewedExpiration, watch.WatchExpiration);
    }

    [Fact]
    public async Task One_mailbox_failure_still_renews_the_next_watch()
    {
        var ada = await SeedUser("ada@example.com");
        var grace = await SeedUser("grace@example.com");
        await SeedWatch(ada, 10, Now.AddHours(1));
        await SeedWatch(grace, 20, Now.AddHours(2));
        _mailboxes.Failures[ada] = new InvalidOperationException("gmail refused");
        _mailboxes.Renewals[grace] = new WatchRegistration(999, RenewedExpiration);

        var outcome = await Refresh();

        Assert.Equal(new WatchRefreshOutcome(1, 1), outcome);
        Assert.Equal(new[] { ada, grace }.Order(), _mailboxes.Watched.Order());
        await AssertExpiration(ada, Now.AddHours(1));
        await AssertExpiration(grace, RenewedExpiration);
    }

    [Fact]
    public async Task Soft_deleted_person_is_skipped()
    {
        var userId = await SeedUser("ada@example.com", deleted: true);
        await SeedWatch(userId, 100, Now.AddHours(1));

        var outcome = await Refresh();

        Assert.Equal(new WatchRefreshOutcome(0, 0), outcome);
        Assert.Empty(_mailboxes.Watched);
    }

    [Fact]
    public async Task Person_without_a_token_is_skipped()
    {
        var userId = await SeedUser("ada@example.com", token: false);
        await SeedWatch(userId, 100, Now.AddHours(1));

        var outcome = await Refresh();

        Assert.Equal(new WatchRefreshOutcome(0, 0), outcome);
        Assert.Empty(_mailboxes.Watched);
    }

    [Fact]
    public async Task Person_with_a_removed_token_is_skipped()
    {
        var userId = await SeedUser("ada@example.com");
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.GoogleAccessTokens.SingleAsync()).Delete(Now);
            await db.SaveChangesAsync();
        }
        await SeedWatch(userId, 100, Now.AddHours(1));

        var outcome = await Refresh();

        Assert.Equal(new WatchRefreshOutcome(0, 0), outcome);
        Assert.Empty(_mailboxes.Watched);
    }

    [Fact]
    public async Task Person_without_a_watch_is_skipped()
    {
        await SeedUser("ada@example.com");

        var outcome = await Refresh();

        Assert.Equal(new WatchRefreshOutcome(0, 0), outcome);
        Assert.Empty(_mailboxes.Watched);
    }

    [Theory]
    [InlineData("2026-11-09T18:29:00Z", "2026-11-09T18:30:00Z")]
    [InlineData("2026-11-09T18:30:00Z", "2026-11-10T18:30:00Z")]
    [InlineData("2026-11-09T18:31:00Z", "2026-11-10T18:30:00Z")]
    public void Next_midnight_ist_is_the_following_local_midnight(string now, string expected)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST");

        var next = GmailWatchRefreshService.NextMidnight(DateTimeOffset.Parse(now), zone);

        Assert.Equal(DateTimeOffset.Parse(expected), next);
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    private Task<WatchRefreshOutcome> Refresh() =>
        _services.GetRequiredService<GmailWatchRefresh>().RefreshDueAsync();

    private async Task AssertExpiration(Guid userId, DateTime expected)
    {
        using var scope = _services.CreateScope();
        var watch = await scope.ServiceProvider.GetRequiredService<AppDbContext>().GmailWatches.SingleAsync(w => w.UserId == userId);
        Assert.Equal(expected, watch.WatchExpiration);
    }

    private async Task<Guid> SeedUser(string email, bool token = true, bool deleted = false)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.CreateVersion7();
        var user = new User(id, email, "gmail", Now);
        if (deleted)
            user.Delete(Now);
        db.Users.Add(user);
        if (token)
        {
            var access = new GoogleAccessToken(id, "access", "refresh", Now, 3600, Now);
            if (deleted)
                access.Delete(Now);
            db.GoogleAccessTokens.Add(access);
        }

        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedWatch(Guid userId, ulong historyId, DateTime expiresAtUtc)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.GmailWatches.Add(new GmailWatch(userId, historyId, expiresAtUtc, Now));
        await db.SaveChangesAsync();
    }

    private sealed class IdleNotices : NoticeExtraction.IApplicationNoticeExtractor
    {
        public Task<NoticeExtraction.ApplicationNotice?> ExtractAsync(string messageId, string content, CancellationToken ct) =>
            Task.FromResult<NoticeExtraction.ApplicationNotice?>(null);
    }

    private sealed class FakeMailboxes : IMailboxClientFactory
    {
        public Dictionary<Guid, WatchRegistration> Renewals { get; } = [];
        public Dictionary<Guid, Exception> Failures { get; } = [];
        public List<Guid> Watched { get; } = [];

        public Task<IMailboxClient?> OpenAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IMailboxClient?>(new Client(this, userId));

        private sealed class Client(FakeMailboxes owner, Guid userId) : IMailboxClient
        {
            public Task<WatchRegistration> WatchInboxAsync(string topicName, CancellationToken ct)
            {
                owner.Watched.Add(userId);
                if (owner.Failures.TryGetValue(userId, out var error))
                    throw error;
                return Task.FromResult(owner.Renewals[userId]);
            }

            public Task<HistoryBatch> ListAddedSinceAsync(ulong startHistoryId, CancellationToken ct) =>
                throw new InvalidOperationException("History must not be listed during refresh.");

            public Task<MailMessage?> ReadAsync(string messageId, CancellationToken ct) =>
                throw new InvalidOperationException("Mail must not be read during refresh.");
        }
    }
}
