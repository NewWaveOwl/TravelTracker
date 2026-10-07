using TravelTracker.Attractions.Api.Data;
using TravelTracker.Attractions.Api.Validation;
namespace TravelTracker.Attractions.Api.Tests;
/**
 * Verifies the compound store boundary independently of HTTP binding.
 */
public sealed class AttractionStoreTests
{
    [Fact]
    public void Update_ValidationExceptionLeavesOriginalSnapshotIntact()
    {
        var store = new InMemoryAttractionStore();
        var before = store.Snapshot()[0];
        Assert.Throws<InputValidationException>(() => store.Update(before.AttractionId, current =>
        {
            var candidate = current with { Name = "New", Rating = 6 };
            AttractionValidator.Validate(candidate);
            return candidate;
        }));
        Assert.Equal(before, store.Get(before.AttractionId));
    }

    [Fact]
    public void Update_DoesNotMutateEarlierReadSnapshots()
    {
        var store = new InMemoryAttractionStore();
        var before = store.Snapshot()[0];
        var saved = store.Update(before.AttractionId, current => current with { Name = "New" });
        Assert.NotEqual(before.Name, saved!.Name);
        Assert.Equal("New", store.Get(before.AttractionId)!.Name);
        Assert.Equal("Montreal Museum", before.Name);
    }
}
