using TravelTracker.Accommodations.Domain.Models;

namespace TravelTracker.Accommodations.Domain.Tests;

/**
 * Verifies the accommodation model's location and ownership invariants.
 */
public sealed class AccommodationTests
{
    [Fact]
    public void Validate_ValidPrivatePlaceHasNoErrorsAndDefaultsPrivate()
    {
        var accommodation = ValidAccommodation();

        Assert.False(accommodation.IsShared);
        Assert.Empty(accommodation.Validate());
    }

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    public void Validate_AcceptsCoordinateBoundaries(decimal latitude, decimal longitude)
    {
        var accommodation = ValidAccommodation();
        accommodation.Latitude = latitude;
        accommodation.Longitude = longitude;

        Assert.Empty(accommodation.Validate());
    }

    [Theory]
    [InlineData(-90.0001, 10, "lat")]
    [InlineData(90.0001, 10, "lat")]
    [InlineData(10, -180.0001, "lon")]
    [InlineData(10, 180.0001, "lon")]
    public void Validate_RejectsCoordinatesOutsideLegalRanges(decimal latitude, decimal longitude, string field)
    {
        var accommodation = ValidAccommodation();
        accommodation.Latitude = latitude;
        accommodation.Longitude = longitude;

        Assert.Contains(field, accommodation.Validate().Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsBlankName(string name)
    {
        var accommodation = ValidAccommodation();
        accommodation.Name = name;

        Assert.Contains("name", accommodation.Validate().Keys);
    }

    [Fact]
    public void Validate_RejectsMissingIdentityAndNullAddress()
    {
        var accommodation = ValidAccommodation();
        accommodation.AccommodationId = Guid.Empty;
        accommodation.OwnerIssuer = " ";
        accommodation.OwnerSubject = "";
        accommodation.Address = null!;

        var errors = accommodation.Validate();

        Assert.Contains("accommodationId", errors.Keys);
        Assert.Contains("owner", errors.Keys);
        Assert.Contains("address", errors.Keys);
    }

    /**
     * Creates a valid accommodation candidate for invariant tests.
     * @return A private accommodation with valid owner and coordinates.
     */
    private static Accommodation ValidAccommodation() => new()
    {
        AccommodationId = Guid.CreateVersion7(),
        OwnerIssuer = "https://issuer.example/",
        OwnerSubject = "traveller-1",
        Name = "Harbour Inn",
        Address = "1 Main Street",
        Latitude = 45.5m,
        Longitude = -73.5m
    };
}
