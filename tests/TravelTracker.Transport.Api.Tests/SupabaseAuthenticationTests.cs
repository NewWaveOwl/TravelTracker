using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TravelTracker.Api.Authentication;

namespace TravelTracker.Transport.Api.Tests;

/** Verifies Supabase signature, identity, issuer, audience, expiry and permission boundaries. */
public sealed class SupabaseAuthenticationTests
{
    [Theory]
    [InlineData("valid", 200)]
    [InlineData("signature", 401)]
    [InlineData("issuer", 401)]
    [InlineData("audience", 401)]
    [InlineData("expired", 401)]
    [InlineData("subject", 401)]
    [InlineData("role-missing", 401)]
    [InlineData("service-role", 401)]
    [InlineData("anonymous", 401)]
    [InlineData("scope", 403)]
    [InlineData("metadata-scope", 403)]
    public async Task SupabaseTokensPassThroughRealValidationAndScopePolicies(string scenario, int status)
    {
        using var host = new SupabaseBearerHost();
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(scenario));

        using var response = await host.Client.GetAsync("/private");

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.NotEmpty(host.RequestedUrls);
        Assert.All(host.RequestedUrls, url => Assert.Equal(SupabaseBearerHost.Issuer + "/.well-known/jwks.json", url));
    }

    [Theory]
    [InlineData("Authority", "https://other.example.test/auth/v1")]
    [InlineData("Issuer", "https://project.example.test/auth/v1/")]
    [InlineData("Issuer", "https://project.example.test/other")]
    [InlineData("Provider", "Unrecognized")]
    public void SupabaseConfigurationRejectsMismatchedOrUnexpectedIssuers(string setting, string value)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Authentication:Bearer:Provider"] = "Supabase",
            ["Authentication:Bearer:Authority"] = SupabaseBearerHost.Issuer,
            ["Authentication:Bearer:Issuer"] = SupabaseBearerHost.Issuer,
            ["Authentication:Bearer:Audience"] = "authenticated"
        };
        settings["Authentication:Bearer:" + setting] = value;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiBearerAuthentication(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BearerSettings>>().Value);
    }
}
