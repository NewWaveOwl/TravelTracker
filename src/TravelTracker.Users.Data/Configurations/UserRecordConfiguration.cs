using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelTracker.Users.Data.Entities;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Data.Configurations;

/**
 * Maps account/profile invariants and indexes to the Users-owned PostgreSQL table.
 */
internal sealed class UserRecordConfiguration : IEntityTypeConfiguration<UserRecord>
{
    /**
     * Configures persisted identifiers, nullable fields, uniqueness and optimistic concurrency.
     * @param builder The user record mapping.
     */
    public void Configure(EntityTypeBuilder<UserRecord> builder)
    {
        builder.ToTable("UserAccounts", table =>
        {
            table.HasCheckConstraint("CK_UserAccounts_Identity", "length(btrim(\"Issuer\")) > 0 AND length(btrim(\"Subject\")) > 0");
            table.HasCheckConstraint("CK_UserAccounts_Email", "\"Email\" IS NULL OR length(btrim(\"Email\")) > 0");
            table.HasCheckConstraint("CK_UserAccounts_Role", "\"Role\" IN ('traveller', 'administrator')");
            table.HasCheckConstraint("CK_UserAccounts_Status", "\"Status\" IN ('active', 'banned', 'deactivated')");
        });

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id)
            .HasConversion(id => (long)id, value => new Id64(value))
            .HasColumnType("bigint")
            .ValueGeneratedNever();
        builder.Property(record => record.PublicId).HasColumnName("UserId").ValueGeneratedNever();
        builder.Property(record => record.Issuer).IsRequired();
        builder.Property(record => record.Subject).IsRequired();
        builder.Property(record => record.Role).IsRequired();
        builder.Property(record => record.Status).IsRequired();
        builder.Property(record => record.ConcurrencyToken).IsConcurrencyToken().IsRequired();
        builder.HasIndex(record => record.PublicId).IsUnique().HasDatabaseName("IX_UserAccounts_PublicId");
        builder.HasIndex(record => record.NormalizedUsername)
            .IsUnique()
            .HasFilter("\"NormalizedUsername\" IS NOT NULL")
            .HasDatabaseName("IX_UserAccounts_NormalizedUsername");
        builder.HasIndex(record => new { record.Issuer, record.Subject })
            .IsUnique()
            .HasDatabaseName("IX_UserAccounts_Issuer_Subject");
    }
}
