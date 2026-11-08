using TravelTracker.Settings.Domain.Models;

namespace TravelTracker.Settings.Domain.Tests;

/**
 * verifies the documented defaults and every preference validation rule.
 */
public sealed class UserSettingsTests
{
    [Fact]
    public void Defaults_AreDocumentedAndValid()
    {
        var settings = new UserSettings();

        Assert.Equal("system", settings.Theme);
        Assert.Equal(0m, settings.DefaultMapLatitude);
        Assert.Equal(0m, settings.DefaultMapLongitude);
        Assert.Equal(2, settings.DefaultMapZoom);
        Assert.Equal("car", settings.PreferredTransportMode);
        Assert.Equal("UTC", settings.TimeZone);
        Assert.Empty(settings.Validate());
    }

    [Theory]
    [InlineData("system")]
    [InlineData("light")]
    [InlineData("dark")]
    public void Validate_AcceptsEverySupportedTheme(string theme)
    {
        var settings = new UserSettings { Theme = theme };

        Assert.Empty(settings.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Dark")]
    [InlineData("dark ")]
    [InlineData("neon")]
    public void Validate_RejectsUnsupportedThemeWithFieldError(string theme)
    {
        var settings = new UserSettings { Theme = theme };

        Assert.Contains("theme", settings.Validate().Keys);
    }

    [Theory]
    [InlineData(-90, -180, 0)]
    [InlineData(90, 180, 20)]
    [InlineData(45.5, -73.5, 10)]
    public void Validate_AcceptsCoordinateAndZoomBoundaries(decimal latitude, decimal longitude, int zoom)
    {
        var settings = new UserSettings
        {
            DefaultMapLatitude = latitude,
            DefaultMapLongitude = longitude,
            DefaultMapZoom = zoom
        };

        Assert.Empty(settings.Validate());
    }

    [Theory]
    [InlineData(-90.0001, 0, "defaultMapLatitude")]
    [InlineData(90.0001, 0, "defaultMapLatitude")]
    [InlineData(0, -180.0001, "defaultMapLongitude")]
    [InlineData(0, 180.0001, "defaultMapLongitude")]
    public void Validate_RejectsCoordinatesOutsideLegalRanges(decimal latitude, decimal longitude, string field)
    {
        var settings = new UserSettings { DefaultMapLatitude = latitude, DefaultMapLongitude = longitude };

        Assert.Contains(field, settings.Validate().Keys);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void Validate_RejectsZoomOutsideRange(int zoom)
    {
        var settings = new UserSettings { DefaultMapZoom = zoom };

        Assert.Contains("defaultMapZoom", settings.Validate().Keys);
    }

    [Theory]
    [InlineData("car")]
    [InlineData("bus")]
    [InlineData("train")]
    [InlineData("pedestrian")]
    [InlineData("bike")]
    [InlineData("plane")]
    public void Validate_AcceptsEverySupportedTransportMode(string mode)
    {
        var settings = new UserSettings { PreferredTransportMode = mode };

        Assert.Empty(settings.Validate());
    }

    [Theory]
    [InlineData("boat")]
    [InlineData("Car")]
    [InlineData("")]
    public void Validate_RejectsUnsupportedTransportMode(string mode)
    {
        var settings = new UserSettings { PreferredTransportMode = mode };

        Assert.Contains("preferredTransportMode", settings.Validate().Keys);
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("America/Montreal")]
    [InlineData("Europe/Paris")]
    public void Validate_AcceptsKnownTimeZones(string timeZone)
    {
        var settings = new UserSettings { TimeZone = timeZone };

        Assert.Empty(settings.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData(" UTC")]
    public void Validate_RejectsUnknownOrBlankTimeZone(string timeZone)
    {
        var settings = new UserSettings { TimeZone = timeZone };

        Assert.Contains("timeZone", settings.Validate().Keys);
    }

    [Fact]
    public void Validate_ReportsEveryInvalidFieldTogether()
    {
        var settings = new UserSettings
        {
            Theme = "neon",
            DefaultMapLatitude = 91,
            DefaultMapLongitude = 181,
            DefaultMapZoom = 99,
            PreferredTransportMode = "boat",
            TimeZone = "Nowhere/Nowhere"
        };

        var errors = settings.Validate();

        Assert.Equal(["defaultMapLatitude", "defaultMapLongitude", "defaultMapZoom", "preferredTransportMode", "theme", "timeZone"],
            errors.Keys.Order(StringComparer.Ordinal));
    }
}
