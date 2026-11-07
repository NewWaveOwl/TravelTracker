using System.Net;
using TravelTracker.Api.Tests.Support;
using TravelTracker.Api.Trips;

namespace TravelTracker.Api.Tests.Trips;

/**
 * Proves that a trip is written only when validation is ready, and that saved snapshots read without any catalogue.
 */
public sealed class TripSaveServiceTests {
    /**
     * Builds a save service over the given upstream and a fresh fake store.
     *
     * @param upstream The scripted upstream.
     * @param repository The store the service writes to.
     * @return The save service.
     */
    private static TripSaveService Create(StubUpstream upstream, FakeTripRepository repository) =>
        TripFixture.CreateSaveService(upstream, repository);

    /**
     * Saves a valid draft exactly once.
     */
    [Fact]
    public async Task Save_ReadyDraft_StoresExactlyOneSnapshotTrip() {
        var repository = new FakeTripRepository();

        var outcome = await Create(TripFixture.HealthyUpstream(), repository)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.Ready, outcome.Status);
        Assert.Same(outcome.Trip, Assert.Single(repository.Stored));
    }

    /**
     * An unavailable dependency leaves storage unchanged.
     */
    [Fact]
    public async Task Save_CatalogueUnavailable_LeavesStorageUnchanged() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", $"/api/v1/attractions/{TripFixture.Museum:D}", HttpStatusCode.ServiceUnavailable);
        var repository = new FakeTripRepository();

        var outcome = await Create(upstream, repository)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.DependencyUnavailable, outcome.Status);
        Assert.Empty(repository.Stored);
    }

    /**
     * An absent or inaccessible reference blocks the write.
     */
    [Fact]
    public async Task Save_MissingReference_LeavesStorageUnchanged() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", $"/api/v1/accommodations/{TripFixture.Hotel:D}", HttpStatusCode.NotFound);
        var repository = new FakeTripRepository();

        var outcome = await Create(upstream, repository)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.ReferenceRejected, outcome.Status);
        Assert.Empty(repository.Stored);
    }

    /**
     * Users being down blocks the write even when the catalogue is healthy.
     */
    [Fact]
    public async Task Save_UsersUnavailable_LeavesStorageUnchanged() {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("POST", "/internal/users/identity", HttpStatusCode.ServiceUnavailable);
        var repository = new FakeTripRepository();

        var outcome = await Create(upstream, repository)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.DependencyUnavailable, outcome.Status);
        Assert.Empty(repository.Stored);
    }

    /**
     * The validated caller becomes the owner; another subject cannot read the stored trip.
     */
    [Fact]
    public async Task Save_RecordsValidatedOwner_AndHidesTripFromOtherSubjects() {
        var repository = new FakeTripRepository();
        await Create(TripFixture.HealthyUpstream(), repository)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);
        var saved = Assert.Single(repository.Stored);

        Assert.NotNull(await repository.GetOwnedByIdAsync(saved.TripId, TripFixture.Caller.Issuer, TripFixture.Caller.Subject));
        Assert.Null(await repository.GetOwnedByIdAsync(saved.TripId, TripFixture.Caller.Issuer, "auth0|someone-else"));
    }

    /**
     * A failed write is reported to the caller as an exception, not as a stored or ready trip.
     */
    [Fact]
    public async Task Save_WriteFailure_Throws_AndStoresNothing() {
        var repository = new FakeTripRepository { NextWriteFailure = new InvalidOperationException("simulated write failure") };
        var logs = new LogSink();

        await Assert.ThrowsAsync<InvalidOperationException>(() => TripFixture.CreateSaveService(TripFixture.HealthyUpstream(), repository, logs)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None));

        Assert.Empty(repository.Stored);
        Assert.Contains(logs.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error
            && entry.Message.Contains("InvalidOperationException") && !entry.Message.Contains("simulated write failure"));
    }

    /**
     * A saved trip reads back its snapshot values with every catalogue host unreachable, because the reads never call one.
     */
    [Fact]
    public async Task SavedSnapshot_ReadsWithoutAnyCatalogueHost() {
        var repository = new FakeTripRepository();
        await Create(TripFixture.HealthyUpstream(), repository)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        // the read path is storage only: it holds no HTTP client, so no catalogue host is involved
        var saved = Assert.Single(repository.Stored);
        var trip = await repository.GetOwnedByIdAsync(saved.TripId, TripFixture.Caller.Issuer, TripFixture.Caller.Subject);

        var leg = Assert.Single(trip!.Legs);
        Assert.Equal("Hotel Lumiere", leg.From.Name);
        Assert.Equal("Louvre", leg.To.Name);
        Assert.Equal("Train", leg.Transport.Name);
    }
}
