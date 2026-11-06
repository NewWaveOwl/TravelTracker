using System.Net;
using Microsoft.Extensions.Logging;
using TravelTracker.Api.Tests.Support;
using TravelTracker.Api.Trips;

namespace TravelTracker.Api.Tests.Trips;

/**
 * Proves that every failure and fail-closed decision surfaces in the logs with its detail, and that tokens never do.
 */
public sealed class TripReferenceLoggingTests {
    private static readonly string HotelPath = $"/api/v1/accommodations/{TripFixture.Hotel:D}";
    private static readonly string MuseumPath = $"/api/v1/attractions/{TripFixture.Museum:D}";
    private static readonly string IdentityPath = "/internal/users/identity";
    private static readonly string AccessPath = $"/internal/users/{TripFixture.UserId:D}/access";

    /**
     * Reports whether a line with the given level, category and text was logged.
     *
     * @param logs The captured log lines.
     * @param level The expected severity.
     * @param categorySuffix The end of the logger category, such as ReferenceSender.
     * @param fragment Text the message must contain.
     * @return True when a matching line exists.
     */
    private static bool Logged(LogSink logs, LogLevel level, string categorySuffix, string fragment) =>
        logs.Entries.Any(entry => entry.Level == level && entry.Category.EndsWith(categorySuffix) && entry.Message.Contains(fragment));

    /**
     * Asserts that neither credential appears in any captured line.
     *
     * @param logs The captured log lines.
     */
    private static void AssertNoCredentialLogged(LogSink logs) =>
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(TripFixture.CallerToken) || entry.Message.Contains(TripFixture.ServiceToken)
            || entry.Message.Contains("Bearer"));

    /**
     * A catalogue timeout is logged with the configured limit, and the check fails closed with the timeout named.
     */
    [Fact]
    public async Task Timeout_IsLoggedWithConfiguredLimit() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Stall("GET", HotelPath);
        var logs = new LogSink();

        var outcome = await TripFixture.CreateValidator(upstream, logs, new Dictionary<string, string?> { ["TripReferences:Timeout"] = "00:00:00.300" })
            .ValidateAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.DependencyUnavailable, outcome.Status);
        Assert.True(Logged(logs, LogLevel.Warning, "ReferenceSender", "timed out after 0.3 s"));
        Assert.True(Logged(logs, LogLevel.Warning, "TripReferenceValidator", "failed closed at legs[0].from"));
        AssertNoCredentialLogged(logs);
    }

    /**
     * A non-success status from a catalogue is logged with its code and the blocked field.
     */
    [Fact]
    public async Task CatalogueServerError_IsLoggedWithStatusCode() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", MuseumPath, HttpStatusCode.ServiceUnavailable);
        var logs = new LogSink();

        await TripFixture.CreateValidator(upstream, logs).ValidateAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.True(Logged(logs, LogLevel.Warning, "ReferenceSender", "HTTP 503"));
        Assert.True(Logged(logs, LogLevel.Warning, "TripReferenceValidator", "failed closed at legs[0].to"));
        AssertNoCredentialLogged(logs);
    }

    /**
     * An outage in Users is logged, and the save is refused without reading any catalogue.
     */
    [Fact]
    public async Task UsersOutage_IsLoggedAndFailsClosed() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("POST", IdentityPath, HttpStatusCode.ServiceUnavailable);
        var logs = new LogSink();

        var outcome = await TripFixture.CreateValidator(upstream, logs).ValidateAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.DependencyUnavailable, outcome.Status);
        Assert.True(Logged(logs, LogLevel.Warning, "ReferenceSender", "users identity lookup failed"));
        Assert.True(Logged(logs, LogLevel.Warning, "TripReferenceValidator", "account standing could not be confirmed"));
        AssertNoCredentialLogged(logs);
    }

    /**
     * Users rejecting the backend credential is logged as an upstream rejection, never with the credential itself.
     */
    [Fact]
    public async Task UsersRejectsBackendCredential_IsLoggedWithoutTheCredential() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("POST", IdentityPath, HttpStatusCode.Unauthorized);
        var logs = new LogSink();

        await TripFixture.CreateValidator(upstream, logs).ValidateAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.True(Logged(logs, LogLevel.Warning, "ReferenceSender", "was rejected: upstream answered HTTP 401"));
        AssertNoCredentialLogged(logs);
    }

    /**
     * A catalogue that rejects the caller's token is logged as a rejection, without the token.
     */
    [Fact]
    public async Task CatalogueRejectsCallerToken_IsLoggedWithoutTheToken() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", HotelPath, HttpStatusCode.Unauthorized);
        var logs = new LogSink();

        var outcome = await TripFixture.CreateValidator(upstream, logs).ValidateAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.Unauthorized, outcome.Status);
        Assert.True(Logged(logs, LogLevel.Warning, "ReferenceSender", "was rejected: upstream answered HTTP 401"));
        Assert.True(Logged(logs, LogLevel.Warning, "TripReferenceValidator", "rejected the caller's credential"));
        AssertNoCredentialLogged(logs);
    }

    /**
     * An absent or private reference is logged at information level with its field, and the refusal is logged once more.
     */
    [Fact]
    public async Task AbsentReference_IsLoggedWithItsField() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", MuseumPath, HttpStatusCode.NotFound);
        var logs = new LogSink();

        var outcome = await TripFixture.CreateValidator(upstream, logs).ValidateAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.ReferenceRejected, outcome.Status);
        Assert.True(Logged(logs, LogLevel.Information, "ReferenceSender", "found no visible record: upstream answered HTTP 404"));
        Assert.True(Logged(logs, LogLevel.Warning, "TripReferenceValidator", "trip check refused: legs[0].to absent or not available"));
    }

    /**
     * A banned account is refused and the refusal names the standing.
     */
    [Fact]
    public async Task BannedAccount_IsLoggedAsRefusal() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", AccessPath, HttpStatusCode.OK, new { userId = TripFixture.UserId, status = "banned", role = "traveller" });
        var logs = new LogSink();

        var outcome = await TripFixture.CreateValidator(upstream, logs).ValidateAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.AccountRejected, outcome.Status);
        Assert.True(Logged(logs, LogLevel.Warning, "TripReferenceValidator", "account standing is banned"));
    }

    /**
     * A successful save logs that the trip was stored, and a refused save logs that nothing was written.
     */
    [Fact]
    public async Task SaveOutcome_IsLoggedForStoredAndSkippedTrips() {
        var logs = new LogSink();
        var stored = TripFixture.CreateSaveService(TripFixture.HealthyUpstream(), new FakeTripRepository(), logs);
        await stored.SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);
        Assert.True(Logged(logs, LogLevel.Information, "TripSaveService", "trip stored with 1 snapshot leg(s)"));

        var refused = new StubUpstream();
        refused.Respond("POST", IdentityPath, HttpStatusCode.ServiceUnavailable);
        var skippedLogs = new LogSink();
        await TripFixture.CreateSaveService(refused, new FakeTripRepository(), skippedLogs)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);
        Assert.True(Logged(skippedLogs, LogLevel.Warning, "TripSaveService", "trip not stored: validation ended as DependencyUnavailable; nothing was written"));
        AssertNoCredentialLogged(skippedLogs);
    }
}
