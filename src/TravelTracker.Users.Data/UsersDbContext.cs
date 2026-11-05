using Microsoft.EntityFrameworkCore;
using TravelTracker.Users.Data.Configurations;
using TravelTracker.Users.Data.Entities;

namespace TravelTracker.Users.Data;

/**
 * Owns the Users database, account profiles, permanent bootstrap marker and audit rows.
 * @param options PostgreSQL connection and provider configuration.
 */
public sealed class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContext(options)
{
    internal DbSet<UserRecord> Accounts => Set<UserRecord>();
    internal DbSet<BootstrapRow> Bootstrap => Set<BootstrapRow>();
    internal DbSet<AdministratorAuditRow> AdministratorAudit => Set<AdministratorAuditRow>();

    /**
     * Applies service-owned mappings while retaining permanent administrator bootstrap state.
     * @param modelBuilder Entity model configuration.
     */
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UserRecordConfiguration());
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
