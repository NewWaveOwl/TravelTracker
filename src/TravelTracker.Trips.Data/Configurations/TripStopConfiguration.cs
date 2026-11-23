using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelTracker.Trips.Data.Entities;

namespace TravelTracker.Trips.Data.Configurations;

/** Defines PostgreSQL storage for ordered stop snapshots, independent of any journey times. */
internal sealed class TripStopConfiguration : IEntityTypeConfiguration<TripStopRow>
{
    /**
     * Configures the stop key and basic snapshot constraints without prescribing place composition.
     * @param builder Stop row mapping.
     */
    public void Configure(EntityTypeBuilder<TripStopRow> builder)
    {
        builder.ToTable("TripStops", table =>
        {
            table.HasCheckConstraint("CK_TripStops_Sequence", "\"Sequence\" > 0");
            table.HasCheckConstraint("CK_TripStops_PlaceId", "\"PlaceId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_TripStops_Type", "\"PlaceType\" IN ('accommodation', 'attraction', 'transport_stop')");
            table.HasCheckConstraint("CK_TripStops_Name", "length(btrim(\"PlaceName\")) > 0");
            table.HasCheckConstraint("CK_TripStops_Latitude", "\"Latitude\" >= -90 AND \"Latitude\" <= 90");
            table.HasCheckConstraint("CK_TripStops_Longitude", "\"Longitude\" >= -180 AND \"Longitude\" <= 180");
        });
        builder.HasKey(row => new { row.TripId, row.Sequence });
        builder.Property(row => row.PlaceType).HasMaxLength(32);
        builder.Property(row => row.Latitude).HasColumnType("numeric");
        builder.Property(row => row.Longitude).HasColumnType("numeric");
    }
}
