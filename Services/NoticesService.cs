using Apptrack.Data;
using Apptrack.Services.NoticeExtraction;
using Microsoft.EntityFrameworkCore;

namespace Apptrack.Services;

public sealed record NoticeCard(Guid Id, string Company, string? Role, DateTime SentAt);

/// <summary>
/// One window of one status. Null from <see cref="NoticesService.GetNotices"/> means the person is missing.
/// </summary>
public sealed record NoticePage(string Email, IReadOnlyList<NoticeCard> Notices);

/// <summary>A UTC instant that was not produced by <see cref="DateTime.SpecifyKind"/>.</summary>
internal readonly record struct UtcInstant
{
    private UtcInstant(DateTime utc) => Value = utc;

    public DateTime Value { get; }

    /// <summary>Kind must already be Utc, and the value must not be default.</summary>
    public static UtcInstant? FromUtc(DateTime value)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
            return null;
        return new UtcInstant(value);
    }

    public static UtcInstant From(DateTimeOffset value) => new(value.UtcDateTime);
}

/// <summary>Trimmed, non-blank company. The only trim.</summary>
internal readonly record struct CompanyName
{
    public const int MaxLength = 200;

    private CompanyName(string value) => Value = value;

    public string Value { get; }

    public static CompanyName? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return new CompanyName(raw.Trim());
    }
}

/// <summary>A named role. Blank is unnamed, which is null, not a value.</summary>
internal readonly record struct RoleName
{
    public const int MaxLength = 200;

    private RoleName(string value) => Value = value;

    public string Value { get; }

    public static RoleName? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return new RoleName(raw.Trim());
    }
}

/// <summary>
/// Formats a hand-made message id and recognizes that same shape. Gmail ids are hex without this prefix.
/// </summary>
internal static class HandMessageId
{
    private const string Prefix = "h.";

    public static string Format(Guid key) => Prefix + key.ToString("N");

    public static bool IsHand(string? messageId)
    {
        if (messageId is null || messageId.Length != Prefix.Length + 32)
            return false;
        if (!messageId.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        foreach (var ch in messageId.AsSpan(Prefix.Length))
        {
            var hex = ch is >= '0' and <= '9' or >= 'a' and <= 'f';
            if (!hex)
                return false;
        }

        return true;
    }
}

/// <summary>
/// What is allowed to become one row. A hand notice has no sent time and no content.
/// A mailbox notice has a Gmail message id, the mail's sent time, and the mail body.
/// </summary>
public abstract class IncomingNotice
{
    private IncomingNotice() { }

    public sealed class ByHand : IncomingNotice
    {
        private ByHand(Guid key, CompanyName company, RoleName? role, NoticeStatus status)
        {
            Key = key;
            Company = company;
            Role = role;
            Status = status;
        }

        internal Guid Key { get; }
        internal CompanyName Company { get; }
        internal RoleName? Role { get; }
        internal NoticeStatus Status { get; }

        internal static ByHand? Try(string? company, string? role, string? statusLabel, string? key)
        {
            if (!Guid.TryParse(key, out var parsed) || parsed == Guid.Empty)
                return null;

            var companyName = CompanyName.Parse(company);
            if (companyName is null || companyName.Value.Value.Length > CompanyName.MaxLength)
                return null;

            if (!TryRole(role, out var roleName))
                return null;

            var status = NoticeStatus.FromLabel(statusLabel);
            if (status is null)
                return null;

            return new ByHand(parsed, companyName.Value, roleName, status);
        }

        private static bool TryRole(string? raw, out RoleName? role)
        {
            role = null;
            if (string.IsNullOrWhiteSpace(raw))
                return true;

            var named = RoleName.Parse(raw);
            if (named is null || named.Value.Value.Length > RoleName.MaxLength)
                return false;

            role = named;
            return true;
        }
    }

    public sealed class ByMailbox : IncomingNotice
    {
        private ByMailbox(
            string messageId,
            CompanyName company,
            RoleName? role,
            NoticeStatus status,
            UtcInstant sentAt,
            string content)
        {
            MessageId = messageId;
            Company = company;
            Role = role;
            Status = status;
            SentAt = sentAt;
            Content = content;
        }

        internal string MessageId { get; }
        internal CompanyName Company { get; }
        internal RoleName? Role { get; }
        internal NoticeStatus Status { get; }
        internal UtcInstant SentAt { get; }
        internal string Content { get; }

        internal static ByMailbox? FromExtraction(
            string? messageId,
            NoticeExtraction.ApplicationNotice extracted,
            string? content)
        {
            if (string.IsNullOrWhiteSpace(messageId)
                || messageId.Length > Entities.ApplicationNotice.MessageIdMaxLength
                || HandMessageId.IsHand(messageId))
                return null;
            if (content is null)
                return null;

            var company = CompanyName.Parse(extracted.Company);
            if (company is null)
                return null;

            RoleName? role = null;
            if (extracted.Role.Title is not null)
            {
                role = RoleName.Parse(extracted.Role.Title);
                if (role is null)
                    return null;
            }

            var status = NoticeStatus.FromLabel(extracted.Status.DisplayName());
            if (status is null)
                return null;

            return new ByMailbox(messageId, company.Value, role, status, UtcInstant.From(extracted.SentAt), content);
        }
    }

    public static ByHand? TryHand(string? company, string? role, string? statusLabel, string? key) =>
        ByHand.Try(company, role, statusLabel, key);

    /// <summary>
    /// Mailbox boundary. Null when the message id is blank, too long, or already a hand id,
    /// when the extraction status has no label, or when content is null. A null result is skipped.
    /// </summary>
    public static ByMailbox? FromMailbox(
        string? messageId,
        NoticeExtraction.ApplicationNotice extracted,
        string? content) =>
        ByMailbox.FromExtraction(messageId, extracted, content);
}

public abstract record MoveNoticeResult
{
    private MoveNoticeResult() { }

