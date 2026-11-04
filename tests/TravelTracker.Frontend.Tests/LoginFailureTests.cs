using TravelTracker.Frontend.Auth;

namespace TravelTracker.Frontend.Tests;

public sealed class LoginFailureTests
{
    [Theory]
    [InlineData("access_denied", "Client \"abc\" is not authorized to access resource server \"api\".", LoginFailure.NotAuthorized)]
    [InlineData("access_denied", "Service not found: traveltracker-api", LoginFailure.NotConfigured)]
    [InlineData("login_required", null, LoginFailure.LoginRequired)]
    [InlineData("consent_required", null, LoginFailure.ConsentRequired)]
    [InlineData("access_denied", "user denied", LoginFailure.Canceled)]
    [InlineData(null, null, LoginFailure.Canceled)]
    public void MapsProviderFailuresToFixedCodes(string? error, string? description, string expected)
    {
        Assert.Equal(expected, LoginFailure.CodeFor(error, description));
    }

    [Fact]
    public void NeverEchoesUnknownCodes()
    {
        var message = LoginFailure.MessageFor("<script>secret</script>");

        Assert.Equal("Sign-in was canceled or could not be completed. You can try again.", message);
    }
}
