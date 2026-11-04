using System.Text;
using Xunit;
using System.Text.Json;
using Apptrack.Data;
using Apptrack.Entities;
using Apptrack.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Apptrack.Tests;

public sealed class GmailSyncServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly FakeMailboxes _mailboxes = new();
    private readonly CollectingLogger _logger = new();
    private readonly GmailSyncService _sync;

    public GmailSyncServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Google:TopicName"] = "projects/app/topics/gmail" })
            .Build();
        _sync = new GmailSyncService(_db, _mailboxes, configuration, _logger);
    }

    [Fact]
    public async Task Malformed_push_is_acknowledged_and_inserts_nothing()
    {
        var push = new PubSubPush(new PubSubMessage("%%%", "m1", DateTime.UtcNow), "forged");

        await _sync.ProcessPushAsync(push);

        Assert.Equal(0, await _db.Users.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await _db.GmailWatches.CountAsync());

        await _sync.ProcessPushAsync(new PubSubPush(new PubSubMessage("", "m2", DateTime.UtcNow), "forged"));
        await _sync.ProcessPushAsync(new PubSubPush(
            new PubSubMessage(Convert.ToBase64String("[]"u8.ToArray()), "m3", DateTime.UtcNow),
            "forged"));
    }

    [Fact]
    public async Task Unknown_mailbox_is_ignored()
    {
        await _sync.ProcessPushAsync(Push("nobody@example.com", 20));

        Assert.Equal(0, await _db.Users.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await _db.GoogleAccessTokens.CountAsync());
    }

    [Fact]
    public async Task Live_user_without_a_token_is_ignored()
    {
        await SeedUser("ada@example.com", token: false);

        await _sync.ProcessPushAsync(Push("ada@example.com", 20));

        Assert.Equal(1, await _db.Users.CountAsync());
        Assert.Equal(0, await _db.GoogleAccessTokens.CountAsync());
    }

    [Fact]
    public async Task Soft_deleted_person_is_ignored()
    {
        var id = await SeedUser("ada@example.com");
        var user = await _db.Users.SingleAsync();
        user.Delete(DateTime.UtcNow);
        await _db.SaveChangesAsync();

        await _sync.ProcessPushAsync(Push("ada@example.com", 20));

        Assert.Equal(id, (await _db.Users.IgnoreQueryFilters().SingleAsync()).Id);
        Assert.Equal(1, await _db.GoogleAccessTokens.CountAsync());
    }

    [Fact]
    public async Task Push_without_a_watch_row_does_not_list_the_mailbox()
    {
        await SeedUser("ada@example.com");
        _mailboxes.OnList = _ => throw new InvalidOperationException("history list must not run");

        await _sync.ProcessPushAsync(Push("ada@example.com", 20));

        Assert.Equal(0, await _db.GmailWatches.CountAsync());
    }

    [Fact]
    public async Task Start_watch_stores_history_expiration_and_creation_time()
    {
        var userId = await SeedUser("ada@example.com");
        var expires = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        _mailboxes.NextWatch = new WatchRegistration(100, expires);

        await _sync.StartWatchAsync(userId);

        var watch = await _db.GmailWatches.SingleAsync();
        Assert.Equal(userId, watch.UserId);
        Assert.Equal(100ul, watch.HistoryId);
        Assert.Equal(expires, watch.WatchExpiration);
        Assert.InRange(watch.CreatedAt, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Later_watch_refreshes_expiration_and_keeps_history_id()
    {
        var userId = await SeedUser("ada@example.com");
        _mailboxes.NextWatch = new WatchRegistration(100, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        await _sync.StartWatchAsync(userId);
        var createdAt = (await _db.GmailWatches.SingleAsync()).CreatedAt;

        _mailboxes.NextWatch = new WatchRegistration(999, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await _sync.StartWatchAsync(userId);

        var watch = await _db.GmailWatches.SingleAsync();
        Assert.Equal(100ul, watch.HistoryId);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), watch.WatchExpiration);
        Assert.Equal(createdAt, watch.CreatedAt);
        Assert.Equal(1, await _db.Users.CountAsync());
        Assert.Equal(1, await _db.GoogleAccessTokens.CountAsync());
    }

    [Fact]
    public async Task Watch_without_a_history_id_fails_and_stores_nothing()
    {
        var userId = await SeedUser("ada@example.com");
        _mailboxes.WatchError = new InvalidOperationException("Watch response is missing a history id or expiration.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sync.StartWatchAsync(userId));

        Assert.Equal(0, await _db.GmailWatches.CountAsync());
    }

    [Fact]
    public async Task Push_logs_inbox_and_sent_messages_and_advances_from_the_stored_id()
    {
        var userId = await SeedUser("ada@example.com");
        _mailboxes.NextWatch = new WatchRegistration(100, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        await _sync.StartWatchAsync(userId);

        _mailboxes.OnList = start =>
        {
            if (start != 100)
                throw new InvalidOperationException($"history list started at {start}");
            return new HistoryBatch(180,
            [
                "inbox-1",
                "sent-1",
                "spam-1",
            ]);
        };
        _mailboxes.Messages["inbox-1"] = new MailMessage("inbox-1", "Thanks for applying", ["INBOX"]);
        _mailboxes.Messages["sent-1"] = new MailMessage("sent-1", "I applied today", ["SENT"]);
        _mailboxes.Messages["spam-1"] = new MailMessage("spam-1", "Buy now", ["SPAM"]);

        await _sync.ProcessPushAsync(Push("ada@example.com", 150, subscription: "forged"));

        Assert.Contains(_logger.Lines, line => line.Contains("Thanks for applying"));
        Assert.Contains(_logger.Lines, line => line.Contains("I applied today"));
        Assert.DoesNotContain(_logger.Lines, line => line.Contains("Buy now"));
        _db.ChangeTracker.Clear();
        Assert.Equal(180ul, (await _db.GmailWatches.SingleAsync()).HistoryId);
        Assert.Equal(userId, _mailboxes.OpenedFor);
        Assert.Equal(1, await _db.Users.CountAsync());
        Assert.Equal(1, await _db.GoogleAccessTokens.CountAsync());
    }

    [Fact]
    public async Task Push_logs_every_inbox_message_in_a_larger_batch()
    {
        await SeedUser("ada@example.com");
        _mailboxes.NextWatch = new WatchRegistration(100, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        await _sync.StartWatchAsync((await _db.Users.SingleAsync()).Id);

        var ids = Enumerable.Range(1, 8).Select(i => $"inbox-{i}").ToArray();
        _mailboxes.OnList = _ => new HistoryBatch(180, ids);
        foreach (var id in ids)
            _mailboxes.Messages[id] = new MailMessage(id, $"body {id}", ["INBOX"]);

        await _sync.ProcessPushAsync(Push("ada@example.com", 150));

        foreach (var id in ids)
            Assert.Contains(_logger.Lines, line => line.Contains($"body {id}"));
        _db.ChangeTracker.Clear();
        Assert.Equal(180ul, (await _db.GmailWatches.SingleAsync()).HistoryId);
    }

    [Fact]
    public async Task Already_read_history_is_acknowledged_without_listing()
    {
        var userId = await SeedUser("ada@example.com");
        _mailboxes.NextWatch = new WatchRegistration(100, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        await _sync.StartWatchAsync(userId);
        _mailboxes.OnList = _ => throw new InvalidOperationException("history list must not run");

        await _sync.ProcessPushAsync(Push("ada@example.com", 100));
        await _sync.ProcessPushAsync(Push("ada@example.com", 40));

        Assert.Equal(100ul, (await _db.GmailWatches.SingleAsync()).HistoryId);
    }

    [Fact]
    public async Task Gmail_failure_before_the_cursor_moves_is_retryable()
    {
        var userId = await SeedUser("ada@example.com");
        _mailboxes.NextWatch = new WatchRegistration(100, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        await _sync.StartWatchAsync(userId);
        _mailboxes.OnList = _ => throw new GmailSyncRetryException("gmail down");

        await Assert.ThrowsAsync<GmailSyncRetryException>(() => _sync.ProcessPushAsync(Push("ada@example.com", 150)));

        Assert.Equal(100ul, (await _db.GmailWatches.SingleAsync()).HistoryId);
        Assert.Empty(_logger.Lines);
    }

    [Fact]
    public async Task Failed_message_read_does_not_advance_the_cursor()
    {
        await SeedUser("ada@example.com");
        _mailboxes.NextWatch = new WatchRegistration(100, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        await _sync.StartWatchAsync((await _db.Users.SingleAsync()).Id);
        _mailboxes.OnList = _ => new HistoryBatch(180, ["inbox-1", "inbox-2"]);
        _mailboxes.Messages["inbox-1"] = new MailMessage("inbox-1", "ok", ["INBOX"]);
        _mailboxes.ReadError = new GmailSyncRetryException("rate limited");

        await Assert.ThrowsAsync<GmailSyncRetryException>(() => _sync.ProcessPushAsync(Push("ada@example.com", 150)));

        _db.ChangeTracker.Clear();
        Assert.Equal(100ul, (await _db.GmailWatches.SingleAsync()).HistoryId);
    }

    [Fact]
    public async Task Lower_history_id_from_the_list_does_not_rewind_the_cursor()
    {
        await SeedUser("ada@example.com");
        _mailboxes.NextWatch = new WatchRegistration(100, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        await _sync.StartWatchAsync((await _db.Users.SingleAsync()).Id);
        _mailboxes.OnList = start =>
        {
            if (start != 100)
                throw new InvalidOperationException($"history list started at {start}");
            return new HistoryBatch(50, ["inbox-1"]);
        };
        _mailboxes.Messages["inbox-1"] = new MailMessage("inbox-1", "Still log me", ["INBOX"]);

        await _sync.ProcessPushAsync(Push("ada@example.com", 200));

        Assert.Contains(_logger.Lines, line => line.Contains("Still log me"));
        _db.ChangeTracker.Clear();
        Assert.Equal(100ul, (await _db.GmailWatches.SingleAsync()).HistoryId);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<Guid> SeedUser(string email, bool token = true)
    {
        var id = Guid.CreateVersion7();
        _db.Users.Add(new User(id, email, "gmail", DateTime.UtcNow));
        if (token)
        {
            _db.GoogleAccessTokens.Add(new GoogleAccessToken(
                id, "access", "refresh", DateTime.UtcNow, 3600, DateTime.UtcNow));
        }

        await _db.SaveChangesAsync();
        return id;
    }

    private static PubSubPush Push(string email, ulong historyId, string subscription = "projects/app/subscriptions/gmail")
    {
        var json = JsonSerializer.Serialize(new { emailAddress = email, historyId = historyId.ToString() });
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return new PubSubPush(new PubSubMessage(data, "message-1", DateTime.UtcNow), subscription);
    }

    private sealed class FakeMailboxes : IMailboxClientFactory
    {
        public WatchRegistration? NextWatch { get; set; }
        public Exception? WatchError { get; set; }
        public Func<ulong, HistoryBatch>? OnList { get; set; }
        public Dictionary<string, MailMessage> Messages { get; } = [];
        public Exception? ReadError { get; set; }
        public Guid? OpenedFor { get; private set; }

        public Task<IMailboxClient?> OpenAsync(Guid userId, CancellationToken ct)
        {
            OpenedFor = userId;
            return Task.FromResult<IMailboxClient?>(new Client(this));
        }

        private sealed class Client(FakeMailboxes owner) : IMailboxClient
        {
            public Task<WatchRegistration> WatchInboxAsync(string topicName, CancellationToken ct)
            {
                if (owner.WatchError is not null)
                    throw owner.WatchError;
                return Task.FromResult(owner.NextWatch ?? throw new InvalidOperationException("No watch was arranged."));
            }

            public Task<HistoryBatch> ListAddedSinceAsync(ulong startHistoryId, CancellationToken ct) =>
                Task.FromResult(owner.OnList?.Invoke(startHistoryId) ?? throw new InvalidOperationException("No history was arranged."));

            public Task<MailMessage?> ReadAsync(string messageId, CancellationToken ct)
            {
                if (owner.ReadError is not null)
                    throw owner.ReadError;
                return Task.FromResult(owner.Messages.TryGetValue(messageId, out var message) ? message : null);
            }
        }
    }

    private sealed class CollectingLogger : ILogger<GmailSyncService>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
                Lines.Add(formatter(state, exception));
        }
    }
}
