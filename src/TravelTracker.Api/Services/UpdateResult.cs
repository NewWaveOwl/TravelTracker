using TravelTracker.Domain.Models;
namespace TravelTracker.Api.Services;

/**
 * Describes the outcome of a partial update attempt.
 * @param Updated The updated attraction when successful; null otherwise.
 * @param Errors A dictionary of field errors when validation fails; null when successful.
 * @param NotFound True when the target attraction does not exist.
 */
public sealed record UpdateResult(Attraction? Updated, Dictionary<string, string[]>? Errors, bool NotFound = false);
