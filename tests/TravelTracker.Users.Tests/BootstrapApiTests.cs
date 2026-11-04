using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

public sealed class BootstrapApiTests
{
    const string Bootstrap = "/api/v1/users/bootstrap-first-administrator";

    static Task<HttpResponseMessage> Provision(HttpClient client, string? secret = UsersApiFactory.Secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Bootstrap) { Content = JsonContent.Create(new { }) };
        if (secret is not null) request.Headers.Add("X-Bootstrap-Secret", secret);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task HealthIsAnonymousAndBootstrapRequiresRealAuthentication()
    {
        using var app = new UsersApiFactory(); using var client = app.Browser();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Provision(client)).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    public async Task InvalidBearerIsRejectedBeforeProviderOrPersistence(string failure)
    {
        using var app = new UsersApiFactory(); using var client = app.Browser(app.Token(failure));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Provision(client)).StatusCode);
        Assert.Equal(0, app.Identity.Calls); Assert.Equal(0, app.Repository.BootstrapCalls);
    }

    [Fact]
    public async Task MissingCatalogueScopeCannotBootstrap()
    {
        using var app = new UsersApiFactory(); using var client = app.Browser(app.Token(scope: "openid"));
        Assert.Equal(HttpStatusCode.Forbidden, (await Provision(client)).StatusCode);
        Assert.Equal(0, app.Repository.BootstrapCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-secret")]
    public async Task InvalidOperatorCredentialNeverCallsProviderOrDatabase(string? secret)
    {
        using var app = new UsersApiFactory(); using var client = app.Browser(app.Token());
        Assert.Equal(HttpStatusCode.Forbidden, (await Provision(client, secret)).StatusCode);
        Assert.Equal(0, app.Identity.Calls); Assert.Equal(0, app.Repository.BootstrapCalls);
    }

    [Fact]
    public async Task BootstrapIsDisabledWithoutStrongOperatorConfiguration()
    {
        using var app = new UsersApiFactory { ConfiguredSecret = "short" }; using var client = app.Browser(app.Token());
        Assert.Equal(HttpStatusCode.NotFound, (await Provision(client)).StatusCode);
        Assert.Equal(0, app.Repository.BootstrapCalls);
    }

    [Theory]
    [InlineData("different-subject", UsersApiFactory.Email, true)]
    [InlineData("admin-subject", "different@example.test", true)]
    [InlineData("admin-subject", UsersApiFactory.Email, false)]
    public async Task BootstrapRequiresMatchingVerifiedProviderIdentity(string subject, string email, bool verified)
    {
        using var app = new UsersApiFactory(); app.Identity.Subject = subject; app.Identity.Email = email; app.Identity.Verified = verified;
        using var client = app.Browser(app.Token());
        Assert.Equal(HttpStatusCode.Forbidden, (await Provision(client)).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"sub\":\"admin-subject\",\"email\":\"initial-admin@example.test\",\"email_verified\":\"true\"}")]
    public async Task MalformedProviderIdentityFailsClosed(string response)
    {
        using var app = new UsersApiFactory(); app.Identity.RawResponse = response;
        using var client = app.Browser(app.Token());
        Assert.Equal(HttpStatusCode.Forbidden, (await Provision(client)).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Fact]
    public async Task UnavailableIdentityProviderNeverCreatesAdministratorOrLeaksDiagnostics()
    {
        using var app = new UsersApiFactory(); app.Identity.Unavailable = true;
        using var client = app.Browser(app.Token());
        var response = await Provision(client);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("provider-secret", await response.Content.ReadAsStringAsync());
        Assert.Null(app.Repository.Account);
    }

    [Fact]
    public async Task VerifiedTargetBootstrapsAndReadsRoleWithoutExposingProviderIdentifiers()
    {
        using var app = new UsersApiFactory(); var token = app.Token(); using var client = app.Browser(token);
        var response = await Provision(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>();
        Assert.Equal("administrator", profile!.Role); Assert.Equal("active", profile.Status);
        Assert.Equal(new Uri(UsersApiFactory.Issuer + "userinfo"), app.Identity.Destination);
        Assert.Equal("Bearer " + token, app.Identity.Authorization);
        var read = await client.GetFromJsonAsync<UserProfileResponse>("/api/v1/users/me");
        Assert.Equal(profile, read);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("subject", json); Assert.DoesNotContain("issuer", json); Assert.DoesNotContain(UsersApiFactory.Secret, json);
        Assert.Equal(profile, await (await Provision(client)).Content.ReadFromJsonAsync<UserProfileResponse>());
    }

    [Fact]
    public async Task OtherIdentityCannotClaimCompletedBootstrapEvenWithTheSameVerifiedEmail()
    {
        using var app = new UsersApiFactory(); using var first = app.Browser(app.Token()); await Provision(first);
        app.Identity.Subject = "other-subject";
        using var other = app.Browser(app.Token(subject: "other-subject"));
        Assert.Equal(HttpStatusCode.Conflict, (await Provision(other)).StatusCode);
        Assert.Equal("admin-subject", app.Repository.Account!.Subject);
    }

    [Fact]
    public async Task VerifiedFirstSignInCreatesOnlyAnOrdinaryTraveller()
    {
        using var app = new UsersApiFactory(); using var client = app.Browser(app.Token());
        var response = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>();
        Assert.Equal("traveller", profile!.Role);
        Assert.Equal("active", profile.Status);
        Assert.Equal(0, app.Repository.BootstrapCalls);
    }

    [Fact]
    public async Task DeactivatedAccountCannotReadItsProfile()
    {
        using var app = new UsersApiFactory(); using var client = app.Browser(app.Token());
        Assert.Equal(HttpStatusCode.OK, (await Provision(client)).StatusCode);
        app.Repository.Deactivate();
        var response = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(UsersApiFactory.Email, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BootstrapRequiresHttps()
    {
        using var app = new UsersApiFactory(); using var client = app.Browser(app.Token(), false);
        Assert.Equal(HttpStatusCode.BadRequest, (await Provision(client)).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Fact]
    public async Task LiveAccessLookupRequiresSeparateServiceCredentialAndDoesNotProvisionUnknownUsers()
    {
        using var app = new UsersApiFactory(); using var client = app.Browser(app.Token());
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
