using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** HTTP tests for own-profile read/update: scope matrix, spoofing, standing rules and validation, using real JWT validation. */
public sealed class OwnProfileApiTests
{
    const string Me = "/api/v1/users/me";

    static async Task<UsersApiFactory> Seeded(string status = "active")
    {
        var app = new UsersApiFactory();
        var account = await app.Repository.GetOrCreateTravellerAsync(UsersApiFactory.Issuer, "traveller", "pat@example.test", default);
        if (status != "active") app.Repository.SetStatus(account.UserId, status);
        return app;
    }

    static string Json(HttpResponseMessage response) => response.Content.ReadAsStringAsync().Result;

    [Theory]
    [InlineData(null, 401)]
    [InlineData("openid", 403)]
    [InlineData("catalogue.read catalogue.write", 403)]
    [InlineData("users.write", 403)]
    [InlineData("users.read.extra", 403)]
    [InlineData("users.read", 200)]
    public async Task ReadRequiresTheUsersReadScope(string? scope, int expected)
    {
        using var app = await Seeded();
        using var client = app.Browser(scope is null ? null : app.Token(subject: "traveller", scope: scope));
        Assert.Equal(expected, (int)(await client.GetAsync(Me)).StatusCode);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("openid", 403)]
    [InlineData("catalogue.read catalogue.write", 403)]
    [InlineData("users.read", 403)]
    [InlineData("users.write.extra", 403)]
    [InlineData("users.write", 200)]
    [InlineData("users.read users.write", 200)]
    public async Task UpdateRequiresTheUsersWriteScope(string? scope, int expected)
    {
        using var app = await Seeded();
        using var client = app.Browser(scope is null ? null : app.Token(subject: "traveller", scope: scope));
        var response = await client.PutAsJsonAsync(Me, new { displayName = "Pat" });
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(expected == 200 ? 1 : 0, app.Repository.ProfileUpdateCalls);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    public async Task InvalidBearerNeverReachesTheRepository(string failure)
    {
        using var app = await Seeded();
        using var client = app.Browser(app.Token(failure, subject: "traveller"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(Me, new { displayName = "Pat" })).StatusCode);
        Assert.Equal(0, app.Repository.ProfileUpdateCalls);
    }

    [Fact]
    public async Task UpdateSavesTrimmedFieldsAndReadReturnsThemWithoutProviderIdentity()
    {
        using var app = await Seeded();
        using var client = app.Browser(app.Token(subject: "traveller"));
        var put = await client.PutAsJsonAsync(Me, new { displayName = "  Pat Traveller  ", address = " 1 Main St\nOslo " });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        using var json = JsonDocument.Parse(Json(await client.GetAsync(Me)));
        Assert.Equal("Pat Traveller", json.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("1 Main St\nOslo", json.RootElement.GetProperty("address").GetString());
        Assert.Equal(new[] { "address", "displayName", "email", "role", "status", "userId" },
            json.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task NewAccountsStartWithTheEmailLocalPartAsDisplayNameAndNoAddress()
    {
        using var app = await Seeded();
        using var client = app.Browser(app.Token(subject: "traveller"));
        using var json = JsonDocument.Parse(Json(await client.GetAsync(Me)));
        Assert.Equal("pat", json.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("address").ValueKind);
    }

    [Fact]
    public async Task BlankAddressClearsIt()
    {
        using var app = await Seeded();
        using var client = app.Browser(app.Token(subject: "traveller"));
        await client.PutAsJsonAsync(Me, new { displayName = "Pat", address = "Somewhere" });
        using var json = JsonDocument.Parse(Json(await client.PutAsJsonAsync(Me, new { displayName = "Pat", address = "   " })));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("address").ValueKind);
    }

    [Theory]
    [InlineData("userId", "\"019ac550-1000-7000-8000-000000000009\"")]
    [InlineData("email", "\"evil@example.test\"")]
    [InlineData("role", "\"administrator\"")]
    [InlineData("status", "\"active\"")]
    [InlineData("issuer", "\"https://evil.example.test/\"")]
    [InlineData("subject", "\"admin-subject\"")]
    public async Task ClientControlledIdentityRoleAndStatusFieldsAreRejectedUnchanged(string field, string value)
    {
        using var app = await Seeded();
        using var client = app.Browser(app.Token(subject: "traveller"));
        var content = new StringContent($"{{\"displayName\":\"Hacker\",\"{field}\":{value}}}", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(Me, content)).StatusCode);
        Assert.Equal(0, app.Repository.ProfileUpdateCalls);
        Assert.Equal("pat", app.Repository.Account!.DisplayName);
        Assert.Equal("traveller", app.Repository.Account.Role);
    }

    [Fact]
    public async Task SuppliedIdOrIdentityHeadersCannotRedirectTheUpdateToAnotherAccount()
    {
        using var app = await Seeded();
        var other = await app.Repository.GetOrCreateTravellerAsync(UsersApiFactory.Issuer, "victim", "victim@example.test", default);
        using var client = app.Browser(app.Token(subject: "traveller"));
        client.DefaultRequestHeaders.Add("X-User-Id", other.UserId.ToString());
        client.DefaultRequestHeaders.Add("X-Subject", "victim");
        client.DefaultRequestHeaders.Add("X-Role", "administrator");
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"{Me}/{other.UserId}", new { displayName = "Hacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(Me, new { displayName = "Mine" })).StatusCode);
        Assert.Equal((UsersApiFactory.Issuer, "traveller"), app.Repository.ProfileUpdateActor!.Value);
        Assert.Equal("victim", (await app.Repository.FindAsync(UsersApiFactory.Issuer, "victim", default))!.DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tab\there")]
    public async Task InvalidDisplayNameReturnsValidationProblemAndLeavesProfileUnchanged(string? displayName)
    {
        using var app = await Seeded();
        using var client = app.Browser(app.Token(subject: "traveller"));
        var response = await client.PutAsJsonAsync(Me, new { displayName });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("displayName", Json(response));
        Assert.Equal(0, app.Repository.ProfileUpdateCalls);
        Assert.Equal("pat", app.Repository.Account!.DisplayName);
    }

    [Fact]
    public async Task OverlongFieldsAreRejectedAtTheirLimitsAndAcceptedAtThem()
    {
        using var app = await Seeded();
        using var client = app.Browser(app.Token(subject: "traveller"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Me, new { displayName = new string('a', 61) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Me, new { displayName = "Pat", address = new string('a', 201) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(Me, new { displayName = new string('a', 60), address = new string('b', 200) })).StatusCode);
    }

    [Fact]
    public async Task BannedAccountsReadButCannotUpdate()
    {
        using var app = await Seeded("banned");
        using var client = app.Browser(app.Token(subject: "traveller"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(Me, new { displayName = "New" })).StatusCode);
        Assert.Equal("pat", app.Repository.Account!.DisplayName);
    }

    [Fact]
    public async Task DeactivatedAccountsCannotReadOrUpdate()
    {
        using var app = await Seeded("deactivated");
        using var client = app.Browser(app.Token(subject: "traveller"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Me)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(Me, new { displayName = "New" })).StatusCode);
        Assert.Equal("pat", app.Repository.Account!.DisplayName);
    }

    [Fact]
    public async Task UpdateWithoutAnAccountIsNotFoundAndProvisionsNothing()
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token(subject: "stranger"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(Me, new { displayName = "Pat" })).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Fact]
    public async Task UpdateOverHttpIsRejected()
    {
        using var app = await Seeded();
        using var client = app.Browser(app.Token(subject: "traveller"), https: false);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Me, new { displayName = "Pat" })).StatusCode);
        Assert.Equal(0, app.Repository.ProfileUpdateCalls);
    }
}
