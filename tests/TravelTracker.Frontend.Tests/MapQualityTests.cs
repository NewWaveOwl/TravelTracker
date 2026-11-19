using TravelTracker.Frontend.Features.Trips;

namespace TravelTracker.Frontend.Tests;

/** automatic map quality rules over the capabilities a browser reports; a pure decision with no rendering. */
public sealed class MapQualityTests
{
    static MapCapabilities Desktop => new(WebGl: true, DeviceMemoryGigabytes: 16, LogicalCores: 12, CoarsePointer: false);

    static MapCapabilities Phone => new(WebGl: true, DeviceMemoryGigabytes: 6, LogicalCores: 8, CoarsePointer: true);

    [Fact]
    public void AutomaticUsesStandardOnNormalDesktop()
    {
        Assert.Equal(MapQuality.Standard, MapQuality.Resolve(MapQuality.Automatic, Desktop));
    }

    [Fact]
    public void AutomaticUsesLowOnTouchPhoneEvenWithGoodSpecs()
    {
        Assert.Equal(MapQuality.Low, MapQuality.Resolve(MapQuality.Automatic, Phone));
    }

    [Fact]
    public void AutomaticUsesLowWhenWebGlIsUnavailable()
    {
        Assert.Equal(MapQuality.Low, MapQuality.Resolve(MapQuality.Automatic, Desktop with { WebGl = false }));
    }

    [Theory]
    [InlineData(2, 12)]
    [InlineData(16, 2)]
    public void AutomaticUsesLowForLimitedMemoryOrCores(double memory, int cores)
    {
        var capabilities = Desktop with { DeviceMemoryGigabytes = memory, LogicalCores = cores };

        Assert.Equal(MapQuality.Low, MapQuality.Resolve(MapQuality.Automatic, capabilities));
    }

    [Fact]
    public void AutomaticDoesNotTreatUnreportedValuesAsLimited()
    {
        var capabilities = new MapCapabilities(WebGl: true, DeviceMemoryGigabytes: null, LogicalCores: null, CoarsePointer: false);

        Assert.Equal(MapQuality.Standard, MapQuality.Resolve(MapQuality.Automatic, capabilities));
    }

    [Theory]
    [InlineData(MapQuality.Standard)]
    [InlineData(MapQuality.Low)]
    public void SavedChoiceOverridesAutomaticOnEveryDevice(string choice)
    {
        Assert.Equal(choice, MapQuality.Resolve(choice, Phone));
        Assert.Equal(choice, MapQuality.Resolve(choice, Desktop));
    }

    [Theory]
    [InlineData("ultra")]
    [InlineData("")]
    public void UnknownSavedValueFallsBackToAutomatic(string saved)
    {
        Assert.Equal(MapQuality.Standard, MapQuality.Resolve(saved, Desktop));
        Assert.Equal(MapQuality.Low, MapQuality.Resolve(saved, Phone));
    }
}
