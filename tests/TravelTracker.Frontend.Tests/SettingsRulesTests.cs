using TravelTracker.Frontend.Api.Settings;
using TravelTracker.Frontend.Features.Settings;

namespace TravelTracker.Frontend.Tests;

public sealed class SettingsRulesTests
{
    static SettingsDraft Valid() => SettingsDraft.From(new SettingsDto("system", 0, 0, 2, "car", "UTC"));

    static void Set(SettingsDraft draft, string field, string value)
    {
        switch (field)
        {
            case "theme": draft.Theme = value; break;
            case "defaultMapLatitude": draft.Latitude = value; break;
            case "defaultMapLongitude": draft.Longitude = value; break;
            case "defaultMapZoom": draft.Zoom = value; break;
            case "preferredTransportMode": draft.TransportMode = value; break;
            case "timeZone": draft.TimeZone = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, "unknown field");
        }
    }

    [Fact]
    public void DefaultsPassTheClientRules()
    {
        Assert.Empty(Valid().Validate());
    }

    [Theory]
    [InlineData("theme", "purple")]
    [InlineData("defaultMapLatitude", "90.1")]
    [InlineData("defaultMapLatitude", "north")]
    [InlineData("defaultMapLongitude", "-180.5")]
    [InlineData("defaultMapZoom", "21")]
    [InlineData("defaultMapZoom", "2.5")]
    [InlineData("preferredTransportMode", "boat")]
    [InlineData("timeZone", "Mars/Olympus")]
    [InlineData("timeZone", "   ")]
    public void RejectsValuesTheServiceRejectsUnderTheSameFieldName(string field, string value)
    {
        var draft = Valid();
        Set(draft, field, value);
        Assert.Contains(field, draft.Validate().Keys);
    }

    [Theory]
    [InlineData("defaultMapLatitude", "-90")]
    [InlineData("defaultMapLatitude", "90")]
    [InlineData("defaultMapLongitude", "-180")]
    [InlineData("defaultMapLongitude", "180")]
    [InlineData("defaultMapZoom", "0")]
    [InlineData("defaultMapZoom", "20")]
    public void AcceptsTheInclusiveBoundaries(string field, string value)
    {
        var draft = Valid();
        Set(draft, field, value);
        Assert.Empty(draft.Validate());
    }

    [Fact]
    public void BuildsTheContractWithInvariantNumbersAndTrimmedTimeZone()
    {
        var draft = Valid();
        draft.Latitude = "45.5";
        draft.Longitude = "-73.5";
        draft.Zoom = "5";
        draft.TransportMode = "train";
        draft.TimeZone = " America/Montreal ";
        Assert.Empty(draft.Validate());
        Assert.Equal(new SettingsDto("system", 45.5m, -73.5m, 5, "train", "America/Montreal"), draft.ToSettings());
    }

    [Fact]
    public void SavedValuesRoundTripThroughTheDraft()
    {
        var saved = new SettingsDto("dark", -12.25m, 170m, 20, "plane", "Europe/Paris");
        Assert.Equal(saved, SettingsDraft.From(saved).ToSettings());
    }

    [Theory]
    [InlineData("UTC", true)]
    [InlineData("America/Montreal", true)]
    [InlineData("", false)]
    [InlineData("Not/AZone", false)]
    public void TimeZoneCheckMatchesTheHostDatabase(string timeZone, bool expected)
    {
        Assert.Equal(expected, SettingsRules.IsKnownTimeZone(timeZone));
    }
}
