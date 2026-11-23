using System.Collections.Concurrent;
using System.Security.Cryptography;
using TravelTracker.Trips.Data.TicketImports;

namespace TravelTracker.Trips.Data.Tests;

/** Proves the temporary PDF store's owner boundaries, retention and atomic capacity limits without PostgreSQL. */
public sealed class TicketImportStoreTests
{
    private const string Issuer = "https://issuer.example.test/";
    private static readonly Guid TripId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly byte[] Content = "%PDF-1.7\nsynthetic test document"u8.ToArray();

    [Fact]
    public void AddAndGet_ReturnIndependentContentAndStableMetadata()
    {
        var clock = new ManualTimeProvider();
        using var store = new MemoryTicketImportStore(clock);
        var source = Content.ToArray();
        var added = Assert.IsType<TicketImportDocument>(store.Add(Issuer, "alice", TripId, "ticket.pdf", source));
        source[0] = 0;
        added.Content[1] = 0;

        var firstRead = Assert.IsType<TicketImportDocument>(store.Get(Issuer, "alice", TripId, added.ImportId));
        firstRead.Content[2] = 0;
        var secondRead = Assert.IsType<TicketImportDocument>(store.Get(Issuer, "alice", TripId, added.ImportId));

        Assert.NotEqual(Guid.Empty, added.ImportId);
        Assert.Equal(TripId, secondRead.TripId);
        Assert.Equal("ticket.pdf", secondRead.FileName);
        Assert.Equal(Content, secondRead.Content);
        Assert.Equal(clock.GetUtcNow() + MemoryTicketImportStore.Lifetime, secondRead.ExpiresAt);
        Assert.NotSame(firstRead.Content, secondRead.Content);
    }

    [Theory]
    [InlineData("https://issuer.example.test/", "bob", "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb")]
    [InlineData("https://other-issuer.example.test/", "alice", "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb")]
    [InlineData("https://issuer.example.test/", "Alice", "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb")]
    [InlineData("https://issuer.example.test/", "alice", "cccccccc-cccc-4ccc-8ccc-cccccccccccc")]
    public void WrongOwnerOrTrip_CannotReadOrRemove(string issuer, string subject, string tripId)
    {
        using var store = new MemoryTicketImportStore();
        var added = Assert.IsType<TicketImportDocument>(store.Add(Issuer, "alice", TripId, "ticket.pdf", Content));

        Assert.Null(store.Get(issuer, subject, Guid.Parse(tripId), added.ImportId));
        Assert.False(store.Remove(issuer, subject, Guid.Parse(tripId), added.ImportId));
        Assert.NotNull(store.Get(Issuer, "alice", TripId, added.ImportId));
    }

    [Fact]
    public void UnknownIdentifier_LooksAbsent()
    {
        using var store = new MemoryTicketImportStore();

        Assert.Null(store.Get(Issuer, "alice", TripId, Guid.NewGuid()));
        Assert.False(store.Remove(Issuer, "alice", TripId, Guid.NewGuid()));
    }

    [Fact]
    public void Reading_DoesNotExtendAbsoluteExpiry()
    {
        var clock = new ManualTimeProvider();
        using var store = new MemoryTicketImportStore(clock);
        var added = Assert.IsType<TicketImportDocument>(store.Add(Issuer, "alice", TripId, "ticket.pdf", Content));

        clock.Advance(MemoryTicketImportStore.Lifetime - TimeSpan.FromSeconds(1));
        var beforeExpiry = Assert.IsType<TicketImportDocument>(store.Get(Issuer, "alice", TripId, added.ImportId));
        Assert.Equal(added.ExpiresAt, beforeExpiry.ExpiresAt);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Null(store.Get(Issuer, "alice", TripId, added.ImportId));
        Assert.False(store.Remove(Issuer, "alice", TripId, added.ImportId));
    }

