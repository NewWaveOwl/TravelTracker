using Microsoft.EntityFrameworkCore;
using TravelTracker.Transport.Data.Entities;

namespace TravelTracker.Transport.Data;

/** Owns only Transport entries and reusable locations.
 * @param options Service-owned database options.
 */
public sealed class TransportDbContext(DbContextOptions<TransportDbContext> options) : DbContext(options)
{
    public DbSet<TransportModeRow> TransportModes => Set<TransportModeRow>();
    public DbSet<RoutePointRow> RoutePoints => Set<RoutePointRow>();

    /** Applies durable identity and input constraints.
     * @param model Model under construction.
     */
    protected override void OnModelCreating(ModelBuilder model)
    {
        var modes = model.Entity<TransportModeRow>();
        modes.ToTable("TransportModes", table => {
            table.HasCheckConstraint("CK_Transport_Code", "\"Code\" IN ('car','bus','train','pedestrian','bike','plane')");
            table.HasCheckConstraint("CK_Transport_Name", "length(btrim(\"DisplayName\")) > 0");
        });
        modes.HasKey(row => row.Id);
        modes.Property(row => row.Id).ValueGeneratedNever();
        modes.HasIndex(row => row.PublicId).IsUnique();
        modes.Property(row => row.Code).IsRequired();
        modes.Property(row => row.DisplayName).IsRequired();
        var points = model.Entity<RoutePointRow>();
        points.ToTable("RoutePoints", table => {
            table.HasCheckConstraint("CK_Point_Type", "\"Type\" IN ('accommodation','attraction','transport_stop')");
            table.HasCheckConstraint("CK_Point_Name", "length(btrim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_Point_Latitude", "\"Latitude\" BETWEEN -90 AND 90");
            table.HasCheckConstraint("CK_Point_Longitude", "\"Longitude\" BETWEEN -180 AND 180");
        });
        points.HasKey(row => row.Id);
        points.Property(row => row.Id).ValueGeneratedNever();
        points.HasIndex(row => row.PublicId).IsUnique();
        points.Property(row => row.Type).IsRequired();
        points.Property(row => row.Name).IsRequired();
        points.Property(row => row.Address).IsRequired();
    }
}
