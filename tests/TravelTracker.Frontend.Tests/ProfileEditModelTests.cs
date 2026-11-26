using System.ComponentModel.DataAnnotations;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Features.Users;

namespace TravelTracker.Frontend.Tests;

public sealed class ProfileEditModelTests
{
    [Fact]
    public void MissingInitialAddressStaysOptionalAndIsOmittedUntilItChanges()
    {
        var original = new SelfUserProfile(Guid.Parse("00000000-0000-0000-0000-000000009001"), "traveller", "active", "traveller");
        var model = new ProfileEditModel(original);

        Assert.False(model.HasChanges);
        model.Address = "1 Main Street";

        Assert.Empty(Validate(model));
        Assert.True(model.HasChanges);
        Assert.Equal(["address"], new UpdateSelfProfileRequest(model.Address).ChangedFields(original).Keys);
    }

    [Fact]
    public void OverlongAddressProducesFieldAssociatedError()
    {
        var model = new ProfileEditModel(new SelfUserProfile(Guid.Parse("00000000-0000-0000-0000-000000009002"), "traveller", "active", "traveller"))
        {
            Address = new string('x', 501)
        };

        Assert.Contains(Validate(model), error => error.MemberNames.Contains(nameof(model.Address)));
    }

    [Fact]
    public void UpdateRequestClearsOnlyTheAddressExplicitly()
    {
        var original = new SelfUserProfile(Guid.Parse("00000000-0000-0000-0000-000000009003"), "traveller", "active", "traveller", "1 Main St");

        var fields = new UpdateSelfProfileRequest(" ").ChangedFields(original);

        Assert.Single(fields);
        Assert.True(fields.ContainsKey("address"));
        Assert.Null(fields["address"]);
    }

    [Fact]
    public void AnUnchangedAddressSendsNothing()
    {
        var original = new SelfUserProfile(Guid.Parse("00000000-0000-0000-0000-000000009004"), "traveller", "active", "traveller", "1 Main St");

        Assert.Empty(new UpdateSelfProfileRequest(" 1 Main St ").ChangedFields(original));
    }

    static List<ValidationResult> Validate(ProfileEditModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
