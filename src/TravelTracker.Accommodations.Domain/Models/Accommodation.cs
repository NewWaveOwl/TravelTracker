namespace TravelTracker.Accommodations.Domain.Models;

/**
 * Represents a lodging place in the Accommodations catalogue.
 */
public sealed class Accommodation
{
    /**
     * Gets or sets the public identifier assigned by the service.
     */
    public Guid AccommodationId { get; set; }

    /**
     * Gets or sets the issuer of the validated identity that created the place.
     */
    public string OwnerIssuer { get; set; } = string.Empty;

    /**
     * Gets or sets the subject of the validated identity that created the place.
     */
    public string OwnerSubject { get; set; } = string.Empty;

    /**
     * Gets or sets whether the place is visible in the shared catalogue.
     */
    public bool IsShared { get; set; }

    /**
     * Gets or sets the display name of the accommodation.
     */
    public string Name { get; set; } = string.Empty;

    /**
     * Gets or sets the street or location address of the accommodation.
     */
    public string Address { get; set; } = string.Empty;

    /**
     * Gets or sets the latitude of the accommodation.
     */
    public decimal Latitude { get; set; }

    /**
     * Gets or sets the longitude of the accommodation.
     */
    public decimal Longitude { get; set; }

    /**
     * Validates the intrinsic location and identity values.
     * @return Field errors; empty when all invariants hold.
     */
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (AccommodationId == Guid.Empty) errors["accommodationId"] = ["Accommodation identifier must not be empty."];
        if (string.IsNullOrWhiteSpace(OwnerIssuer) || string.IsNullOrWhiteSpace(OwnerSubject))
            errors["owner"] = ["A validated owner identity is required."];
        if (string.IsNullOrWhiteSpace(Name)) errors["name"] = ["Name must not be empty."];
        if (Address is null) errors["address"] = ["Address cannot be null."];
        if (Latitude is < -90m or > 90m) errors["lat"] = ["Latitude must be between -90 and 90."];
        if (Longitude is < -180m or > 180m) errors["lon"] = ["Longitude must be between -180 and 180."];
        return errors;
    }
}
