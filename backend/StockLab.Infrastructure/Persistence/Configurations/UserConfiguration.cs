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
            table.HasCheckConstraint("CK_Users_PasswordHash_NotBlank", "[PasswordHash] IS NULL OR LTRIM(RTRIM([PasswordHash])) <> ''");
            table.HasCheckConstraint("CK_Users_AuthenticationMethod", "[PasswordHash] IS NOT NULL OR [GoogleSubject] IS NOT NULL");
            table.HasCheckConstraint("CK_Users_GoogleSubject_NotBlank", "[GoogleSubject] IS NULL OR LTRIM(RTRIM([GoogleSubject])) <> ''");
            table.HasCheckConstraint("CK_Users_UpdatedAtUtc", "[UpdatedAtUtc] >= [CreatedAtUtc]");
            table.HasCheckConstraint("CK_Users_PreferredLanguage", "[PreferredLanguage] IN ('en', 'fr')");
            table.HasCheckConstraint("CK_Users_SessionVersion", "[SessionVersion] >= 0");
        });
        builder.HasKey(user => user.Id);
        builder.Property(user => user.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(254).IsRequired();
        builder.Property(user => user.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(1024);
        builder.Property(user => user.GoogleSubject).HasMaxLength(255).IsConcurrencyToken();
        builder.Property(user => user.PreferredLanguage).HasMaxLength(2).HasDefaultValue("en").IsRequired();
        builder.Property(user => user.EmailNotifications).HasDefaultValue(false);
        builder.Property(user => user.TradeAlerts).HasDefaultValue(false);
        builder.Property(user => user.MarketingCommunications).HasDefaultValue(false);
        builder.Property(user => user.DarkMode).HasDefaultValue(false);
        builder.Property(user => user.SessionVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(user => user.PasswordResetTokenHash).HasColumnType("varbinary(32)").HasMaxLength(32);
        builder.Property(user => user.PasswordResetExpiresAtUtc).HasColumnType("datetime2(7)");
        builder.Property(user => user.PasswordResetRequestedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(user => user.CreatedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(user => user.UpdatedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(user => user.Version).IsRowVersion().HasColumnType("rowversion");
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
        builder.HasIndex(user => user.GoogleSubject).IsUnique().HasFilter("[GoogleSubject] IS NOT NULL");
    }
}
