using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Api.References;
using TravelTracker.Api.Tests.Support;
using TravelTracker.Api.Trips;

namespace TravelTracker.Api.Tests.Trips;

/**
 * Proves that a trip is written only when validation is ready, and that saved snapshots read without any catalogue.
 */
public sealed class TripSaveServiceTests
{
    /**
     * Saves a valid draft exactly once.
     */
    [Fact]
    public async Task Save_ReadyDraft_StoresExactlyOneSnapshotTrip()
    {
        var repository = new FakeTripRepository();

        var outcome = await TripFixture.CreateSaveService(TripFixture.HealthyUpstream(), repository)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.Ready, outcome.Status);
        Assert.Same(outcome.Trip, Assert.Single(repository.Stored));
    }

    /**
     * An unavailable dependency leaves storage unchanged.
     */
    [Fact]
    public async Task Save_CatalogueUnavailable_LeavesStorageUnchanged()
    {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", TripFixture.MuseumPath, HttpStatusCode.ServiceUnavailable);
        var repository = new FakeTripRepository();

        var outcome = await TripFixture.CreateSaveService(upstream, repository)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.DependencyUnavailable, outcome.Status);
        Assert.Empty(repository.Stored);
    }

    /**
     * An absent or inaccessible reference blocks the write.
     */
    [Fact]
    public async Task Save_MissingReference_LeavesStorageUnchanged()
    {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", TripFixture.HotelPath, HttpStatusCode.NotFound);
        var repository = new FakeTripRepository();

        var outcome = await TripFixture.CreateSaveService(upstream, repository)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.ReferenceRejected, outcome.Status);
        Assert.Empty(repository.Stored);
    }

    /**
     * Users being down blocks the write even when the catalogue is healthy.
     */
    [Fact]
    public async Task Save_UsersUnavailable_LeavesStorageUnchanged()
    {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("POST", TripFixture.IdentityPath, HttpStatusCode.ServiceUnavailable);
        var repository = new FakeTripRepository();

        var outcome = await TripFixture.CreateSaveService(upstream, repository)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.DependencyUnavailable, outcome.Status);
        Assert.Empty(repository.Stored);
    }

    /**
     * The validated caller becomes the owner; another subject cannot read the stored trip.
     */
    [Fact]
    public async Task Save_RecordsValidatedOwner_AndHidesTripFromOtherSubjects()
    {
        var repository = new FakeTripRepository();
        await TripFixture.CreateSaveService(TripFixture.HealthyUpstream(), repository)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);
        var saved = Assert.Single(repository.Stored);

        var owned = await repository.GetOwnedByIdAsync(saved.TripId, TripFixture.Caller.Issuer, TripFixture.Caller.Subject);
        Assert.NotNull(owned);
        Assert.Equal(saved.TripId, owned.TripId);
        Assert.Null(await repository.GetOwnedByIdAsync(saved.TripId, TripFixture.Caller.Issuer, "auth0|someone-else"));
    }

    /**
     * A failed write is reported to the caller as an exception, not as a stored or ready trip.
     */
    [Fact]
    public async Task Save_WriteFailure_Throws_AndStoresNothing()
    {
        var repository = new FakeTripRepository { NextWriteFailure = new InvalidOperationException("simulated write failure") };
        var logs = new LogSink();

        await Assert.ThrowsAsync<InvalidOperationException>(() => TripFixture.CreateSaveService(TripFixture.HealthyUpstream(), repository, logs)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None));

        Assert.Empty(repository.Stored);
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Error
            && entry.Message.Contains("InvalidOperationException") && !entry.Message.Contains("simulated write failure"));
    }

    /**
     * A banned account is refused and nothing is written.
     */
    [Fact]
    public async Task Save_BannedAccount_LeavesStorageUnchanged()
    {
        var upstream = TripFixture.HealthyUpstream();
        upstream.Respond("GET", TripFixture.AccessPath, HttpStatusCode.OK, new { userId = TripFixture.UserId, status = "banned", role = "traveller" });
        var repository = new FakeTripRepository();

        var outcome = await TripFixture.CreateSaveService(upstream, repository)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.AccountRejected, outcome.Status);
        Assert.Empty(repository.Stored);
    }

    /**
     * An unsupported place type is refused before any catalogue lookup and nothing is written. The account standing check still runs first.
     */
    [Fact]
    public async Task Save_UnsupportedPlaceType_LeavesStorageUnchanged()
    {
        var upstream = TripFixture.HealthyUpstream();
        var draft = TripFixture.OneWayDraft();
        var badLeg = draft.Legs[0] with { From = new(TripFixture.Hotel, "hotel") };
        var repository = new FakeTripRepository();

        var outcome = await TripFixture.CreateSaveService(upstream, repository)
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, draft with { Legs = [badLeg] }, CancellationToken.None);

        Assert.Equal(TripReferenceStatus.InvalidTrip, outcome.Status);
        Assert.Empty(repository.Stored);
        Assert.DoesNotContain(upstream.Calls, call => call.Contains("/api/v1/"));
    }

    /**
     * A saved trip reads back its snapshot values through the real read service without reaching any catalogue host.
     */
    [Fact]
    public async Task SavedSnapshot_ReadsWithoutAnyCatalogueHost()
    {
        var upstream = TripFixture.HealthyUpstream();
        var repository = new FakeTripRepository();
        using var provider = TripFixture.CreateProvider(upstream, new LogSink(), repository);
        await provider.GetRequiredService<TripSaveService>()
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);
        var saved = Assert.Single(repository.Stored);
        var callsBeforeRead = upstream.Calls.Count;
        var reader = new TripReadService(repository, provider.GetRequiredService<UsersStandingClient>(), NullLogger<TripReadService>.Instance);

        var outcome = await reader.GetOwnedAsync(TripFixture.Caller, saved.TripId, CancellationToken.None);

        Assert.Equal(TripReadStatus.Found, outcome.Status);
        var leg = Assert.Single(outcome.Trip!.Legs);
        Assert.Equal("Hotel Lumiere", leg.From.Name);
        Assert.Equal("Louvre", leg.To.Name);
        Assert.Equal("Train", leg.Transport.Name);
        Assert.DoesNotContain(upstream.Calls.Skip(callsBeforeRead), call => call.Contains("/api/v1/"));
    }
}
