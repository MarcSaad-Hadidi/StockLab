using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", table =>
        {
            table.HasCheckConstraint("CK_Users_DisplayName_NotBlank", "LTRIM(RTRIM([DisplayName])) <> ''");
            table.HasCheckConstraint("CK_Users_Email_NotBlank", "LTRIM(RTRIM([Email])) <> ''");
            table.HasCheckConstraint("CK_Users_NormalizedEmail_NotBlank", "LTRIM(RTRIM([NormalizedEmail])) <> ''");
            table.HasCheckConstraint("CK_Users_PasswordHash_NotBlank", "LTRIM(RTRIM([PasswordHash])) <> ''");
            table.HasCheckConstraint("CK_Users_UpdatedAtUtc", "[UpdatedAtUtc] >= [CreatedAtUtc]");
        });
        builder.HasKey(user => user.Id);
        builder.Property(user => user.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(254).IsRequired();
        builder.Property(user => user.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(1024).IsRequired();
        builder.Property(user => user.CreatedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(user => user.UpdatedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(user => user.Version).IsRowVersion().HasColumnType("rowversion");
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
    }
}
