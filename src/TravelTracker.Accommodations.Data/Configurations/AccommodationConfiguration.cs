using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelTracker.Accommodations.Data.Entities;

namespace TravelTracker.Accommodations.Data.Configurations;

/** Defines PostgreSQL mapping and database-enforced accommodation invariants. */
internal sealed class AccommodationConfiguration : IEntityTypeConfiguration<AccommodationRow>
{
    /**
     * Configures public identity, location values, visibility and ownership.
     * @param builder Accommodation row mapping.
     */
    public void Configure(EntityTypeBuilder<AccommodationRow> builder)
    {
        builder.ToTable("Accommodations", table =>
        {
            table.HasCheckConstraint("CK_Accommodations_Name", "length(btrim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_Accommodations_Coordinates", "\"Latitude\" BETWEEN -90 AND 90 AND \"Longitude\" BETWEEN -180 AND 180");
            table.HasCheckConstraint("CK_Accommodations_Owner", "length(btrim(\"OwnerIssuer\")) > 0 AND length(btrim(\"OwnerSubject\")) > 0");
        });
        builder.HasKey(row => row.AccommodationId);
        builder.Property(row => row.AccommodationId).ValueGeneratedNever();
        builder.Property(row => row.Name).IsRequired();
        builder.Property(row => row.Address).IsRequired();
        builder.Property(row => row.Latitude).HasColumnType("numeric");
        builder.Property(row => row.Longitude).HasColumnType("numeric");
        builder.Property(row => row.OwnerIssuer).IsRequired();
        builder.Property(row => row.OwnerSubject).IsRequired();
        builder.HasIndex(row => new { row.OwnerIssuer, row.OwnerSubject, row.IsShared });
    }
}
