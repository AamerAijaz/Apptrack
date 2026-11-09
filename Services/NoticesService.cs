using Apptrack.Data;
using Microsoft.EntityFrameworkCore;

namespace Apptrack.Services;

public sealed record NoticeCard(Guid Id, string Company, string? Role, DateTime SentAt);

/// <summary>
/// One window of one status. Null from <see cref="NoticesService.GetNotices"/> means the person is missing.
/// </summary>
public sealed record NoticePage(string Email, IReadOnlyList<NoticeCard> Notices);

public sealed class NoticesService(AppDbContext db)
{
    public const int BoardLimit = 12;
    public const int MaxLimit = 50;

    /// <summary>
    /// One status window for a live person, newest sent time then id. Null when the person is missing or soft-deleted.
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
            .OrderByDescending(n => n.SentAt)
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
}
