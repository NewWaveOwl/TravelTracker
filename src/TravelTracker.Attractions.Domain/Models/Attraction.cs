using TravelTracker.Attractions.Domain.Types;

namespace TravelTracker.Attractions.Domain.Models;

/**
 * Represents an attraction that a traveler can add to a trip.
 */
public sealed class Attraction
{
    public string OwnerIssuer { get; set; } = string.Empty;
    public string OwnerSubject { get; set; } = string.Empty;
    public bool IsShared { get; set; }

    /**
     * Checks intrinsic attraction values independently of HTTP and storage.
     * @return Field errors; empty when all invariants hold.
     */
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Name)) errors["name"] = ["Name must not be empty."];
        if (Address is null) errors["address"] = ["Address cannot be null."];
        if (Latitude is < -90m or > 90m) errors["lat"] = ["Latitude must be between -90 and 90."];
        if (Longitude is < -180m or > 180m) errors["lon"] = ["Longitude must be between -180 and 180."];
        if (Rating is < 0m or > 5m) errors["rating"] = ["Rating must be between 0 and 5."];
        if (EstimatedVisitMinutes < 0) errors["estimatedVisitMinutes"] = ["Visit duration must be nonnegative."];
        return errors;
    }
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
