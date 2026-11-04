using Microsoft.EntityFrameworkCore;
using TravelTracker.Users.Data.Entities;

namespace TravelTracker.Users.Data;

/**
 * Owns the Users database, account identities, permanent bootstrap marker and audit rows.
 * @param options PostgreSQL connection and provider configuration.
 */
public sealed class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContext(options)
{
    internal DbSet<UserRow> Accounts => Set<UserRow>();
    internal DbSet<BootstrapRow> Bootstrap => Set<BootstrapRow>();
    internal DbSet<AdministratorAuditRow> AdministratorAudit => Set<AdministratorAuditRow>();

    /**
     * Maps service-owned persistence constraints; bootstrap identity remains even if an account is removed later.
     * @param modelBuilder Entity model configuration.
     */
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRow>(row =>
        {
            row.ToTable("UserAccounts", table =>
            {
                table.HasCheckConstraint("CK_UserAccounts_Identity", "length(btrim(\"Issuer\")) > 0 AND length(btrim(\"Subject\")) > 0");
                table.HasCheckConstraint("CK_UserAccounts_Email", "length(btrim(\"Email\")) > 0");
                table.HasCheckConstraint("CK_UserAccounts_Role", "\"Role\" IN ('traveller', 'administrator')");
                table.HasCheckConstraint("CK_UserAccounts_Status", "\"Status\" IN ('active', 'banned', 'deactivated')");
            });
            row.HasKey(x => x.UserId);
            row.Property(x => x.UserId).ValueGeneratedNever();
            row.Property(x => x.Issuer).IsRequired();
            row.Property(x => x.Subject).IsRequired();
            row.Property(x => x.Email).IsRequired();
            row.Property(x => x.Role).IsRequired();
            row.Property(x => x.Status).IsRequired();
            row.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        });
        modelBuilder.Entity<BootstrapRow>(row =>
        {
            row.ToTable("AdministratorBootstrap", table => table.HasCheckConstraint("CK_AdministratorBootstrap_Singleton", "\"Id\" = 1"));
            row.HasKey(x => x.Id);
            row.Property(x => x.Id).ValueGeneratedNever();
            row.Property(x => x.Issuer).IsRequired();
            row.Property(x => x.Subject).IsRequired();
        });
        modelBuilder.Entity<AdministratorAuditRow>(row =>
        {
            row.ToTable("AdministratorAudit");
            row.HasKey(x => x.AuditId);
            row.Property(x => x.AuditId).ValueGeneratedNever();
            row.Property(x => x.Reason).IsRequired();
            row.Property(x => x.Action).IsRequired().HasDefaultValue("bootstrap");
        });
    }
}
