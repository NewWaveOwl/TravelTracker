using TravelTracker.Frontend.Features.Trips;

namespace TravelTracker.Frontend.Tests;

/** Validates routing-zone compatibility and explicit daylight-saving choices independently of the host zone. */
public sealed class RouteLocalTimeTests
{
    [Fact]
    public void WindowsTimeZoneNormalizesToIanaWithoutChangingItsOffsetOrSeconds()
    {
        var wireZone = RouteLocalTime.RouterZone("Eastern Standard Time");
        Assert.NotNull(wireZone);
        Assert.DoesNotContain("Standard Time", wireZone);
        var local = new DateTime(2026, 11, 14, 10, 0, 5);
        Assert.Equal(DateTimeOffset.Parse("2026-11-14T10:00:05-05:00"), RouteLocalTime.Resolve(local, "Eastern Standard Time", null));
    }

    [Fact]
    public void IanaZoneWithoutASlashStillWorks()
    {
        Assert.True(TimeZoneInfo.TryFindSystemTimeZoneById("GMT", out _));
        Assert.NotNull(RouteLocalTime.RouterZone("GMT"));
        Assert.Equal(TimeSpan.Zero, RouteLocalTime.Resolve(new(2026, 11, 14, 10, 0, 5), "GMT", null)!.Value.Offset);
    }

    [Fact]
    public void RepeatedLocalTimeRequiresAnExplicitValidOffset()
    {
        var repeated = new DateTime(2026, 11, 1, 1, 30, 17);
        Assert.Equal(2, RouteLocalTime.Offsets(repeated, "America/Toronto").Length);
        Assert.Null(RouteLocalTime.Resolve(repeated, "America/Toronto", null));
        Assert.Null(RouteLocalTime.Resolve(repeated, "America/Toronto", TimeSpan.Zero));
        var first = RouteLocalTime.Resolve(repeated, "America/Toronto", TimeSpan.FromHours(-4))!.Value;
        var second = RouteLocalTime.Resolve(repeated, "America/Toronto", TimeSpan.FromHours(-5))!.Value;
        Assert.Equal(TimeSpan.FromHours(1), second - first);
        Assert.Equal(17, first.Second);
    }

    [Fact]
    public void DaylightSavingGapAndUnknownZoneNeverGuessADeparture()
    {
        var gap = new DateTime(2026, 3, 8, 2, 30, 0);
        Assert.Empty(RouteLocalTime.Offsets(gap, "America/Toronto"));
        Assert.Null(RouteLocalTime.Resolve(gap, "America/Toronto", null));
        Assert.Null(RouteLocalTime.RouterZone("Not/AZone"));
    }
}
