using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using TravelTracker.Users.Api.Services;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** Exercises the signed-in password change through production JWT validation and the credential store. */
public sealed class PasswordChangeApiTests
{
    const string Path = "/api/v1/users/me/password";
    const string Subject = "password-subject";
    const string Login = "password-owner";
    const string OldPassword = "correct horse";
    const string NewPassword = "battery staple";
    const string Writer = "users.read users.write";

    /** Registers a password account whose token subject is Subject. */
    static UsersApiFactory SeededApp(string subject = Subject, string login = Login)
    {
        var app = new UsersApiFactory();
        app.Credentials.Seed(login, UsersApiFactory.Issuer, subject, PasswordCredentials.Hash(OldPassword));
        return app;
    }

    static bool Accepts(string hash, string password) =>
        PasswordCredentials.Verify(hash, password) != PasswordVerificationResult.Failed;

    static object Body(string currentPassword, string newPassword) => new { currentPassword, newPassword };

    [Fact]
    public async Task ChangeWithTheCorrectCurrentPassword_SavesTheNewHashAndKeepsTheSession()
    {
        using var app = SeededApp();
        using var client = app.Browser(app.Token(subject: Subject, scope: Writer));

        var response = await client.PostAsJsonAsync(Path, Body(OldPassword, NewPassword));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var hash = app.Credentials.For(Login).PasswordHash;
        Assert.True(Accepts(hash, NewPassword));
        Assert.False(Accepts(hash, OldPassword));
        Assert.DoesNotContain(NewPassword, hash);
        // tokens are not revoked, so the same bearer keeps working until it expires
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/users/me")).StatusCode);
        Assert.DoesNotContain(app.Logs.Entries, entry => entry.Contains(OldPassword) || entry.Contains(NewPassword));
    }

    [Fact]
    public async Task WrongCurrentPassword_IsAValidationFailureAndChangesNothing()
    {
        using var app = SeededApp();
        using var client = app.Browser(app.Token(subject: Subject, scope: Writer));
        var before = app.Credentials.For(Login).PasswordHash;

        var response = await client.PostAsJsonAsync(Path, Body("not the password", NewPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("currentPassword", out _));
        Assert.Equal(before, app.Credentials.For(Login).PasswordHash);
        Assert.True(Accepts(before, OldPassword));
        Assert.DoesNotContain(app.Logs.Entries, entry => entry.Contains("not the password") || entry.Contains(NewPassword));
    }

    [Theory]
    [InlineData(5, HttpStatusCode.BadRequest)]
    [InlineData(6, HttpStatusCode.NoContent)]
    [InlineData(128, HttpStatusCode.NoContent)]
    [InlineData(129, HttpStatusCode.BadRequest)]
    public async Task NewPasswordFollowsTheSixToOneHundredTwentyEightCharacterRule(int length, HttpStatusCode expected)
    {
        using var app = SeededApp();
        using var client = app.Browser(app.Token(subject: Subject, scope: Writer));
        var candidate = new string('x', length);

        var response = await client.PostAsJsonAsync(Path, Body(OldPassword, candidate));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.NoContent, Accepts(app.Credentials.For(Login).PasswordHash, candidate));
        if (expected == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(problem.GetProperty("errors").TryGetProperty("newPassword", out _));
            Assert.True(Accepts(app.Credentials.For(Login).PasswordHash, OldPassword));
        }
    }

    [Fact]
    public async Task MissingCurrentPassword_IsAValidationFailure()
    {
        using var app = SeededApp();
        using var client = app.Browser(app.Token(subject: Subject, scope: Writer));

        var response = await client.PostAsJsonAsync(Path, new { newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(Accepts(app.Credentials.For(Login).PasswordHash, OldPassword));
    }

    [Fact]
    public async Task AnonymousOrForgedBearer_CannotChangeAnyPassword()
    {
        using var app = SeededApp();
        using var anonymous = app.Browser();
        using var forged = app.Browser(app.Token("signature", subject: Subject, scope: Writer));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Path, Body(OldPassword, NewPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.PostAsJsonAsync(Path, Body(OldPassword, NewPassword))).StatusCode);
        Assert.True(Accepts(app.Credentials.For(Login).PasswordHash, OldPassword));
    }

    [Fact]
    public async Task ReadOnlySession_CannotChangeThePassword()
    {
        using var app = SeededApp();
        using var reader = app.Browser(app.Token(subject: Subject, scope: "users.read"));

        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(Path, Body(OldPassword, NewPassword))).StatusCode);
        Assert.True(Accepts(app.Credentials.For(Login).PasswordHash, OldPassword));
    }

    [Fact]
    public async Task CallerCanOnlyChangeTheirOwnPassword()
    {
        using var app = new UsersApiFactory();
        app.Credentials.Seed("alice", UsersApiFactory.Issuer, "alice-subject", PasswordCredentials.Hash("alice password"));
        app.Credentials.Seed("bob", UsersApiFactory.Issuer, "bob-subject", PasswordCredentials.Hash("bob password"));
        var bobHash = app.Credentials.For("bob").PasswordHash;
        using var alice = app.Browser(app.Token(subject: "alice-subject", scope: Writer));

        // bob's password is not alice's current password, so alice cannot reach bob's hash by supplying it
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync(Path, Body("bob password", NewPassword))).StatusCode);
        // a subject in the body is not a supported field and cannot select another account
        var smuggled = await alice.PostAsJsonAsync(Path, new { currentPassword = "alice password", newPassword = NewPassword, subject = "bob-subject" });
        Assert.NotEqual(HttpStatusCode.NoContent, smuggled.StatusCode);

        Assert.Equal(bobHash, app.Credentials.For("bob").PasswordHash);
        Assert.True(Accepts(app.Credentials.For("alice").PasswordHash, "alice password"));
        Assert.True(Accepts(bobHash, "bob password"));
    }

    [Fact]
    public async Task BannedAccount_CannotChangeItsPassword()
    {
        using var app = SeededApp();
        app.Credentials.SetStatus(Login, UserStatus.Banned);
        using var client = app.Browser(app.Token(subject: Subject, scope: Writer));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Path, Body(OldPassword, NewPassword))).StatusCode);
        Assert.True(Accepts(app.Credentials.For(Login).PasswordHash, OldPassword));
    }

    [Fact]
    public async Task AccountWithoutAPasswordCredential_CannotUseThePasswordRoute()
    {
        using var app = new UsersApiFactory();
        using var client = app.Browser(app.Token(subject: "provider-only-subject", scope: Writer));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Path, Body(OldPassword, NewPassword))).StatusCode);
    }
}