    [Fact]
    public void LaterDocument_RetainsItsOwnExpiryWhenEarlierDocumentExpires()
    {
        var clock = new ManualTimeProvider();
        using var store = new MemoryTicketImportStore(clock);
        var first = Assert.IsType<TicketImportDocument>(store.Add(Issuer, "alice", TripId, "first.pdf", Content));
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = Assert.IsType<TicketImportDocument>(store.Add(Issuer, "alice", TripId, "second.pdf", Content));

        clock.Advance(TimeSpan.FromMinutes(14));

        Assert.Null(store.Get(Issuer, "alice", TripId, first.ImportId));
        Assert.NotNull(store.Get(Issuer, "alice", TripId, second.ImportId));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(store.Get(Issuer, "alice", TripId, second.ImportId));
    }

    [Fact]
    public void Remove_MakesDocumentAbsentAndReleasesOwnerCapacity()
    {
        using var store = new MemoryTicketImportStore();
        var documents = Enumerable.Range(0, MemoryTicketImportStore.MaxDocumentsPerOwner)
            .Select(_ => Assert.IsType<TicketImportDocument>(store.Add(Issuer, "alice", TripId, "ticket.pdf", Content)))
            .ToArray();
        Assert.Null(store.Add(Issuer, "alice", TripId, "overflow.pdf", Content));

        Assert.True(store.Remove(Issuer, "alice", TripId, documents[0].ImportId));

        Assert.Null(store.Get(Issuer, "alice", TripId, documents[0].ImportId));
        Assert.False(store.Remove(Issuer, "alice", TripId, documents[0].ImportId));
        Assert.NotNull(store.Add(Issuer, "alice", TripId, "replacement.pdf", Content));
        Assert.Equal(Content, documents[0].Content);
    }

    [Fact]
    public void OwnerQuota_IsSharedAcrossTripsButSeparateAcrossValidatedIssuers()
    {
        using var store = new MemoryTicketImportStore();
        for (var index = 0; index < MemoryTicketImportStore.MaxDocumentsPerOwner; index++)
            Assert.NotNull(store.Add(Issuer, "alice", Guid.NewGuid(), "ticket.pdf", Content));

        Assert.Null(store.Add(Issuer, "alice", TripId, "overflow.pdf", Content));
        Assert.NotNull(store.Add("https://other-issuer.example.test/", "alice", TripId, "different-issuer.pdf", Content));
        Assert.NotNull(store.Add(Issuer, "bob", TripId, "different-subject.pdf", Content));
    }

    [Fact]
    public void Expiry_ReleasesOwnerCapacity()
    {
        var clock = new ManualTimeProvider();
        using var store = new MemoryTicketImportStore(clock);
        for (var index = 0; index < MemoryTicketImportStore.MaxDocumentsPerOwner; index++)
            Assert.NotNull(store.Add(Issuer, "alice", TripId, "ticket.pdf", Content));
        Assert.Null(store.Add(Issuer, "alice", TripId, "overflow.pdf", Content));

        clock.Advance(MemoryTicketImportStore.Lifetime);

        Assert.NotNull(store.Add(Issuer, "alice", TripId, "new.pdf", Content));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(MemoryTicketImportStore.MaxFileBytes + 1)]
    public void InvalidContentLength_IsRejectedWithoutUsingCapacity(int length)
    {
        using var store = new MemoryTicketImportStore();

        Assert.Null(store.Add(Issuer, "alice", TripId, "invalid.pdf", new byte[length]));

        for (var index = 0; index < MemoryTicketImportStore.MaxDocumentsPerOwner; index++)
            Assert.NotNull(store.Add(Issuer, "alice", TripId, "ticket.pdf", Content));
    }

    [Fact]
    public void ExactMaximumFileSize_IsAccepted()
    {
        using var store = new MemoryTicketImportStore();

        var added = Assert.IsType<TicketImportDocument>(store.Add(Issuer, "alice", TripId, "ticket.pdf", new byte[MemoryTicketImportStore.MaxFileBytes]));

        Assert.Equal(MemoryTicketImportStore.MaxFileBytes, added.Content.Length);
    }

