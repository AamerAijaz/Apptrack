using Apptrack.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Apptrack.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.Provider).HasMaxLength(50).IsRequired();
        builder.Property(u => u.PictureUrl).HasMaxLength(ProfilePicture.MaxLength);

        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.CreatedAt).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        builder.HasQueryFilter(u => u.DeletedAt == null);
        builder.HasIndex(u => new { u.Email, u.Provider })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}