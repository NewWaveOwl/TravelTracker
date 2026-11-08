using System.ComponentModel.DataAnnotations;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Features.Users;

namespace TravelTracker.Frontend.Tests;

public sealed class ProfileEditModelTests
{
    [Fact]
    public void MissingInitialUsernameAndEmailStayOptionalWhenAddressChanges()
    {
        var model = new ProfileEditModel(new SelfUserProfile(Guid.NewGuid(), "traveller", "active", null, null));
        model.Address = "1 Main Street";

        Assert.Empty(Validate(model));
        var body = new UpdateSelfProfileRequest(model.Username, model.Email, model.Address)
            .ChangedFields(new SelfUserProfile(Guid.NewGuid(), "traveller", "active", null, null));
        Assert.Equal(["address"], body.Keys);
    }

    [Fact]
    public void ExistingUsernameAndEmailCannotBeCleared()
    {
        var model = new ProfileEditModel(new SelfUserProfile(Guid.NewGuid(), "traveller", "active", "traveller", "traveller@example.test"));
        model.Username = " ";
        model.Email = " ";

        var errors = Validate(model);
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(model.Username)));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(model.Email)));
    }

    [Fact]
    public void InvalidUsernameProducesFieldAssociatedError()
    {
        var model = new ProfileEditModel(new SelfUserProfile(Guid.NewGuid(), "traveller", "active", "traveller", "traveller@example.test"));
        model.Username = "abc!";

        var errors = Validate(model);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(model.Username))
            && error.ErrorMessage!.StartsWith("Use only letters", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateRequestPreservesOmittedValuesAndClearsOnlyAddressExplicitly()
    {
        var original = new SelfUserProfile(Guid.NewGuid(), "traveller", "active", "traveller", "traveller@example.test", "1 Main St");

        var fields = new UpdateSelfProfileRequest("traveller", "traveller@example.test", " ").ChangedFields(original);

        Assert.Single(fields);
        Assert.True(fields.ContainsKey("address"));
        Assert.Null(fields["address"]);
    }

    static List<ValidationResult> Validate(ProfileEditModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
