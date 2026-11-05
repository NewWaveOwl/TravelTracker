using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Data.Repositories;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** HTTP tests cover bearer validation, request forwarding and response mapping; PostgreSQL tests prove role policy. */
public sealed class UserAdministrationApiTests
{
    static readonly Guid TargetId = Guid.Parse("019ac550-1000-7000-8000-000000000002");
    static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-11-04T12:00:00Z");

    static UserAccount Account(string role = "traveller") => new(TargetId, new ProviderIdentity(UsersApiFactory.Issuer, "target"),
        role == "administrator" ? UserRole.Administrator : UserRole.User, UserStatus.Active, Created) { VerifiedEmail = "target@example.test" };

    static Task<HttpResponseMessage> Change(HttpClient client, Guid id, string role = "administrator", string expectedRole = "traveller", string reason = "Trusted application administrator") =>
        client.PatchAsJsonAsync($"/api/v1/users/{id}/role", new { role, expectedRole, reason });

    [Fact]
    public async Task AccountListAndRoleChangesRequireAuthenticationBeforeCallingRepository()
    {
        using var app = new UsersApiFactory(); using var client = app.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Change(client, TargetId)).StatusCode);
        Assert.Null(app.Repository.AdministratorListRequest);
        Assert.Null(app.Repository.RoleChangeRequest);
    }

    [Fact]
    public async Task RepositoryListDenialIsAuthoritativeDespiteProviderAdministratorClaim()
    {
        using var app = new UsersApiFactory();
        app.Repository.AdministratorListResult = null;
        using var client = app.Browser(app.Token(subject: "actor", role: "administrator"));
        var list = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.DoesNotContain("target@example.test", await list.Content.ReadAsStringAsync());
        Assert.Equal((UsersApiFactory.Issuer, "actor"), app.Repository.AdministratorListRequest!.Value);
    }

    [Fact]
    public async Task ListForwardsValidatedIdentityAndMapsOnlyPublicProfileFields()
    {
        using var app = new UsersApiFactory();
        // Deliberately retain the repository's order rather than introducing controller-side sorting.
        app.Repository.AdministratorListResult = [Account(), Account() with { PublicId = Guid.Parse("019ac550-1000-7000-8000-000000000003"), VerifiedEmail = "another@example.test" }];
        using var client = app.Browser(app.Token(subject: "actor"));
        var response = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((UsersApiFactory.Issuer, "actor"), app.Repository.AdministratorListRequest!.Value);
        var profiles = await response.Content.ReadFromJsonAsync<List<UserProfileResponse>>();
        Assert.Equal(new[] { "target@example.test", "another@example.test" }, profiles!.Select(a => a.Email));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var account in json.RootElement.EnumerateArray())
            Assert.Equal(new[] { "email", "role", "status", "userId" }, account.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("administrator", "traveller")]
    [InlineData("traveller", "administrator")]
    public async Task SuccessfulChangeForwardsIdentityAndWhitelistedRequestWithoutRequiringCatalogueWrite(string role, string expectedRole)
    {
        using var app = new UsersApiFactory();
        var saved = Account(role);
        app.Repository.RoleChangeResult = new("changed", saved);
        using var client = app.Browser(app.Token(subject: "actor", scope: "catalogue.read"));
        const string reason = "  Approved account access change  ";
        var response = await Change(client, TargetId, role, expectedRole, reason);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new UserProfileResponse(saved.PublicId, saved.VerifiedEmail, role, "active"), await response.Content.ReadFromJsonAsync<UserProfileResponse>());
        Assert.Equal((UsersApiFactory.Issuer, "actor", TargetId, role, expectedRole, reason), app.Repository.RoleChangeRequest!.Value);
        Assert.Equal(1, app.Repository.RoleChangeCalls);
    }

    [Theory]
    [InlineData("forbidden", HttpStatusCode.Forbidden, null)]
    [InlineData("notfound", HttpStatusCode.NotFound, null)]
    [InlineData("conflict", HttpStatusCode.Conflict, "Refresh the user list")]
    [InlineData("last-administrator", HttpStatusCode.Conflict, "last active administrator")]
    [InlineData("invalid", HttpStatusCode.BadRequest, "not valid for this account")]
    [InlineData("unknown-outcome", HttpStatusCode.ServiceUnavailable, "could not be confirmed")]
    public async Task RepositoryOutcomesMapToSafePublicFailures(string code, HttpStatusCode expectedStatus, string? message)
    {
        using var app = new UsersApiFactory();
        app.Repository.RoleChangeResult = new UserRoleChangeResult(code, null);
        using var client = app.Browser(app.Token(subject: "actor"));
        var response = await Change(client, TargetId);
        Assert.Equal(expectedStatus, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (message is not null) Assert.Contains(message, body);
        Assert.DoesNotContain("target@example.test", body);
        Assert.Equal(1, app.Repository.RoleChangeCalls);
    }

    [Theory]
    [InlineData("root", "traveller", "Reason")]
    [InlineData("administrator", "root", "Reason")]
    [InlineData("administrator", "traveller", "")]
    [InlineData("administrator", "traveller", "   ")]
    public async Task InvalidRoleOrMissingReasonIsRejectedBeforeCallingRepository(string role, string expectedRole, string reason)
    {
        using var app = new UsersApiFactory(); using var client = app.Browser(app.Token(subject: "actor"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(client, TargetId, role, expectedRole, reason)).StatusCode);
        Assert.Null(app.Repository.RoleChangeRequest);
    }

    [Fact]
    public async Task OverlongReasonAndUnknownFieldsAreRejectedBeforeCallingRepository()
    {
        using var app = new UsersApiFactory(); using var client = app.Browser(app.Token(subject: "actor"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(client, TargetId, reason: new string('x', 501))).StatusCode);
        var body = JsonContent.Create(new { role = "administrator", expectedRole = "traveller", reason = "Reason", status = "active", issuer = UsersApiFactory.Issuer });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsync($"/api/v1/users/{TargetId}/role", body)).StatusCode);
        Assert.Null(app.Repository.RoleChangeRequest);
    }

    [Fact]
    public async Task AdministrationRequiresHttpsAndAValidReadGrantBeforeCallingRepository()
    {
        using var app = new UsersApiFactory(); using var http = app.Browser(app.Token(subject: "actor"), false);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(http, TargetId)).StatusCode);
        using var noScope = app.Browser(app.Token(subject: "actor", scope: "openid"));
        Assert.Equal(HttpStatusCode.Forbidden, (await noScope.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Change(noScope, TargetId)).StatusCode);
        Assert.Null(app.Repository.AdministratorListRequest);
        Assert.Null(app.Repository.RoleChangeRequest);
    }

}
