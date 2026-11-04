using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** Role changes are authorized against current Users records, not provider role claims or catalogue-write scope. */
public sealed class UserAdministrationApiTests
{
    static readonly Guid ActorId = Guid.Parse("019ac550-1000-7000-8000-000000000001");
    static readonly Guid TargetId = Guid.Parse("019ac550-1000-7000-8000-000000000002");
    static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-11-04T12:00:00Z");

    static void Seed(UsersApiFactory app, string actorRole = "administrator", string actorStatus = "active", string targetRole = "traveller", string targetStatus = "active")
    {
        app.Repository.Seed(new(ActorId, UsersApiFactory.Issuer, "actor", "actor@example.test", actorRole, actorStatus, Created));
        app.Repository.Seed(new(TargetId, UsersApiFactory.Issuer, "target", "target@example.test", targetRole, targetStatus, Created));
    }

    static Task<HttpResponseMessage> Change(HttpClient client, Guid id, string role = "administrator", string expectedRole = "traveller", string reason = "Trusted application administrator") =>
        client.PatchAsJsonAsync($"/api/v1/users/{id}/role", new { role, expectedRole, reason });

    [Fact]
    public async Task AccountListAndRoleChangesRequireAuthentication()
    {
        using var app = new UsersApiFactory(); Seed(app); using var client = app.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Change(client, TargetId)).StatusCode);
        Assert.Equal(0, app.Repository.RoleChanges);
    }

    [Theory]
    [InlineData("traveller", "active")]
    [InlineData("administrator", "banned")]
    [InlineData("administrator", "deactivated")]
    public async Task CurrentNonAdminOrInactiveActorCannotListOrChangeOtherAccounts(string role, string status)
    {
        using var app = new UsersApiFactory(); Seed(app, role, status); using var client = app.Browser(app.Token(subject: "actor", role: "administrator"));
        var list = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.DoesNotContain("target@example.test", await list.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await Change(client, TargetId)).StatusCode);
        Assert.Equal(0, app.Repository.RoleChanges);
    }

    [Fact]
    public async Task MissingStoredActorCannotUseProviderAdministratorClaim()
    {
        using var app = new UsersApiFactory(); using var client = app.Browser(app.Token(role: "administrator"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Change(client, TargetId)).StatusCode);
        Assert.Equal(0, app.Repository.RoleChanges);
    }

    [Fact]
    public async Task AdministratorListsPublicProfilesWithoutProviderIdentityOrCredentials()
    {
        using var app = new UsersApiFactory(); Seed(app); using var client = app.Browser(app.Token(subject: "actor"));
        var response = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profiles = await response.Content.ReadFromJsonAsync<List<UserProfileResponse>>();
        Assert.Equal(new[] { "actor@example.test", "target@example.test" }, profiles!.Select(a => a.Email));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var account in json.RootElement.EnumerateArray())
            Assert.Equal(new[] { "email", "role", "status", "userId" }, account.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ActiveAdministratorCanPromoteAndDemoteWithOnlyCatalogueReadAndConfirmedCurrentRole()
    {
        using var app = new UsersApiFactory(); Seed(app); using var client = app.Browser(app.Token(subject: "actor"));
        var promoted = await Change(client, TargetId);
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        Assert.Equal("administrator", (await promoted.Content.ReadFromJsonAsync<UserProfileResponse>())!.Role);
        var demoted = await Change(client, TargetId, "traveller", "administrator", "Return to traveller access");
        Assert.Equal(HttpStatusCode.OK, demoted.StatusCode);
        Assert.Equal("traveller", (await demoted.Content.ReadFromJsonAsync<UserProfileResponse>())!.Role);
        Assert.Equal(2, app.Repository.RoleChanges);
    }

    [Fact]
    public async Task LastActiveAdministratorCannotBeDemoted()
    {
        using var app = new UsersApiFactory(); Seed(app); using var client = app.Browser(app.Token(subject: "actor"));
        var response = await Change(client, ActorId, "traveller", "administrator");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("last active administrator", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, app.Repository.RoleChanges);
    }

    [Fact]
    public async Task SelfDemotionImmediatelyRemovesFurtherAdministrationAccess()
    {
        using var app = new UsersApiFactory(); Seed(app, targetRole: "administrator"); using var client = app.Browser(app.Token(subject: "actor"));
        Assert.Equal(HttpStatusCode.OK, (await Change(client, ActorId, "traveller", "administrator")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Change(client, TargetId, "traveller", "administrator")).StatusCode);
        Assert.Equal(1, app.Repository.RoleChanges);
    }

    [Fact]
    public async Task StaleExpectedRoleAndRepeatedNoOpChangesDoNotWrite()
    {
        using var app = new UsersApiFactory(); Seed(app); using var client = app.Browser(app.Token(subject: "actor"));
        Assert.Equal(HttpStatusCode.Conflict, (await Change(client, TargetId, "administrator", "administrator")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Change(client, TargetId, "traveller", "traveller")).StatusCode);
        Assert.Equal(0, app.Repository.RoleChanges);
    }

    [Theory]
    [InlineData("root", "traveller", "Reason")]
    [InlineData("administrator", "root", "Reason")]
    [InlineData("administrator", "traveller", "")]
    [InlineData("administrator", "traveller", "   ")]
    public async Task InvalidRoleOrMissingReasonIsRejectedWithoutPersistence(string role, string expectedRole, string reason)
    {
        using var app = new UsersApiFactory(); Seed(app); using var client = app.Browser(app.Token(subject: "actor"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(client, TargetId, role, expectedRole, reason)).StatusCode);
        Assert.Equal(0, app.Repository.RoleChanges);
    }

    [Fact]
    public async Task OverlongReasonAndUnknownFieldsAreRejectedWithoutPersistence()
    {
        using var app = new UsersApiFactory(); Seed(app); using var client = app.Browser(app.Token(subject: "actor"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(client, TargetId, reason: new string('x', 501))).StatusCode);
        var body = JsonContent.Create(new { role = "administrator", expectedRole = "traveller", reason = "Reason", status = "active", issuer = UsersApiFactory.Issuer });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsync($"/api/v1/users/{TargetId}/role", body)).StatusCode);
        Assert.Equal(0, app.Repository.RoleChanges);
    }

    [Fact]
    public async Task InactiveTargetsCannotBePromoted()
    {
        using var app = new UsersApiFactory(); Seed(app, targetStatus: "banned"); using var client = app.Browser(app.Token(subject: "actor"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(client, TargetId)).StatusCode);
        Assert.Equal(0, app.Repository.RoleChanges);
    }

    [Fact]
    public async Task MissingTargetReturnsNotFoundWithoutCreatingAnAccount()
    {
        using var app = new UsersApiFactory(); Seed(app); using var client = app.Browser(app.Token(subject: "actor"));
        Assert.Equal(HttpStatusCode.NotFound, (await Change(client, Guid.NewGuid())).StatusCode);
        Assert.Equal(0, app.Repository.RoleChanges);
    }

    [Fact]
    public async Task AdministrationRequiresHttpsAndAValidReadGrant()
    {
        using var app = new UsersApiFactory(); Seed(app); using var http = app.Browser(app.Token(subject: "actor"), false);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(http, TargetId)).StatusCode);
        using var noScope = app.Browser(app.Token(subject: "actor", scope: "openid"));
        Assert.Equal(HttpStatusCode.Forbidden, (await noScope.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Change(noScope, TargetId)).StatusCode);
        Assert.Equal(0, app.Repository.RoleChanges);
    }

    [Theory]
    [InlineData(false, "admin-subject")]
    [InlineData(true, "wrong-subject")]
    public async Task UnverifiedOrMismatchedFirstSignInNeverProvisionsAUser(bool verified, string subject)
    {
        using var app = new UsersApiFactory(); app.Identity.Verified = verified; app.Identity.Subject = subject;
        using var client = app.Browser(app.Token(role: "administrator"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users/me")).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Fact]
    public async Task ProviderRoleClaimsNeverPromoteNewlyProvisionedTravellerAndExistingRoleIsRetained()
    {
        using var app = new UsersApiFactory(); using var client = app.Browser(app.Token(role: "administrator"));
        var first = await client.GetFromJsonAsync<UserProfileResponse>("/api/v1/users/me");
        Assert.Equal("traveller", first!.Role);
        Assert.Equal(1, app.Identity.Calls);
        var again = await client.GetFromJsonAsync<UserProfileResponse>("/api/v1/users/me");
        Assert.Equal(first, again); Assert.Equal(1, app.Identity.Calls);
    }
}
