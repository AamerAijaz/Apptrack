using Apptrack.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Apptrack.Data.Configurations;

public sealed class GmailWatchConfiguration : IEntityTypeConfiguration<GmailWatch>
{
    public void Configure(EntityTypeBuilder<GmailWatch> builder)
    {
        builder.ToTable("gmail_watches");

        builder.HasKey(w => w.UserId);
        builder.Property(w => w.UserId).ValueGeneratedNever();

        builder.Property(w => w.HistoryId)
            .HasConversion(v => (decimal)v, v => (ulong)v)
            .HasColumnType("numeric(20,0)")
            .IsRequired();

        builder.Property(w => w.WatchExpiration).IsRequired();
        builder.Property(w => w.CreatedAt).IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
