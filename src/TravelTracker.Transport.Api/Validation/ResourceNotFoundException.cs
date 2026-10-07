namespace TravelTracker.Transport.Api.Validation;
/**
 * Identifies an absent reference without saving a partial route.
 * @param resource The resource value.
 */
public sealed class ResourceNotFoundException(string resource)
    : Exception($"The referenced {resource} was not found.");
