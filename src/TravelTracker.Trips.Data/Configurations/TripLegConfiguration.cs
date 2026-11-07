using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelTracker.Trips.Data.Entities;

namespace TravelTracker.Trips.Data.Configurations;

/**
 * Defines PostgreSQL mapping for ordered legs. Place and transport types are short codes; snapshot names and
 * addresses are unbounded because their limits belong to the owning catalogue services.
 */
internal sealed class TripLegConfiguration : IEntityTypeConfiguration<TripLegRow>
{
    /**
     * Configures the composite key, ordering rule, snapshot columns and time ordering.
     *
     * @param builder Leg row mapping.
     */
    public void Configure(EntityTypeBuilder<TripLegRow> builder)
    {
        builder.ToTable("TripLegs", table =>
        {
            table.HasCheckConstraint("CK_TripLegs_Sequence", "\"Sequence\" > 0");
            table.HasCheckConstraint("CK_TripLegs_Times", "\"ArrivalAt\" > \"DepartureAt\"");
        });
        builder.HasKey(row => new { row.TripId, row.Sequence });
        builder.Property(row => row.FromPlaceType).HasMaxLength(32);
        builder.Property(row => row.ToPlaceType).HasMaxLength(32);
        builder.Property(row => row.TransportCode).HasMaxLength(32);
        builder.Property(row => row.FromLatitude).HasColumnType("numeric");
        builder.Property(row => row.FromLongitude).HasColumnType("numeric");
        builder.Property(row => row.ToLatitude).HasColumnType("numeric");
        builder.Property(row => row.ToLongitude).HasColumnType("numeric");
    }
}
