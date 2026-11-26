using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** verifies the granted scopes in the session come from the issued token response and nothing else. */
public sealed class GrantedScopeTests
{
    [Theory]
    [InlineData("catalogue.read", "catalogue.read")]
    [InlineData("catalogue.read trips.write", "catalogue.read trips.write")]
    [InlineData("", "")]
    public async Task SessionKeepsTheScopeTheUsersApiGranted(string grant, string expected)
    {
        using var factory = new FrontendFactory();
        factory.Users.Scope = grant;
        using var browser = factory.CreateBrowser();
        await factory.SignInAsync(browser);
        Assert.Equal(expected, await browser.GetStringAsync("/test/granted-scope"));
    }
}
