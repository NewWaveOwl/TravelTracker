using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelTracker.Attractions.Data.Entities;

namespace TravelTracker.Attractions.Data.Configurations;

/**

 * Defines PostgreSQL mappings without leaking persistence keys to the API.

 */
internal sealed class AttractionConfiguration : IEntityTypeConfiguration<AttractionRow>
{
    /**
     * Configures identifiers, precise numeric values and relational invariants.
     * @param builder The attraction row mapping.
     */
    public void Configure(EntityTypeBuilder<AttractionRow> builder)
    {
        builder.ToTable("Attractions", table =>
        {
            table.HasCheckConstraint("CK_Attractions_Name", "length(btrim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_Attractions_Coordinates", "\"Latitude\" BETWEEN -90 AND 90 AND \"Longitude\" BETWEEN -180 AND 180");
            table.HasCheckConstraint("CK_Attractions_Rating", "\"Rating\" IS NULL OR \"Rating\" BETWEEN 0 AND 5");
            table.HasCheckConstraint("CK_Attractions_Duration", "\"EstimatedVisitMinutes\" >= 0");
            table.HasCheckConstraint("CK_Attractions_Owner", "\"IsShared\" OR (length(\"OwnerIssuer\") > 0 AND length(\"OwnerSubject\") > 0)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.Property(row => row.Latitude).HasColumnType("numeric");
        builder.Property(row => row.Longitude).HasColumnType("numeric");
        builder.Property(row => row.Rating).HasColumnType("numeric");
    }
}
