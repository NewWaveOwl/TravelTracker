using TravelTracker.Attractions.Domain.Types;

namespace TravelTracker.Attractions.Domain.Models;

/**
 * Represents a place that is either private to its creator or shared in the catalogue.
 */
public sealed class Attraction
{
    /** The longest accepted attraction name. */
    public const int NameMaxLength = 200;

    /** The longest accepted description. */
    public const int DescriptionMaxLength = 2000;

    /** The longest accepted address. */
    public const int AddressMaxLength = 500;

    /**
     * Gets or sets the unique identifier of the attraction.
     */
    public EntityId<Attraction> AttractionId { get; set; }

    /**
     * Gets or sets the validated issuer of the creating account.
     */
    public string OwnerIssuer { get; set; } = string.Empty;

    /**
     * Gets or sets the validated subject of the creating account.
     */
    public string OwnerSubject { get; set; } = string.Empty;

    /**
     * Gets or sets whether this record belongs to the shared catalogue instead of one private owner.
     */
    public bool IsShared { get; set; }

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

    /**
     * Checks intrinsic attraction values independently of HTTP and storage.
     * This is the single source of truth for value rules; the API parser defers to it.
     * @return Field errors; empty when all invariants hold.
     */
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Name)) errors["name"] = ["Name must not be empty."];
        else if (Name.Trim().Length > NameMaxLength) errors["name"] = [$"Name must be at most {NameMaxLength} characters."];
        if (Address is null) errors["address"] = ["Address cannot be null."];
        else if (Address.Length > AddressMaxLength) errors["address"] = [$"Address must be at most {AddressMaxLength} characters."];
        if (Description?.Length > DescriptionMaxLength) errors["description"] = [$"Description must be at most {DescriptionMaxLength} characters."];
        if (Latitude is < -90m or > 90m) errors["lat"] = ["Latitude must be between -90 and 90."];
        if (Longitude is < -180m or > 180m) errors["lon"] = ["Longitude must be between -180 and 180."];
        if (Rating is < 0m or > 5m) errors["rating"] = ["Rating must be between 0 and 5."];
        if (EstimatedVisitMinutes < 0) errors["estimatedVisitMinutes"] = ["EstimatedVisitMinutes must be non-negative."];
        return errors;
    }
}
