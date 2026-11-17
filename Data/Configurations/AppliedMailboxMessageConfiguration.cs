using Apptrack.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Apptrack.Data.Configurations;

public sealed class AppliedMailboxMessageConfiguration : IEntityTypeConfiguration<AppliedMailboxMessage>
{
    public void Configure(EntityTypeBuilder<AppliedMailboxMessage> builder)
    {
        builder.ToTable("applied_mailbox_messages");

        builder.HasKey(m => new { m.UserId, m.MessageId });
        builder.Property(m => m.MessageId).HasMaxLength(ApplicationNotice.MessageIdMaxLength).IsRequired();
        builder.Property(m => m.NoticeId).IsRequired();

        builder.HasOne<ApplicationNotice>()
            .WithMany()
            .HasForeignKey(m => m.NoticeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
