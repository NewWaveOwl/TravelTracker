using TravelTracker.Domain.Types;

namespace TravelTracker.Domain.Models;

/**
 * Represents an attraction that a traveler can add to a trip.
 */
public sealed class Attraction
{
    /**
     * Gets or sets the unique identifier of the attraction.
     */
    public EntityId<Attraction> AttractionId { get; set; }

    /**
     * Gets or sets the attraction name shown to users.
     */
    public string Name { get; set; } = string.Empty;

    /**
     * Gets or sets the street or location address of the attraction.
     */
    public string Address { get; set; } = string.Empty;

    /**
     * Gets or sets the latitude used to display the attraction on the map.
     */
    public decimal Latitude { get; set; }

    /**
     * Gets or sets the longitude used to display the attraction on the map.
     */
    public decimal Longitude { get; set; }

    /**
     * Gets or sets the estimated number of minutes needed to visit the attraction.
     */
    public int EstimatedVisitMinutes { get; set; }

    /**
     * Gets or sets the optional manually entered description.
     */
    public string? Description { get; set; }

    /**
     * Gets or sets the optional manually entered rating from zero to five.
     */
    public decimal? Rating { get; set; }
}
