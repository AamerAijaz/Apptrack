using Apptrack.Data;
using Microsoft.EntityFrameworkCore;

namespace Apptrack.Services;

public sealed record SessionProfile(string Email, string? Picture);

public sealed class SessionProfileService(AppDbContext db)
{
    /// <summary>
    /// The person's address and stored picture. Null when the person is missing or soft-deleted.
    /// </summary>
    public async Task<SessionProfile?> LoadAsync(Guid userId, CancellationToken ct)
    {
        var row = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.PictureUrl })
            .SingleOrDefaultAsync(ct);
        if (row is null)
            return null;

        return new SessionProfile(row.Email, row.PictureUrl);
    }
}
