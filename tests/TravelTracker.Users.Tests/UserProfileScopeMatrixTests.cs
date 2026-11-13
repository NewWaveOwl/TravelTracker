using System.Net;
using System.Net.Http.Json;
using TravelTracker.Users.Domain.Types;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** Exercises the full bearer and scope matrix of the self-profile routes, plus the HTTPS requirement. */
public sealed class UserProfileScopeMatrixTests
{
    const string Me = "/api/v1/users/me";
    const string Subject = "traveller";

    static async Task<UsersApiFactory> Seeded()
    {
        var app = new UsersApiFactory();
        await app.Repository.GetOrCreateAsync(new ProviderIdentity(UsersApiFactory.Issuer, Subject), DateTimeOffset.UtcNow, default);
        return app;
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("openid profile email", 403)]
    [InlineData("catalogue.read catalogue.write", 403)]
    [InlineData("users.write", 403)]
    [InlineData("users.access", 403)]
    [InlineData("users.read.extra", 403)]
    [InlineData("users.read,users.write", 403)]
    [InlineData("USERS.READ", 403)]
    [InlineData("users.read", 200)]
    [InlineData("users.read users.write", 200)]
    [InlineData("openid users.read catalogue.read", 200)]
    public async Task ReadRequiresExactlyTheUsersReadScope(string? scope, int expected)
    {
        using var app = await Seeded();
        using var client = app.Browser(scope is null ? null : app.Token(subject: Subject, scope: scope));
        Assert.Equal(expected, (int)(await client.GetAsync(Me)).StatusCode);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("openid profile email", 403)]
    [InlineData("catalogue.read catalogue.write", 403)]
    [InlineData("users.read", 403)]
    [InlineData("users.access", 403)]
    [InlineData("users.write.extra", 403)]
    [InlineData("users.read,users.write", 403)]
    [InlineData("USERS.WRITE", 403)]
    [InlineData("users.write", 200)]
    [InlineData("users.read users.write", 200)]
    public async Task PatchRequiresExactlyTheUsersWriteScopeAndRejectsBeforeTouchingTheProfile(string? scope, int expected)
    {
        using var app = await Seeded();
        using var client = app.Browser(scope is null ? null : app.Token(subject: Subject, scope: scope));
        Assert.Equal(expected, (int)(await client.PatchAsJsonAsync(Me, new { address = "1 Main St" })).StatusCode);
        Assert.Equal(expected == 200 ? "1 Main St" : null, app.Repository.Account!.Address);
    }

    [Fact]
    public async Task RejectedRequestsDoNotProvisionAnAccount()
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token(subject: "stranger", scope: "users.write"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Me)).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("malformed")]
    [InlineData("unsigned")]
    [InlineData("missing-sub")]
    [InlineData("empty-sub")]
    [InlineData("no-expiry")]
    [InlineData("future")]
    public async Task ProfileRejectsInvalidBearerBeforeProvisioningAnAccount(string failure)
    {
        using var app = new UsersApiFactory();
        var token = app.Token(subject: "stranger", scope: "users.read users.write", failure: failure);
        using var client = app.Browser(token);
        using var read = await client.GetAsync(Me);
        using var patch = await client.PatchAsJsonAsync(Me, new { address = "1 Main St" });
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, patch.StatusCode);
        Assert.Contains(read.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Contains(patch.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.DoesNotContain(token, await read.Content.ReadAsStringAsync());
        Assert.DoesNotContain(token, await patch.Content.ReadAsStringAsync());
        Assert.DoesNotContain(token, string.Join(" ", read.Headers.WwwAuthenticate));
        Assert.DoesNotContain(token, string.Join(" ", patch.Headers.WwwAuthenticate));
        Assert.Null(app.Repository.Account);
        Assert.DoesNotContain(app.Logs.Entries, entry => entry.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProfileRoutesRefuseCleartextHttp()
    {
        using var app = await Seeded();
        using var client = app.Browser(app.Token(subject: Subject, scope: "users.read users.write"), https: false);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Me)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(Me, new { address = "1 Main St" })).StatusCode);
        Assert.Null(app.Repository.Account!.Address);
    }
}
