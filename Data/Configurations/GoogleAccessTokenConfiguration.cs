using Apptrack.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Apptrack.Data.Configurations;

public sealed class GoogleAccessTokenConfiguration : IEntityTypeConfiguration<GoogleAccessToken>
{
    public void Configure(EntityTypeBuilder<GoogleAccessToken> builder)
    {
        builder.ToTable("google_access_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        builder.Property(t => t.UserId).IsRequired();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(t => t.AccessToken).IsRequired();
        builder.Property(t => t.RefreshToken).IsRequired();
        builder.Property(t => t.IssuedUtc).IsRequired();
        builder.Property(t => t.ExpiresInSeconds).IsRequired();

        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.CreatedAt).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        builder.HasQueryFilter(t => t.DeletedAt == null);
        builder.HasIndex(t => t.UserId)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}