    /// <summary>The cookie user does not exist or is soft-deleted.</summary>
    public sealed record MissingPerson : MoveNoticeResult;

    /// <summary>No live notice with this id belongs to the person.</summary>
    public sealed record NotFound : MoveNoticeResult;

    /// <summary>The stage is now the requested one. A repeat move returns the same card.</summary>
    public sealed record Stored(NoticeCard Card, NoticeStatus Status) : MoveNoticeResult;
}

public abstract record AddNoticeResult
{
    private AddNoticeResult() { }

    /// <summary>The cookie user does not exist or is soft-deleted.</summary>
    public sealed record MissingPerson : AddNoticeResult;

    /// <summary>(UserId, message id) exists and DeletedAt is set. Do not insert. Do not return a card.</summary>
    public sealed record Retired : AddNoticeResult;

    /// <summary>Created now, or already stored and still live. The card is the stored row.</summary>
    public sealed record Stored(NoticeCard Card, NoticeStatus Status) : AddNoticeResult;
}

public sealed class NoticesService(AppDbContext db, TimeProvider time)
{
    public const int BoardLimit = 12;
    public const int MaxLimit = 50;

    /// <summary>
    /// One status window for a live person, newest created first, then newest id.
    /// Null when the person is missing or soft-deleted.
    /// </summary>
    public async Task<NoticePage?> GetNotices(
        Guid userId, NoticeStatus status, int limit, int offset, CancellationToken ct)
    {
        var email = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .SingleOrDefaultAsync(ct);
        if (email is null)
            return null;

        var rows = await db.ApplicationNotices
            .Where(n => n.UserId == userId && n.Status == status.Label)
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Select(n => new { n.Id, n.Company, n.Role, n.SentAt })
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        var notices = rows
            .Select(n => new NoticeCard(n.Id, n.Company, n.Role, DateTime.SpecifyKind(n.SentAt, DateTimeKind.Utc)))
            .ToList();
        return new NoticePage(email, notices);
    }

    /// <summary>
    /// The only insert. Hand and mailbox share the unique (UserId, MessageId) check, including soft-deleted rows.
    /// A hand message id is built only by <see cref="HandMessageId.Format"/>. Hand Sent at and Created at are one
    /// clock reading. Mailbox Sent at is the mail instant. Its Created at, and the id, are a new clock reading.
    /// A unique-index race detaches the added entry and reads again. Replay returns the stored card.
    /// </summary>
    public async Task<AddNoticeResult> Add(Guid userId, IncomingNotice notice, CancellationToken ct)
    {
        var person = await db.Users.AnyAsync(u => u.Id == userId, ct);
        if (!person)
            return new AddNoticeResult.MissingPerson();

        var recorded = UtcInstant.From(time.GetUtcNow());
        var candidate = notice switch
        {
            IncomingNotice.ByHand hand => new Entities.ApplicationNotice(
                userId,
                HandMessageId.Format(hand.Key),
                hand.Company,
                hand.Role,
                hand.Status,
                recorded,
                recorded,
                ""),
            IncomingNotice.ByMailbox mail => new Entities.ApplicationNotice(
                userId,
                mail.MessageId,
                mail.Company,
                mail.Role,
                mail.Status,
                mail.SentAt,
                recorded,
                mail.Content),
            _ => throw new InvalidOperationException("A notice is either entered by hand or read from mail."),
        };

        var stored = await InsertOnce(candidate, ct);
        if (stored.DeletedAt is not null)
            return new AddNoticeResult.Retired();

        var status = NoticeStatus.FromLabel(stored.Status)
            ?? throw new InvalidOperationException("A stored notice has a status the board does not know.");
        return new AddNoticeResult.Stored(ToCard(stored), status);
    }

    /// <summary>
    /// Moves one live notice to a stage. Recorded time and Sent at are unchanged.
    /// The same stage returns the stored card without writing a new row.
    /// </summary>
    public async Task<MoveNoticeResult> Move(Guid userId, Guid noticeId, NoticeStatus status, CancellationToken ct)
    {
        var person = await db.Users.AnyAsync(u => u.Id == userId, ct);
        if (!person)
            return new MoveNoticeResult.MissingPerson();

        var notice = await db.ApplicationNotices
            .SingleOrDefaultAsync(n => n.Id == noticeId && n.UserId == userId, ct);
        if (notice is null)
            return new MoveNoticeResult.NotFound();

        if (notice.Status != status.Label)
        {
            notice.MoveTo(status);
            await db.SaveChangesAsync(ct);
        }

        return new MoveNoticeResult.Stored(ToCard(notice), status);
    }

    private async Task<Entities.ApplicationNotice> InsertOnce(
        Entities.ApplicationNotice candidate, CancellationToken ct)
    {
        var existing = await Find(candidate.UserId, candidate.MessageId, ct);
        if (existing is not null)
            return existing;

        db.ApplicationNotices.Add(candidate);
        try
        {
            await db.SaveChangesAsync(ct);
            return candidate;
        }
        catch (DbUpdateException)
        {
            db.Entry(candidate).State = EntityState.Detached;
            var stored = await Find(candidate.UserId, candidate.MessageId, ct);
            if (stored is null)
                throw;
            return stored;
        }
    }

    private Task<Entities.ApplicationNotice?> Find(Guid userId, string messageId, CancellationToken ct) =>
        db.ApplicationNotices
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(n => n.UserId == userId && n.MessageId == messageId, ct);

    private static NoticeCard ToCard(Entities.ApplicationNotice row) =>
        new(row.Id, row.Company, row.Role, DateTime.SpecifyKind(row.SentAt, DateTimeKind.Utc));
}
