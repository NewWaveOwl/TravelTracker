using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelTracker.Settings.Data.Entities;

namespace TravelTracker.Settings.Data.Configurations;

/** defines the PostgreSQL mapping and the database-enforced preference invariants. */
internal sealed class SettingsConfiguration : IEntityTypeConfiguration<SettingsRow>
{
    /**
     * configures the owner key, precise coordinates and check constraints.
     * @param builder the settings row mapping.
     */
    public void Configure(EntityTypeBuilder<SettingsRow> builder)
    {
        builder.ToTable("UserSettings", table =>
        {
            table.HasCheckConstraint("CK_UserSettings_Owner",
                "length(btrim(\"OwnerIssuer\")) > 0 AND length(btrim(\"OwnerSubject\")) > 0");
            table.HasCheckConstraint("CK_UserSettings_Theme", "\"Theme\" IN ('system', 'light', 'dark')");
            table.HasCheckConstraint("CK_UserSettings_Coordinates",
                "\"DefaultMapLatitude\" BETWEEN -90 AND 90 AND \"DefaultMapLongitude\" BETWEEN -180 AND 180");
            table.HasCheckConstraint("CK_UserSettings_Zoom", "\"DefaultMapZoom\" BETWEEN 0 AND 20");
            table.HasCheckConstraint("CK_UserSettings_Revision", "\"Revision\" >= 1");
            table.HasCheckConstraint("CK_UserSettings_TransportMode",
                "\"PreferredTransportMode\" IN ('car', 'bus', 'train', 'pedestrian', 'bike', 'plane')");
        });
        builder.HasKey(row => new { row.OwnerIssuer, row.OwnerSubject });
        builder.Property(row => row.OwnerIssuer).IsRequired();
        builder.Property(row => row.OwnerSubject).IsRequired();
        builder.Property(row => row.Theme).IsRequired();
        builder.Property(row => row.DefaultMapLatitude).HasColumnType("numeric");
        builder.Property(row => row.DefaultMapLongitude).HasColumnType("numeric");
        builder.Property(row => row.PreferredTransportMode).IsRequired();
        builder.Property(row => row.TimeZone).IsRequired();
        // the revision guards replaces: EF adds it to the UPDATE's WHERE clause, so a stale write changes no rows.
        builder.Property(row => row.Revision).IsConcurrencyToken();
    }
}
