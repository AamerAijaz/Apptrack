using Apptrack.Data;
using Microsoft.EntityFrameworkCore;

namespace Apptrack.Services;

public sealed record NoticesPage(string Email, IReadOnlyList<NoticeItem> Notices);

public sealed record NoticeItem(Guid Id, string Company, string? Role, string Status, DateTime SentAt);

public sealed class NoticesService(AppDbContext db)
{
    /// <summary>
    /// The person's address and live notices, newest first. Null when the person is missing or soft-deleted.
    /// </summary>
    public async Task<NoticesPage?> LoadAsync(Guid userId, CancellationToken ct)
    {
        var email = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .SingleOrDefaultAsync(ct);
        if (email is null)
            return null;

        var rows = await db.ApplicationNotices
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.SentAt)
            .ThenByDescending(n => n.Id)
            .Select(n => new { n.Id, n.Company, n.Role, n.Status, n.SentAt })
            .ToListAsync(ct);

        var notices = rows
            .Select(n => new NoticeItem(n.Id, n.Company, n.Role, n.Status, DateTime.SpecifyKind(n.SentAt, DateTimeKind.Utc)))
            .ToList();
        return new NoticesPage(email, notices);
    }
}
