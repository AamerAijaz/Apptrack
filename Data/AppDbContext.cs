using Apptrack.Entities;

using Microsoft.EntityFrameworkCore;

namespace Apptrack.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<GoogleAccessToken> GoogleAccessTokens => Set<GoogleAccessToken>();
    public DbSet<GmailWatch> GmailWatches => Set<GmailWatch>();
    public DbSet<ApplicationNotice> ApplicationNotices => Set<ApplicationNotice>();
}