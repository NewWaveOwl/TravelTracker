using System.Net;
using System.Net.Http.Json;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** verifies the Users API when Supabase Auth is the identity provider instead of the built-in password sign-in. */
public sealed class SupabaseModeTests
{
    const string Bootstrap = "/api/v1/users/bootstrap-first-administrator";
    const string Subject = "10d5c7fe-8989-4a43-b68a-9c45e9a73bd8";

    static Task<HttpResponseMessage> Provision(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Bootstrap) { Content = JsonContent.Create(new { }) };
        request.Headers.Add("X-Bootstrap-Secret", UsersApiFactory.Secret);
        return client.SendAsync(request);
    }

    static string SupabaseToken(UsersApiFactory app) => app.Token(subject: Subject, scope: "catalogue.read", role: "authenticated");

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/auth/register")]
    public async Task PasswordRoutesAreNotServedAndNeverTouchCredentials(string route)
    {
        using var app = new UsersApiFactory { Supabase = true };
        using var client = app.Browser();

        var response = await client.PostAsJsonAsync(route, new { username = "traveller", password = "secret1" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, app.Credentials.Calls);
    }

    [Fact]
    public async Task ServiceTokensAreNotIssuedByTheUsersApi()
    {
        using var app = new UsersApiFactory { Supabase = true };
        using var client = app.Browser();
        client.DefaultRequestHeaders.Authorization = new("Bearer", UsersApiFactory.ServiceToken);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/v1/auth/service-token", null)).StatusCode);
    }

    [Fact]
    public async Task BootstrapPromotesTheAccountWhoseConfirmedEmailMatches()
    {
        using var app = new UsersApiFactory { Supabase = true };
        using var client = app.Browser(SupabaseToken(app));
        app.Identity.User["email"] = UsersApiFactory.Email;

        Assert.Equal(HttpStatusCode.OK, (await Provision(client)).StatusCode);
        Assert.Equal(1, app.Identity.Calls);
        Assert.Equal(1, app.Repository.BootstrapCalls);
        Assert.Equal(0, app.Credentials.Calls);
    }

    [Fact]
    public async Task BootstrapRefusesAnotherConfirmedEmail()
    {
        using var app = new UsersApiFactory { Supabase = true };
        using var client = app.Browser(SupabaseToken(app));
        app.Identity.User["email"] = "someone-else@example.test";

        Assert.Equal(HttpStatusCode.Forbidden, (await Provision(client)).StatusCode);
        Assert.Equal(0, app.Repository.BootstrapCalls);
    }

    [Fact]
    public async Task TokensFromAnAnonymousSupabaseUserAreRejected()
    {
        using var app = new UsersApiFactory { Supabase = true };
        using var client = app.Browser(app.Token(subject: Subject, scope: "catalogue.read", role: "service_role"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Provision(client)).StatusCode);
        Assert.Equal(0, app.Identity.Calls);
    }
}
