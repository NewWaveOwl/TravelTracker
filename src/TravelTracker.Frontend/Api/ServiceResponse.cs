namespace TravelTracker.Frontend.Api;

/**
 * A parsed public response together with the entity tag the service attached to it.
 *
 * @param Body The parsed JSON body, or null when the service sent none.
 * @param ETag The raw strong entity tag, such as "3", or null when the service sent none.
 */
public sealed record ServiceResponse<T>(T? Body, string? ETag);
