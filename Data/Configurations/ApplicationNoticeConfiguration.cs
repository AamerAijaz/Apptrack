using Apptrack.Entities;
using Apptrack.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Apptrack.Data.Configurations;

public sealed class ApplicationNoticeConfiguration : IEntityTypeConfiguration<ApplicationNotice>
{
    public void Configure(EntityTypeBuilder<ApplicationNotice> builder)
    {
        builder.ToTable("application_notices", table =>
            table.HasCheckConstraint(
                "CK_application_notices_Status",
                NoticeStatus.CheckConstraintSql));

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();

        builder.Property(n => n.MessageId).HasMaxLength(ApplicationNotice.MessageIdMaxLength).IsRequired();
        builder.Property(n => n.UserId).IsRequired();
        builder.Property(n => n.Company).IsRequired();
        builder.Property(n => n.Status).HasMaxLength(ApplicationNotice.StatusMaxLength).IsRequired();
        builder.Property(n => n.SentAt).IsRequired();
        builder.Property(n => n.Content).IsRequired();

        builder.Property(n => n.CreatedAt).IsRequired();
        builder.Property(n => n.CreatedAt).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(n => new { n.UserId, n.MessageId }).IsUnique();
        builder.HasIndex(n => new { n.UserId, n.Status, n.CreatedAt, n.Id })
            .IsDescending(false, false, true, true)
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_application_notices_lane");
        builder.HasQueryFilter(n => n.DeletedAt == null);
    }
}
