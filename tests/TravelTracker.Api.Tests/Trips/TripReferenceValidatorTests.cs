using System.Net;
using TravelTracker.Api.Tests.Support;
using TravelTracker.Api.Trips;
using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Api.Tests.Trips;

/**
 * Proves the reference checks over stubbed HTTP: live standing, visibility, dependency failures, credentials and snapshot values.
 */
public sealed class TripReferenceValidatorTests {
    private static readonly string HotelPath = $"/api/v1/accommodations/{TripFixture.Hotel:D}";
    private static readonly string MuseumPath = $"/api/v1/attractions/{TripFixture.Museum:D}";
    private static readonly string StopPath = $"/api/v1/route-points/{TripFixture.Stop:D}";
    private static readonly string TrainPath = $"/api/v1/transport-modes/{TripFixture.Train:D}";
    private static readonly string IdentityPath = "/internal/users/identity";
    private static readonly string AccessPath = $"/internal/users/{TripFixture.UserId:D}/access";

    /**
     * Runs the validator for the standard one-way draft and caller.
     *
     * @param upstream The scripted upstream.
     * @param draft The draft to check.
     * @return The validation outcome.
     */
    private static Task<TripReferenceOutcome> ValidateAsync(StubUpstream upstream, TripDraft? draft = null) =>
        TripFixture.CreateValidator(upstream).ValidateAsync(TripFixture.Caller, TripFixture.CallerToken, draft ?? TripFixture.OneWayDraft(), CancellationToken.None);

    /**
     * Returns the catalogue calls, which are the ones that must carry the caller's token.
     *
     * @param upstream The scripted upstream.
     * @return Calls to the catalogue hosts.
     */
    private static IEnumerable<string> CatalogueCalls(StubUpstream upstream) => upstream.Calls.Where(call => call.Contains("/api/v1/"));

    /**
     * Valid references produce the snapshot values the owning services returned.
     */
    [Fact]
    public async Task Validate_ValidReferences_ProducesSnapshotValues() {
        var outcome = await ValidateAsync(TripFixture.HealthyUpstream());

        Assert.Equal(TripReferenceStatus.Ready, outcome.Status);
        Assert.Empty(outcome.Errors);
        var leg = Assert.Single(outcome.Trip!.Legs);
        Assert.Equal(new TripPlace(TripFixture.Hotel, "accommodation", "Hotel Lumiere", "1 rue de Rivoli", 48.85m, 2.35m), leg.From);
        Assert.Equal(new TripPlace(TripFixture.Museum, "attraction", "Louvre", "Rue de Rivoli", 48.86m, 2.34m), leg.To);
        Assert.Equal(new TripTransport(TripFixture.Train, "train", "Train"), leg.Transport);
    }

    /**
     * The caller's token reaches the catalogue hosts only, and the backend credential reaches Users only.
     */
    [Fact]
    public async Task Validate_SendsCallerTokenToCatalogueAndServiceTokenToUsers() {
        var upstream = TripFixture.HealthyUpstream();
        await ValidateAsync(upstream);

        Assert.NotEmpty(CatalogueCalls(upstream));
        Assert.All(CatalogueCalls(upstream), call => Assert.Equal("Bearer caller-token", upstream.Credentials[call]));
        Assert.Equal(2, upstream.Calls.Count(call => call.Contains("/internal/")));
        Assert.All(upstream.Calls.Where(call => call.Contains("/internal/")), call => Assert.Equal("Bearer service-token", upstream.Credentials[call]));
    }

    /**
     * A place that is absent or private to someone else blocks the save and names the field.
     *
     * @param status The status the catalogue returns for the place.
     */
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Validate_MissingOrInaccessiblePlace_RejectsWithFieldAndNoTrip(HttpStatusCode status) {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", MuseumPath, status);

        var outcome = await ValidateAsync(upstream);

        Assert.Equal(TripReferenceStatus.ReferenceRejected, outcome.Status);
        Assert.Null(outcome.Trip);
        var error = Assert.Single(outcome.Errors);
        Assert.Equal("legs[0].to", error.Field);
    }

