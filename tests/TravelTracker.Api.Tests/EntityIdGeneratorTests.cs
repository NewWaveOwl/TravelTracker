using TravelTracker.Data;
using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Tests;

/**
 * Verifies the identity generator seeds once and never repeats an identity under concurrency.
 */
public sealed class EntityIdGeneratorTests
{
    /**
     * Issues identities from many threads at once.
     * @return Completion after checking uniqueness and a single seed read.
     */
    [Fact]
    public async Task NextAsync_ConcurrentCallsAreUniqueAndSeedOnce()
    {
        var seedReads = 0;
        var generator = new EntityIdGenerator(() => { Interlocked.Increment(ref seedReads); return Task.FromResult(41L); });
        var issued = await Task.WhenAll(Enumerable.Range(0, 1000).Select(_ => Task.Run(() => generator.NextAsync<User>().AsTask())));
        Assert.Equal(1000, issued.Distinct().Count());
        Assert.Equal(1000, issued.Select(id => id.PublicId).Distinct().Count());
        Assert.Equal(1, seedReads);
    }
}
