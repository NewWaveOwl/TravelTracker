using Microsoft.Extensions.Configuration;
using TravelTracker.Users.Api.Services;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** Verifies that Supabase email ownership comes from the trusted Auth user endpoint, never user metadata. */
public sealed class SupabaseIdentityTests
{
    const string Issuer = "https://project.example.test/auth/v1";
    const string Subject = "10d5c7fe-8989-4a43-b68a-9c45e9a73bd8";

    [Theory]
    [InlineData("verified", "verified@example.test")]
    [InlineData("wrong-subject", null)]
    [InlineData("unconfirmed", null)]
    [InlineData("invalid-confirmation", null)]
    [InlineData("metadata-only", null)]
    [InlineData("anonymous", null)]
    public async Task SupabaseUserVerificationBindsConfirmedEmailToTheValidatedSubject(string scenario, string? expected)
    {
        using var handler = new SupabaseIdentityHandler();
        if (scenario == "wrong-subject") handler.User["id"] = Guid.NewGuid().ToString();
        if (scenario == "unconfirmed") handler.User["email_confirmed_at"] = null;
        if (scenario == "invalid-confirmation") handler.User["email_confirmed_at"] = "not-a-confirmation-time";
        if (scenario == "metadata-only") handler.User.Remove("email");
        if (scenario == "anonymous") handler.User["is_anonymous"] = true;
        using var http = new HttpClient(handler);
        var client = new VerifiedIdentityClient(http, Settings());

        var email = await client.EmailAsync(Issuer, Subject, "test-user-token", default);

        Assert.Equal(expected, email);
        Assert.Equal(Issuer + "/user", handler.Destination?.AbsoluteUri);
        Assert.Equal("sb_publishable_test-only", handler.PublicKey);
        Assert.Equal("Bearer test-user-token", handler.Bearer);
    }

    [Theory]
    [InlineData("https://other.example.test/auth/v1", "sb_publishable_test-only")]
    [InlineData(Issuer, "")]
    public async Task UntrustedIssuerOrMissingPublicKeyNeverSendsTheBearerToken(string issuer, string key)
    {
        using var handler = new SupabaseIdentityHandler();
        using var http = new HttpClient(handler);
        var client = new VerifiedIdentityClient(http, Settings(key));

        Assert.Null(await client.EmailAsync(issuer, Subject, "test-user-token", default));
        Assert.Equal(0, handler.Calls);
    }

    /**
     * Builds isolated deployment-owned identity-provider settings.
     * @param key Test-only publishable key or a missing-key scenario.
     * @return In-memory configuration for the production identity client.
     */
    static IConfiguration Settings(string key = "sb_publishable_test-only") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Bearer:Provider"] = "Supabase",
            ["Authentication:Bearer:Authority"] = Issuer,
            ["Supabase:PublishableKey"] = key
        }).Build();
}
