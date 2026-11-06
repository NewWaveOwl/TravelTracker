using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelTracker.Api.Trips;
using TravelTracker.Data.Repositories;

namespace TravelTracker.Api.Tests.Support;

/**
 * Shared ids, credentials and a healthy upstream script for the trip reference tests. The services are
 * built through the same AddTripReferences registration the host will use, with only the HTTP handler replaced.
 */
public static class TripFixture {
    /** Public accommodation the draft starts from. */
    public static readonly Guid Hotel = Guid.Parse("0196a1b2-0000-7000-8000-000000000001");

    /** Public attraction the draft ends at. */
    public static readonly Guid Museum = Guid.Parse("0196a1b2-0000-7000-8000-000000000002");

    /** Public transport stop used by optional legs. */
    public static readonly Guid Stop = Guid.Parse("0196a1b2-0000-7000-8000-000000000003");

    /** Public transport mode used by the draft legs. */
    public static readonly Guid Train = Guid.Parse("0196a1b2-0000-7000-8000-000000000004");

    /** Public account identifier that the sign-in resolves to. */
    public static readonly Guid UserId = Guid.Parse("0196a1b2-0000-7000-8000-0000000000aa");

    /** Caller's access token; forwarded to catalogue services only. */
    public const string CallerToken = "caller-token";

    /** Backend credential with users.access; sent to Users only. */
    public const string ServiceToken = "service-token";

    /** Caller identity from a validated token. */
    public static CallerIdentity Caller => new("https://issuer.test/", "auth0|traveller");

    /** Start of the trip in Europe/Paris summer time. */
    public static readonly DateTimeOffset Start = new(2026, 6, 1, 8, 0, 0, TimeSpan.FromHours(2));

    /**
     * Builds the service provider exactly as the host will: logging, the TripReferences section, typed clients and
     * the trip services. Only the primary HTTP handler is replaced, so every upstream call goes to the stub.
     *
     * @param upstream The scripted upstream handler.
     * @param logs Receives every log line written during the test.
     * @param repository The store the save service writes to.
     * @param overrides Configuration values that replace the healthy defaults.
     * @return A provider that validates its registrations on build.
     */
    public static ServiceProvider CreateProvider(StubUpstream upstream, LogSink logs, FakeTripRepository? repository = null,
        IDictionary<string, string?>? overrides = null) {
        var values = new Dictionary<string, string?> {
            ["TripReferences:AccommodationsBaseUrl"] = "https://upstream.test/",
            ["TripReferences:AttractionsBaseUrl"] = "https://upstream.test/",
            ["TripReferences:TransportBaseUrl"] = "https://upstream.test/",
            ["TripReferences:UsersBaseUrl"] = "https://upstream.test/",
            ["TripReferences:UsersServiceAccessToken"] = ServiceToken
        };
        foreach (var pair in overrides ?? new Dictionary<string, string?>()) values[pair.Key] = pair.Value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(new CapturingLoggerProvider(logs)));
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => upstream));
        services.AddSingleton<ITripRepository>(repository ?? new FakeTripRepository());
        services.AddTripReferences(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /**
     * Builds a validator wired through the DI registration.
     *
     * @param upstream The scripted upstream handler.
     * @param logs Receives log lines; a discarded sink is used when omitted.
     * @param overrides Configuration values that replace the healthy defaults.
     * @return The validator.
     */
    public static TripReferenceValidator CreateValidator(StubUpstream upstream, LogSink? logs = null, IDictionary<string, string?>? overrides = null) =>
        CreateProvider(upstream, logs ?? new LogSink(), overrides: overrides).GetRequiredService<TripReferenceValidator>();

    /**
     * Builds the save service wired through the DI registration, writing to the given store.
     *
     * @param upstream The scripted upstream handler.
     * @param repository The store the save service writes to.
     * @param logs Receives log lines; a discarded sink is used when omitted.
     * @return The save service.
     */
    public static TripSaveService CreateSaveService(StubUpstream upstream, FakeTripRepository repository, LogSink? logs = null) =>
        CreateProvider(upstream, logs ?? new LogSink(), repository).GetRequiredService<TripSaveService>();

    /**
     * Scripts every upstream call the healthy path needs: an active traveller, and every referenced record visible to them.
     *
     * @return The scripted upstream.
     */
    public static StubUpstream HealthyUpstream() {
        var upstream = new StubUpstream();
        upstream.Respond("POST", "/internal/users/identity", HttpStatusCode.OK, new { userId = UserId });
        upstream.Respond("GET", $"/internal/users/{UserId:D}/access", HttpStatusCode.OK, new { userId = UserId, status = "active", role = "traveller" });
        upstream.Respond("GET", $"/api/v1/accommodations/{Hotel:D}", HttpStatusCode.OK,
            new { accommodationId = Hotel, name = "Hotel Lumiere", address = "1 rue de Rivoli", lat = 48.85m, lon = 2.35m, visibility = "shared" });
        upstream.Respond("GET", $"/api/v1/attractions/{Museum:D}", HttpStatusCode.OK,
            new { attractionId = Museum, name = "Louvre", address = "Rue de Rivoli", lat = 48.86m, lon = 2.34m, estimatedVisitMinutes = 120 });
        upstream.Respond("GET", $"/api/v1/route-points/{Stop:D}", HttpStatusCode.OK,
            new { routePointId = Stop, type = "transport_stop", name = "Gare du Nord", address = "18 rue de Dunkerque", lat = 48.88m, lon = 2.35m });
        upstream.Respond("GET", $"/api/v1/transport-modes/{Train:D}", HttpStatusCode.OK,
            new { transportModeId = Train, code = "train", name = "Train" });
        return upstream;
    }

    /**
     * Builds a valid one-way draft: the hotel to the museum by train.
     *
     * @return The draft.
     */
    public static TripDraft OneWayDraft() => new("Paris weekend", TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"), Start, Start.AddHours(4), false,
        [new(1, new(Hotel, "accommodation"), new(Museum, "attraction"), Train, Start.AddHours(1), Start.AddHours(2))]);
}
