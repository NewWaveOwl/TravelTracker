using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Verifies scope metadata through the real OIDC callback and encrypted session cookie. */
public sealed class GrantedScopeTests
{
    [Theory]
    [InlineData("openid catalogue.read", "openid catalogue.read")]
    [InlineData("openid catalogue.read catalogue.write", "openid catalogue.read catalogue.write")]
    [InlineData(null, "openid profile email catalogue.read users.read")]
    [InlineData("", "")]
    public async Task SessionUsesTokenEndpointGrantInsteadOfProviderSuppliedServerClaim(string? grant, string expected)
    {
        using var factory = new FrontendFactory();
        factory.Auth0.GrantedScope = grant;
        factory.Auth0.SpoofedGrantedScope = "catalogue.write impersonated-provider-claim";
        using var browser = factory.CreateBrowser();
        var authorize = await factory.StartSignInAsync(browser);
        Assert.Equal(FrontendFactory.RequestedScope, System.Web.HttpUtility.ParseQueryString(authorize.Query)["scope"]);
        await factory.CompleteSignInAsync(browser);
        var scopes = await browser.GetStringAsync("/test/granted-scope");
        Assert.Equal(expected, scopes);
        Assert.DoesNotContain("impersonated", scopes);
    }
}
