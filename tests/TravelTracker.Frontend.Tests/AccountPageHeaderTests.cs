using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** proves the account page, with and without a session, is never cached and never leaks its url as a referrer. */
public sealed class AccountPageHeaderTests
{
    [Theory]
    [InlineData("/account", false)]
    [InlineData("/account/", false)]
    [InlineData("/account", true)]
    [InlineData("/account/", true)]
    public async Task AccountResponsesAreNonCacheableWithoutReferrer(string path, bool signedIn)
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        if (signedIn) await factory.SignInAsync(browser);

        using var response = await browser.GetAsync(path);

        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }
}
