using System.Net.Http.Headers;
using TravelTracker.Accommodations.Api;
using TravelTracker.Attractions.Api;
using TravelTracker.Settings.Api;
using TravelTracker.Transport.Api;
using TravelTracker.Trips.Api;

namespace TravelTracker.Journey.Tests.Support;

/**
 * starts the Settings, Trips, Accommodations, Attractions and Transport hosts together, each on its own PostgreSQL database,
 * with one shared identity provider and a Users stand-in. nothing here touches the network or a production resource.
 */
internal sealed class JourneyEnvironment : IDisposable
{
    private const string UsersBase = "https://users.journey.test/";
    private readonly List<IDisposable> disposables = [];

    /** the shared identity provider that signs every token. */
    internal JourneyIdentity Identity { get; } = new();

    internal JourneyHost<SettingsProgram> Settings { get; }
    internal JourneyHost<TripsProgram> Trips { get; }
    internal JourneyHost<AccommodationsProgram> Accommodations { get; }
    internal JourneyHost<AttractionsProgram> Attractions { get; }
    internal JourneyHost<TransportProgram> Transport { get; }

    /** creates every host and its database; hosts start on first use. */
    internal JourneyEnvironment()
    {
        var router = new HostRouter();
        router.Route("issuer.journey.test", () => Identity);
        router.Route("users.journey.test", () => users);
        disposables.Add(Identity);

        Settings = Add(new JourneyHost<SettingsProgram>(Own("Settings", UsersAccess()), router));
        Accommodations = Add(new JourneyHost<AccommodationsProgram>(Own("Accommodations", UsersAccess()), router));
        Attractions = Add(new JourneyHost<AttractionsProgram>(Own("Attractions", UsersAccess()), router));
        Transport = Add(new JourneyHost<TransportProgram>(Own("Transport", []), router));
        Trips = Add(new JourneyHost<TripsProgram>(Own("Trips", new()
        {
            ["TripReferences:AccommodationsBaseUrl"] = "https://accommodations.journey.test/",
            ["TripReferences:AttractionsBaseUrl"] = "https://attractions.journey.test/",
            ["TripReferences:TransportBaseUrl"] = "https://transport.journey.test/",
            ["TripReferences:UsersBaseUrl"] = UsersBase,
            ["TripReferences:UsersServiceAccessToken"] = UsersService.ServiceToken,
            ["TripReferences:Timeout"] = "00:00:10"
        }), router));

        router.Route("accommodations.journey.test", () => Accommodations.Server.CreateHandler());
        router.Route("attractions.journey.test", () => Attractions.Server.CreateHandler());
        router.Route("transport.journey.test", () => Transport.Server.CreateHandler());
    }

    private readonly UsersService users = new();

    /**
     * creates a client for one host that carries a signed token for a subject.
     * @param factory the host's factory.
     * @param subject the user subject.
     * @param scopes space-separated permissions, or null to send no token.
     * @param failure an optional invalid-token case.
     * @typeparam TProgram the host's entry point type.
     * @return a client addressed to the host.
     */
    internal HttpClient ClientFor<TProgram>(JourneyHost<TProgram> factory, string subject, string? scopes, string? failure = null)
        where TProgram : class
    {
        var client = factory.CreateClient();
        if (scopes is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Identity.CreateToken(subject, scopes, failure));
        return client;
    }

    private Dictionary<string, string?> UsersAccess() => new()
    {
        ["Users:InternalBaseUrl"] = UsersBase,
        ["Users:ServiceAccessToken"] = UsersService.ServiceToken
    };

    private Dictionary<string, string?> Own(string service, Dictionary<string, string?> extra)
    {
        var database = new PostgresDatabase(service.ToLowerInvariant());
        disposables.Add(database);
        extra[$"ConnectionStrings:{service}"] = database.ConnectionString;
        return extra;
    }

    private T Add<T>(T host) where T : IDisposable
    {
        disposables.Add(host);
        return host;
    }

    /** stops every host, then drops every database. */
    public void Dispose()
    {
        for (var index = disposables.Count - 1; index >= 0; index--) disposables[index].Dispose();
    }
}