    /**
     * A transport mode that is absent blocks the save and names the transport field.
     */
    [Fact]
    public async Task Validate_MissingTransportMode_RejectsWithTransportField() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", TrainPath, HttpStatusCode.NotFound);

        var outcome = await ValidateAsync(upstream);

        Assert.Equal(TripReferenceStatus.ReferenceRejected, outcome.Status);
        Assert.Equal("legs[0].transport", Assert.Single(outcome.Errors).Field);
    }

    /**
     * A transport stop reference must name a record that Transport reports as a transport stop.
     */
    [Fact]
    public async Task Validate_TransportStopThatIsNotAStop_RejectsWithFromField() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", StopPath, HttpStatusCode.OK, new { routePointId = TripFixture.Stop, type = "attraction", name = "x", address = "y", lat = 1m, lon = 2m });
        var draft = new TripDraft("stop", TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"), TripFixture.Start, TripFixture.Start.AddHours(4), false,
            [new(1, new(TripFixture.Stop, "transport_stop"), new(TripFixture.Museum, "attraction"), TripFixture.Train, TripFixture.Start.AddHours(1), TripFixture.Start.AddHours(2))]);

        var outcome = await ValidateAsync(upstream, draft);

        Assert.Equal(TripReferenceStatus.ReferenceRejected, outcome.Status);
        Assert.Equal("legs[0].from", Assert.Single(outcome.Errors).Field);
    }

    /**
     * Banned and deactivated accounts cannot save, and no catalogue is read for them.
     *
     * @param status The account standing Users reports.
     */
    [Theory]
    [InlineData("banned")]
    [InlineData("deactivated")]
    public async Task Validate_BannedOrDeactivatedAccount_RejectsBeforeAnyCatalogueCall(string status) {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", AccessPath, HttpStatusCode.OK, new { userId = TripFixture.UserId, status, role = "traveller" });

        var outcome = await ValidateAsync(upstream);

        Assert.Equal(TripReferenceStatus.AccountRejected, outcome.Status);
        Assert.Empty(CatalogueCalls(upstream));
    }

    /**
     * A sign-in with no matching account cannot save, and no catalogue is read for it.
     */
    [Fact]
    public async Task Validate_UnknownAccount_RejectsBeforeAnyCatalogueCall() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("POST", IdentityPath, HttpStatusCode.NotFound);

        var outcome = await ValidateAsync(upstream);

        Assert.Equal(TripReferenceStatus.AccountRejected, outcome.Status);
        Assert.Empty(CatalogueCalls(upstream));
    }

    /**
     * When Users cannot confirm standing, the save fails closed without reading the catalogue.
     *
     * @param status The failure Users returns on the identity lookup.
     */
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Validate_UsersAnswersWithFailure_FailsClosedWithoutCatalogueCall(HttpStatusCode status) {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("POST", IdentityPath, status);

        var outcome = await ValidateAsync(upstream);

        Assert.Equal(TripReferenceStatus.DependencyUnavailable, outcome.Status);
        Assert.Null(outcome.Trip);
        Assert.Empty(CatalogueCalls(upstream));
    }

    /**
     * A Users timeout fails closed without reading the catalogue.
     */
    [Fact]
    public async Task Validate_UsersTimesOut_FailsClosedWithoutCatalogueCall() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Fail("POST", IdentityPath, new TaskCanceledException("timed out"));

        var outcome = await ValidateAsync(upstream);

        Assert.Equal(TripReferenceStatus.DependencyUnavailable, outcome.Status);
        Assert.Empty(CatalogueCalls(upstream));
    }

    /**
     * An identity answer that disagrees with the access answer is not trusted.
     */
    [Fact]
    public async Task Validate_AccessAnswersForAnotherAccount_FailsClosed() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", AccessPath, HttpStatusCode.OK, new { userId = Guid.NewGuid(), status = "active", role = "traveller" });

        var outcome = await ValidateAsync(upstream);

        Assert.Equal(TripReferenceStatus.DependencyUnavailable, outcome.Status);
    }

    /**
     * A catalogue outage blocks the save with no trip, so nothing partial can be stored.
     */
    [Fact]
    public async Task Validate_CatalogueUnavailable_BlocksWithoutTrip() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", MuseumPath, HttpStatusCode.ServiceUnavailable);

        var outcome = await ValidateAsync(upstream);

        Assert.Equal(TripReferenceStatus.DependencyUnavailable, outcome.Status);
        Assert.Null(outcome.Trip);
    }

    /**
     * A catalogue timeout blocks the save as an unavailable dependency.
     */
    [Fact]
    public async Task Validate_CatalogueTimesOut_BlocksAsDependencyUnavailable() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Fail("GET", HotelPath, new TaskCanceledException("timed out"));

        var outcome = await ValidateAsync(upstream);

        Assert.Equal(TripReferenceStatus.DependencyUnavailable, outcome.Status);
    }

    /**
     * A catalogue that rejects the caller's token reports an unauthorized outcome.
     */
    [Fact]
    public async Task Validate_CatalogueRejectsCallerToken_ReportsUnauthorized() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", HotelPath, HttpStatusCode.Unauthorized);

        var outcome = await ValidateAsync(upstream);

        Assert.Equal(TripReferenceStatus.Unauthorized, outcome.Status);
    }

    /**
     * Cancelling the caller's request stops the check instead of being reported as an outage.
     */
    [Fact]
    public async Task Validate_CallerCancels_PropagatesCancellation() {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TripFixture.CreateValidator(TripFixture.HealthyUpstream()).ValidateAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), cancellation.Token));
    }

    /**
     * A missing caller identity or token is rejected before any outbound call.
     */
    [Fact]
    public async Task Validate_IncompleteCaller_RejectsBeforeAnyOutboundCall() {
        var upstream = TripFixture.HealthyUpstream();

        var outcome = await TripFixture.CreateValidator(upstream).ValidateAsync(new("", "sub"), TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.Unauthorized, outcome.Status);
        Assert.Empty(upstream.Calls);
    }

    /**
     * An unsupported place type is an invalid trip and is reported before any outbound call.
     */
    [Fact]
    public async Task Validate_UnsupportedPlaceType_IsInvalidWithoutOutboundCall() {
        var upstream = TripFixture.HealthyUpstream();
        var draft = new TripDraft("bad", TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"), TripFixture.Start, TripFixture.Start.AddHours(4), false,
            [new(1, new(TripFixture.Hotel, "hostel"), new(TripFixture.Museum, "attraction"), TripFixture.Train, TripFixture.Start.AddHours(1), TripFixture.Start.AddHours(2))]);

        var outcome = await ValidateAsync(upstream, draft);

        Assert.Equal(TripReferenceStatus.InvalidTrip, outcome.Status);
        Assert.Equal("legs[0].from", Assert.Single(outcome.Errors).Field);
        Assert.Empty(upstream.Calls);
    }

    /**
     * Every reference can be real and the itinerary can still break a trip rule, which is reported against its field.
     */
    [Fact]
    public async Task Validate_ValidReferencesButBrokenItinerary_ReportsTripRuleWithoutTrip() {
        var draft = new TripDraft("loop", TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"), TripFixture.Start, TripFixture.Start.AddHours(4), false,
            [new(1, new(TripFixture.Hotel, "accommodation"), new(TripFixture.Hotel, "accommodation"), TripFixture.Train, TripFixture.Start.AddHours(1), TripFixture.Start.AddHours(2))]);

        var outcome = await ValidateAsync(TripFixture.HealthyUpstream(), draft);

        Assert.Equal(TripReferenceStatus.InvalidTrip, outcome.Status);
        Assert.Null(outcome.Trip);
        Assert.Contains(outcome.Errors, error => error.Field == "legs[0].to");
    }
}
