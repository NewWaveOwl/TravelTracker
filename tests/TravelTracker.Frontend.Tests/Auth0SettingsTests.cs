using Microsoft.Extensions.Configuration;
using TravelTracker.Frontend.Auth;

namespace TravelTracker.Frontend.Tests;

public sealed class Auth0SettingsTests
{
    static IConfiguration ConfigurationOf(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>($"Auth0:{pair.Key}", pair.Value))).Build();

    [Fact]
    public void ReadsCompleteSettingsAndDefaultsTheScope()
    {
        var settings = Auth0Settings.TryRead(ConfigurationOf(("Domain", "tenant.eu.auth0.com"), ("ClientId", "client"), ("Audience", "traveltracker-api")));

        Assert.Equal("https://tenant.eu.auth0.com/", settings!.Authority);
        Assert.Equal(Auth0Settings.DefaultScope, settings.Scope);
        Assert.Null(settings.ClientSecret);
    }

    [Theory]
    [InlineData(null, "client", "api")]
    [InlineData("tenant", "client", "api")]
    [InlineData("tenant..auth0.com", "client", "api")]
    [InlineData("https://tenant.auth0.com", "client", "api")]
    [InlineData("tenant.auth0.com/path", "client", "api")]
    [InlineData("tenant.auth0.com", "", "api")]
    [InlineData("tenant.auth0.com", "client", "")]
    public void StaysUnconfiguredForMissingOrMalformedSettings(string? domain, string clientId, string audience)
    {
        Assert.Null(Auth0Settings.TryRead(ConfigurationOf(("Domain", domain), ("ClientId", clientId), ("Audience", audience))));
    }
}
