using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelTracker.Trips.Data.Entities;

namespace TravelTracker.Trips.Data.Configurations;

/** Defines PostgreSQL mapping and database-enforced invariants for the trip row. */
internal sealed class TripConfiguration : IEntityTypeConfiguration<TripRow>
{
    /**
     * Configures public identity, owner scoping and the trip's own values.
     *
     * @param builder Trip row mapping.
     */
    public void Configure(EntityTypeBuilder<TripRow> builder)
    {
        builder.ToTable("Trips", table =>
        {
            table.HasCheckConstraint("CK_Trips_Name", "length(btrim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_Trips_Owner", "length(btrim(\"OwnerIssuer\")) > 0 AND length(btrim(\"OwnerSubject\")) > 0");
            table.HasCheckConstraint("CK_Trips_End", "\"EndAt\" >= \"StartAt\"");
        });
        builder.HasKey(row => row.TripId);
        builder.Property(row => row.TripId).ValueGeneratedNever();
        builder.Property(row => row.Revision).HasDefaultValue(1L);
        builder.Property(row => row.Name).IsRequired();
        builder.Property(row => row.TimeZoneId).IsRequired();
        builder.Property(row => row.OwnerIssuer).IsRequired();
        builder.Property(row => row.OwnerSubject).IsRequired();
        builder.HasIndex(row => new { row.OwnerIssuer, row.OwnerSubject });
        builder.HasMany(row => row.Legs).WithOne(leg => leg.Trip).HasForeignKey(leg => leg.TripId).OnDelete(DeleteBehavior.Cascade);
    }
}
