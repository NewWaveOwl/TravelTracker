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
        modelBuilder.Entity<UserPictureRow>(row =>
        {
            row.ToTable("UserPictures");
            row.HasKey(picture => picture.AccountId);
            row.Property(picture => picture.AccountId).ValueGeneratedNever();
            row.HasOne<UserRecord>().WithOne(account => account.Picture)
                .HasForeignKey<UserPictureRow>(picture => picture.AccountId);
        });
        modelBuilder.Entity<BootstrapRow>(row =>
        {
            row.ToTable("AdministratorBootstrap", table => table.HasCheckConstraint("CK_AdministratorBootstrap_Singleton", "\"Id\" = 1"));
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.Id).ValueGeneratedNever();
            row.Property(entity => entity.Issuer).IsRequired();
            row.Property(entity => entity.Subject).IsRequired();
        });
        modelBuilder.Entity<AdministratorAuditRow>(row =>
        {
            row.ToTable("AdministratorAudit");
            row.HasKey(entity => entity.AuditId);
            row.Property(entity => entity.AuditId).ValueGeneratedNever();
            row.Property(entity => entity.Reason).IsRequired();
            row.Property(entity => entity.Action).IsRequired().HasDefaultValue("bootstrap");
        });
    }
}
