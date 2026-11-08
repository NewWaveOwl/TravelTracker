namespace TravelTracker.Api.References;

/**
 * Deployment addresses, the backend Users credential and the per-call timeout for the trip reference clients.
 * Bound from the TripReferences configuration section and validated at startup. Never commit the credential;
 * supply it through the environment or user secrets.
 */
public sealed class TripReferenceOptions {
    /** Configuration section these options bind from. */
    public const string SectionName = "TripReferences";

    /** Base address of the Accommodations service, http or https. */
    public string? AccommodationsBaseUrl { get; set; }

    /** Base address of the Attractions service, http or https. */
    public string? AttractionsBaseUrl { get; set; }

    /** Base address of the Transport service, http or https. */
    public string? TransportBaseUrl { get; set; }

    /** Base address of the Users service. Must be HTTPS because Users refuses plain HTTP lookups. */
    public string? UsersBaseUrl { get; set; }

    /** Backend access token holding users.access. Sent to Users only, never to catalogue hosts. */
    public string? UsersServiceAccessToken { get; set; }

    /** Upper bound for one upstream call. Must be greater than zero and at most 60 seconds. */
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
