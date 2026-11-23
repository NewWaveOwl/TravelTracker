namespace TravelTracker.Frontend.Api.Routing;

/** A loaded transit feed with only the service dates actually supplied by the router. */
public sealed record RoutingFeed(string Id, IReadOnlyList<string> Modes, DateOnly? StartDate = null, DateOnly? EndDate = null,
    string? Version = null, string? Publisher = null);
