using System.ComponentModel.DataAnnotations;
using TravelTracker.Frontend.Features.Users;

namespace TravelTracker.Frontend.Tests;

public sealed class PasswordChangeModelTests
{
    static List<string> Errors(PasswordChangeModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results.Select(result => result.ErrorMessage!).ToList();
    }

    [Fact]
    public void EmptyFormNamesEveryRequiredField()
    {
        var errors = Errors(new PasswordChangeModel());

        Assert.Contains("Enter your current password.", errors);
        Assert.Contains("Enter a new password.", errors);
        Assert.Contains("Confirm the new password.", errors);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void NewPasswordFollowsTheSixToOneHundredTwentyEightRule(int length, bool accepted)
    {
        var password = new string('x', length);
        var model = new PasswordChangeModel { CurrentPassword = "current", NewPassword = password, ConfirmPassword = password };

        Assert.Equal(accepted, Errors(model).Count == 0);
    }

    [Fact]
    public void ConfirmationMustMatchTheNewPassword()
    {
        var model = new PasswordChangeModel { CurrentPassword = "current", NewPassword = "new secret", ConfirmPassword = "new secre" };

        Assert.Contains("The new password and its confirmation do not match.", Errors(model));
    }
}
