using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** verifies the one-time first-administrator bootstrap over HTTP. */
public sealed class BootstrapApiTests
{
    const string Bootstrap = "/api/v1/users/bootstrap-first-administrator";
    static string DefaultTravellerScopes()
    {
        var hook = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "supabase-auth-hook.sql"));
        var declaration = System.Text.RegularExpressions.Regex.Match(hook, @"traveller_scopes text\[\] := ARRAY\[([^\]]+)\]");
        Assert.True(declaration.Success);
        var scopes = System.Text.RegularExpressions.Regex.Matches(declaration.Groups[1].Value, @"'([^']+)'")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Contains("catalogue.read", scopes);
        Assert.DoesNotContain("users.admin", scopes);
        return string.Join(" ", scopes);
    }

    static Task<HttpResponseMessage> Provision(HttpClient client, string? secret = UsersApiFactory.Secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Bootstrap) { Content = JsonContent.Create(new { }) };
        if (secret is not null) request.Headers.Add("X-Bootstrap-Secret", secret);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task HealthIsAnonymousAndBootstrapRequiresRealAuthentication()
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Provision(client)).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HealthRemainsPublicWithMissingOrInvalidBearer(bool invalidToken)
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(invalidToken ? "invalid" : null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    public async Task InvalidBearerIsRejectedBeforeCredentialLookupOrPersistence(string failure)
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token(failure));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Provision(client)).StatusCode);
        Assert.Equal(0, app.Credentials.Calls);
        Assert.Equal(0, app.Repository.BootstrapCalls);
    }

    [Fact]
    public async Task MissingCatalogueReadScopeCannotBootstrap()
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token(scope: "users.read users.write"));
        Assert.Equal(HttpStatusCode.Forbidden, (await Provision(client)).StatusCode);
        Assert.Equal(0, app.Repository.BootstrapCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-secret")]
    public async Task InvalidOperatorCredentialNeverReadsCredentialsOrDatabase(string? secret)
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token());
        Assert.Equal(HttpStatusCode.Forbidden, (await Provision(client, secret)).StatusCode);
        Assert.Equal(0, app.Credentials.Calls);
        Assert.Equal(0, app.Repository.BootstrapCalls);
    }

    [Fact]
    public async Task BootstrapIsDisabledWithoutStrongOperatorConfiguration()
    {
        using var app = new UsersApiFactory { ConfiguredSecret = "short" };
        using var client = app.Browser(app.Token());
        Assert.Equal(HttpStatusCode.NotFound, (await Provision(client)).StatusCode);
        Assert.Equal(0, app.Repository.BootstrapCalls);
    }

    [Fact]
    public async Task BootstrapRequiresTheConfiguredLoginToBelongToTheTokenSubject()
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token(subject: "different-subject"));
        Assert.Equal(HttpStatusCode.Forbidden, (await Provision(client)).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Fact]
    public async Task BootstrapRefusesWhenNoAccountIsRegisteredUnderTheConfiguredEmail()
    {
        using var app = new UsersApiFactory();
        app.Credentials.Unregister();
        using var client = app.Browser(app.Token());
        Assert.Equal(HttpStatusCode.Forbidden, (await Provision(client)).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Fact]
    public async Task UnavailableCredentialStoreNeverCreatesAdministratorOrLeaksDiagnostics()
    {
        using var app = new UsersApiFactory();
        app.Credentials.Unavailable = true;
        using var client = app.Browser(app.Token());
        var response = await Provision(client);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("credential-store-secret", await response.Content.ReadAsStringAsync());
        Assert.Null(app.Repository.Account);
    }

    [Fact]
    public async Task RegisteredTargetBootstrapsAndReadsRoleWithoutExposingProviderIdentifiers()
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token());
        var response = await Provision(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>();
        Assert.Equal("administrator", profile!.Role);
        Assert.Equal("active", profile.Status);
        var read = await client.GetFromJsonAsync<SelfProfileResponse>("/api/v1/users/me");
        Assert.Equal(profile.UserId, read!.PublicId);
        Assert.Equal(profile.Role, read.Role);
        Assert.Null(read.Email);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("subject", json);
        Assert.DoesNotContain("issuer", json);
        Assert.DoesNotContain(UsersApiFactory.Secret, json);
        Assert.Equal(profile, await (await Provision(client)).Content.ReadFromJsonAsync<UserProfileResponse>());
    }

    [Fact]
    public async Task OtherIdentityCannotClaimCompletedBootstrap()
    {
        using var app = new UsersApiFactory();
        using var first = app.Browser(app.Token());
        await Provision(first);
        using var other = app.Browser(app.Token(subject: "other-subject"));
        // the configured login belongs to the first identity, so the second never reaches the consumed marker
        Assert.Equal(HttpStatusCode.Forbidden, (await Provision(other)).StatusCode);
        Assert.Equal("admin-subject", app.Repository.Account!.ProviderIdentity.Subject);
    }

    [Fact]
    public async Task DeactivatedAccountCannotReadItsProfile()
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token());
        Assert.Equal(HttpStatusCode.OK, (await Provision(client)).StatusCode);
        app.Repository.Deactivate();
        var response = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(UsersApiFactory.Email, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BootstrapRequiresHttps()
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token(), false);
        Assert.Equal(HttpStatusCode.BadRequest, (await Provision(client)).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Fact]
    public async Task LiveAccessLookupRequiresSeparateServiceCredentialAndDoesNotProvisionUnknownUsers()
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token());
        var request = new { issuer = UsersApiFactory.Issuer, subject = "admin-subject" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/users/access", request)).StatusCode);
        Assert.Equal(0, app.Repository.ReadCalls);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UsersApiFactory.ServiceToken);
        var response = await client.PostAsJsonAsync("/api/v1/users/access", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var access = await response.Content.ReadFromJsonAsync<AccessResponse>();
        Assert.Equal(new AccessResponse("deactivated", "traveller"), access);
        Assert.Null(app.Repository.Account);
    }
}
