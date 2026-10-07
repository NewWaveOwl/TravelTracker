using TravelTracker.Attractions.Api.Models;
namespace TravelTracker.Attractions.Api.Contracts;

/**
 * Maps service-owned records to their public representation.
 */
public static class AttractionResponseMapper
{
    /**
     * Maps an immutable attraction without exposing storage.
     * @param value The saved attraction.
     * @return Its public representation.
     */
    public static AttractionResponse From(AttractionRecord value) => new(
        value.AttractionId, value.Name, value.Description, value.Rating,
        value.Address, value.Latitude, value.Longitude, value.EstimatedVisitMinutes);
}
