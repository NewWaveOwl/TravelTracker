using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Data.Repositories.Profile;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** Exercises the self-profile and backchannel routes through production JWT middleware. */
public sealed class UserProfileApiTests
{
    const string Me = "/api/v1/users/me";

    [Fact]
    public async Task FirstReadProvisionsOneIncompleteAccountFromValidatedIdentity()
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token(subject: "new-subject", role: "administrator", scope: "users.read"));
        var first = (await client.GetFromJsonAsync<SelfProfileResponse>(Me))!;
        Assert.Equal(7, first.PublicId.Version);
        Assert.Equal("traveller", first.Role);
        Assert.Equal("active", first.Status);
        Assert.Null(first.Username);
        Assert.Null(first.Email);
        Assert.Null(first.Address);
        Assert.Null(first.UpdatedAt);
        Assert.Equal("new-subject", app.Repository.Account!.ProviderIdentity.Subject);
        Assert.Equal(first, await client.GetFromJsonAsync<SelfProfileResponse>(Me));
        Assert.Equal(0, app.Identity.Calls);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    public async Task InvalidBearerCannotProvisionAProfile(string failure)
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token(failure));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Me)).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Fact]
    public async Task SuppliedIdentityHeadersAndQueryIdCannotReadOrChangeAnotherProfile()
    {
        using var app = new UsersApiFactory();
        using var other = app.Browser(app.Token(subject: "other-subject", scope: "users.read users.write"));
        var otherProfile = (await other.GetFromJsonAsync<SelfProfileResponse>(Me))!;
        Assert.Equal(HttpStatusCode.OK,
            (await other.PatchAsJsonAsync(Me, new { username = "other-name" })).StatusCode);

        using var caller = app.Browser(app.Token(subject: "caller-subject", scope: "users.read users.write"));
        caller.DefaultRequestHeaders.Add("X-User-Id", otherProfile.PublicId.ToString());
        caller.DefaultRequestHeaders.Add("X-Subject", "other-subject");
        var spoofedPath = $"{Me}?userId={otherProfile.PublicId:D}";
        var callerProfile = (await caller.GetFromJsonAsync<SelfProfileResponse>(spoofedPath))!;
        Assert.NotEqual(otherProfile.PublicId, callerProfile.PublicId);
        Assert.Null(callerProfile.Username);
        Assert.Equal(HttpStatusCode.OK,
            (await caller.PatchAsJsonAsync(spoofedPath, new { username = "caller-name" })).StatusCode);
        Assert.Equal("caller-name", (await caller.GetFromJsonAsync<SelfProfileResponse>(Me))!.Username);
        Assert.Equal("other-name", (await other.GetFromJsonAsync<SelfProfileResponse>(Me))!.Username);
    }

    [Fact]
    public async Task MissingBearerOrBlankSubjectCannotReadOrPatch()
    {
        using var app = new UsersApiFactory();
        using var anonymous = app.Browser();
        using var blankSubject = app.Browser(app.Token(subject: " ", scope: "users.read users.write"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Me)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PatchAsJsonAsync(Me, new { username = "test-name" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await blankSubject.GetAsync(Me)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await blankSubject.PatchAsJsonAsync(Me, new { username = "test-name" })).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Fact]
    public async Task ReadAndWriteScopesAuthorizeOnlyTheirOwnOperations()
    {
        using var app = new UsersApiFactory();
        using var writer = app.Browser(app.Token(scope: "users.write"));
        Assert.Equal(HttpStatusCode.Forbidden, (await writer.GetAsync(Me)).StatusCode);
        Assert.Null(app.Repository.Account);
        using var reader = app.Browser(app.Token(scope: "users.read"));
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(Me)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PatchAsJsonAsync(Me, new { username = "new-name" })).StatusCode);
        Assert.Null(app.Repository.Account!.Username);
    }

    [Fact]
    public async Task PatchTrimsPresentFieldsAndAllowsExplicitAddressClearing()
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token(scope: "users.read users.write"));
        var before = (await client.GetFromJsonAsync<SelfProfileResponse>(Me))!;
        var patch = await client.PatchAsJsonAsync(Me, new { username = "  Map.Editor ", email = " editor@example.test ", address = " 1 Map Street " });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var changed = (await patch.Content.ReadFromJsonAsync<SelfProfileResponse>())!;
        Assert.Equal(before.PublicId, changed.PublicId);
        Assert.Equal(before.CreatedAt, changed.CreatedAt);
        Assert.Equal("Map.Editor", changed.Username);
        Assert.Equal("editor@example.test", changed.Email);
        Assert.Equal("1 Map Street", changed.Address);
        Assert.NotNull(changed.UpdatedAt);

        var clear = await client.PatchAsJsonAsync(Me, new { address = (string?)null });
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var after = (await clear.Content.ReadFromJsonAsync<SelfProfileResponse>())!;
        Assert.Equal(changed.Username, after.Username);
        Assert.Equal(changed.Email, after.Email);
        Assert.Null(after.Address);
        Assert.Equal(after, await client.GetFromJsonAsync<SelfProfileResponse>(Me));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"username\":null}")]
    [InlineData("{\"username\":4}")]
    [InlineData("{\"username\":\"bad name\"}")]
    [InlineData("{\"email\":null}")]
    [InlineData("{\"email\":\"invalid-email\"}")]
    [InlineData("{\"email\":\"Display Name <editor@example.test>\"}")]
    [InlineData("{\"address\":\"1 Test\\u0000 Avenue\"}")]
    [InlineData("{\"address\":\" \"}")]
    [InlineData("{\"username\":\"valid-name\",\"role\":\"administrator\"}")]
    [InlineData("{\"username\":\"valid-name\",\"publicId\":\"f8ac6c5f-cda8-4986-a6d0-056cd55f88cf\"}")]
    public async Task InvalidPatchLeavesProfileAndTimestampUnchanged(string body)
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token(scope: "users.read users.write"));
        var before = await client.GetFromJsonAsync<SelfProfileResponse>(Me);
        using var json = new StringContent(body, Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsync(Me, json)).StatusCode);
        Assert.Equal(before, await client.GetFromJsonAsync<SelfProfileResponse>(Me));
    }

    [Theory]
    [InlineData(UserStatus.Banned, HttpStatusCode.OK)]
    [InlineData(UserStatus.Deactivated, HttpStatusCode.Forbidden)]
    public async Task InactiveStandingPreventsWritesAndDeactivationAlsoPreventsReads(UserStatus status, HttpStatusCode readStatus)
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token(scope: "users.read users.write"));
        var profile = (await client.GetFromJsonAsync<SelfProfileResponse>(Me))!;
        app.Repository.SetStatus(profile.PublicId, status);
        Assert.Equal(readStatus, (await client.GetAsync(Me)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync(Me, new { username = "blocked-edit" })).StatusCode);
        Assert.Null(app.Repository.Account!.Username);
        Assert.Null(app.Repository.Account.UpdatedAt);
    }

    [Fact]
    public async Task UnavailableStorageReturnsSafeServiceUnavailable()
    {
        using var app = new UsersApiFactory();
        app.Repository.StorageUnavailable = true;
        using var client = app.Browser(app.Token());
        var response = await client.GetAsync(Me);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("test-storage-details", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InternalLookupRequiresBothServiceSubjectAndExactAccessScope()
    {
        using var app = new UsersApiFactory();
        var path = $"/internal/users/{Guid.CreateVersion7()}/access";
        using var anonymous = app.Browser();
        using var frontend = app.Browser(app.Token(scope: "users.access"));
        using var wrongScope = app.Browser(app.Token(subject: UsersApiFactory.ServiceSubject, scope: "Users.Access"));
        using var backend = app.Browser(app.Token(subject: UsersApiFactory.ServiceSubject, scope: "users.access"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await frontend.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongScope.GetAsync(path)).StatusCode);
        Assert.Equal(0, app.Repository.ReadCalls);
        Assert.Equal(HttpStatusCode.NotFound, (await backend.GetAsync(path)).StatusCode);
        Assert.Null(app.Repository.Account);
    }

    [Fact]
    public async Task AuthorizedInternalLookupReturnsOnlyLiveStandingWithoutReactivation()
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token());
        var profile = (await client.GetFromJsonAsync<SelfProfileResponse>(Me))!;
        app.Repository.SetStatus(profile.PublicId, UserStatus.Deactivated);

        using var backend = app.Browser(app.Token(subject: UsersApiFactory.ServiceSubject, scope: "users.access"));
        using var access = await backend.GetFromJsonAsync<JsonDocument>($"/internal/users/{profile.PublicId}/access");
        var result = access!.RootElement;
        Assert.Equal(new[] { "role", "status", "userId" }, result.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(profile.PublicId, result.GetProperty("userId").GetGuid());
        Assert.Equal("deactivated", result.GetProperty("status").GetString());
        Assert.Equal("traveller", result.GetProperty("role").GetString());
        Assert.Equal(UserStatus.Deactivated, app.Repository.Account!.Status);
    }

    [Fact]
    public async Task ProfileOperationsKeepIdentityAndSubmittedPersonalDataOutOfLogs()
    {
        using var app = new UsersApiFactory();
        const string subject = "opaque-distinctive-subject";
        const string username = "DistinctiveEditor";
        const string email = "distinctive-editor@example.test";
        const string address = "118 Distinctive Lane";
        var token = app.Token(subject: subject, scope: "users.read users.write");
        using var client = app.Browser(token);
        var profile = (await client.GetFromJsonAsync<SelfProfileResponse>(Me))!;
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync(Me, new { username, email, address })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PatchAsJsonAsync(Me, new { username, email, address, subject })).StatusCode);
        app.Repository.StorageUnavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(Me)).StatusCode);

        var output = string.Join(Environment.NewLine, app.Logs.Entries);
        Assert.Contains("Users request failed", output);
        foreach (var sensitive in new[] { subject, username, email, address, token, UsersApiFactory.Issuer, profile.PublicId.ToString() })
            Assert.DoesNotContain(sensitive, output, StringComparison.OrdinalIgnoreCase);
    }


    [Theory]
    [InlineData("username_conflict")]
    [InlineData("profile_concurrency_conflict")]
    public async Task ProfileWriteConflictsReturnDistinctCodesAndLeaveSavedProfileUnchanged(string code)
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token(scope: "users.read users.write"));
        var before = await client.GetFromJsonAsync<SelfProfileResponse>(Me);
        app.Repository.ProfileUpdateFailure = code == "username_conflict"
            ? new UserProfileUsernameConflictException()
            : new DbUpdateConcurrencyException();
        var response = await client.PatchAsJsonAsync(Me, new { username = "attempted-change" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(code, problem!.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, await client.GetFromJsonAsync<SelfProfileResponse>(Me));
    }

}