    [Fact]
    public void ConcurrentAdds_RespectGlobalByteCapacityAndRemovalReleasesIt()
    {
        using var store = new MemoryTicketImportStore();
        var content = new byte[MemoryTicketImportStore.MaxFileBytes];
        var accepted = new ConcurrentBag<(string Subject, Guid ImportId)>();

        Parallel.For(0, 20, index =>
        {
            var subject = $"synthetic-owner-{index}";
            var document = store.Add(Issuer, subject, TripId, "ticket.pdf", content);
            if (document is null) return;
            accepted.Add((subject, document.ImportId));
            CryptographicOperations.ZeroMemory(document.Content);
        });

        Assert.Equal(MemoryTicketImportStore.MaxTotalBytes / MemoryTicketImportStore.MaxFileBytes, accepted.Count);
        Assert.Null(store.Add(Issuer, "additional-owner", TripId, "overflow.pdf", Content));
        var removed = accepted.First();
        Assert.True(store.Remove(Issuer, removed.Subject, TripId, removed.ImportId));
        Assert.NotNull(store.Add(Issuer, "replacement-owner", TripId, "replacement.pdf", content));
    }

    [Fact]
    public void ConcurrentAdds_RespectPerOwnerCapacity()
    {
        using var store = new MemoryTicketImportStore();
        var accepted = new ConcurrentBag<Guid>();

        Parallel.For(0, 30, _ =>
        {
            var document = store.Add(Issuer, "alice", Guid.NewGuid(), "ticket.pdf", Content);
            if (document is not null) accepted.Add(document.ImportId);
        });

        Assert.Equal(MemoryTicketImportStore.MaxDocumentsPerOwner, accepted.Count);
        Assert.Equal(accepted.Count, accepted.Distinct().Count());
    }

    [Fact]
    public void SmallDocumentsAcrossOwners_ReachGlobalCountLimitAndRemovalReleasesCapacity()
    {
        using var store = new MemoryTicketImportStore();
        var first = Assert.IsType<TicketImportDocument>(store.Add(Issuer, "owner-0", TripId, "ticket.pdf", Content));
        for (var index = 1; index < MemoryTicketImportStore.MaxDocuments; index++)
            Assert.NotNull(store.Add(Issuer, $"owner-{index}", TripId, "ticket.pdf", Content));

        Assert.Null(store.Add(Issuer, "extra-owner", TripId, "overflow.pdf", Content));
        Assert.True(store.Remove(Issuer, "owner-0", TripId, first.ImportId));
        Assert.NotNull(store.Add(Issuer, "extra-owner", TripId, "replacement.pdf", Content));
        Assert.Null(store.Add(Issuer, "another-owner", TripId, "overflow.pdf", Content));
    }

    [Fact]
    public void NewStore_DoesNotRecoverTemporaryDocuments()
    {
        using var original = new MemoryTicketImportStore();
        var document = Assert.IsType<TicketImportDocument>(original.Add(Issuer, "alice", TripId, "ticket.pdf", Content));
        using var restarted = new MemoryTicketImportStore();

        Assert.Null(restarted.Get(Issuer, "alice", TripId, document.ImportId));
    }

    [Fact]
    public void Dispose_IsRepeatableAndPreventsSubsequentUse()
    {
        var store = new MemoryTicketImportStore();
        var document = Assert.IsType<TicketImportDocument>(store.Add(Issuer, "alice", TripId, "ticket.pdf", Content));

        store.Dispose();
        store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => store.Get(Issuer, "alice", TripId, document.ImportId));
        Assert.Throws<ObjectDisposedException>(() => store.Remove(Issuer, "alice", TripId, document.ImportId));
        Assert.Throws<ObjectDisposedException>(() => store.Add(Issuer, "alice", TripId, "ticket.pdf", Content));
        Assert.Equal(Content, document.Content);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset utcNow = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => utcNow;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        internal void Advance(TimeSpan duration)
        {
            var target = utcNow.Add(duration);
            while (timers.Where(timer => timer.DueAt <= target).OrderBy(timer => timer.DueAt).FirstOrDefault() is { } timer)
            {
                utcNow = timer.DueAt!.Value;
                timer.Fire();
            }
            utcNow = target;
        }

        private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
        {
            internal DateTimeOffset? DueAt { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetUtcNow().Add(dueTime);
                return true;
            }

            internal void Fire()
            {
                DueAt = null;
                callback(state);
            }

            public void Dispose() => DueAt = null;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
