namespace TravelTracker.Frontend.Api.Routing;

/** The global union of loaded service dates; null bounds and dates remain unknown. */
public sealed record RoutingCoverage(string? TimeZone = null, DateOnly? StartDate = null, DateOnly? EndDate = null,
    IReadOnlyList<RoutingFeed>? Feeds = null, RoutingBounds? Bounds = null,
    DateTimeOffset? ServiceStart = null, DateTimeOffset? ServiceEndExclusive = null, string? ServiceRangeBasis = null);
