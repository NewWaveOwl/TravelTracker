using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Pages;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

public sealed class AccountTests : BunitContext
{
    static readonly string Profile = """{"publicId":"00000000-0000-0000-0000-000000000101","role":"traveller","status":"active","username":"traveller_1","address":"1 Main St"}""";
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));
    readonly SwitchableAuthentication authentication;

    public AccountTests() => authentication = new(new SessionRegistry(clock));

    void Configure(bool configured = true, StubHandler? handler = null, bool pictureEndpoint = false, bool supabase = false)
    {
        Services.AddSingleton(new AuthSetup(configured, supabase ? new(FakeSupabaseAuth.Origin, "sb_publishable_fixture") : null));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(authentication.Sessions);
        Services.AddSingleton(new ServiceDirectory(new Dictionary<string, string> { ["Users"] = "https://localhost:5101" }));
        var backend = new HttpMessageInvoker(handler ?? StubHandler.Status(200, Profile));
        Services.AddSingleton(backend);
        var routed = new StubHandler((request, token) => !pictureEndpoint && request.RequestUri!.AbsolutePath == "/api/v1/users/me/picture"
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)) : backend.SendAsync(request, token));
        Services.AddSingleton(new ServiceClient(new HttpClient(routed), NullLogger<ServiceClient>.Instance));
        Services.AddScoped<ProtectedClient>();
        Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Services.AddScoped<TravelTracker.Frontend.Api.Users.UsersClient>();
        Services.AddScoped<TravelTracker.Frontend.Services.AdminAccessState>();
        Services.AddLogging();
    }

    [Fact]
    public void SupabaseProfileEmailChangesUseTheVerifiedCredentialFormAndNeverPatchASeparateContactEmail()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Profile, System.Text.Encoding.UTF8, "application/json") }));
        Configure(handler: handler, supabase: true);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        ((System.Security.Claims.ClaimsIdentity)authentication.User.Identity!).AddClaim(new("email", "login@example.test"));
        var page = Render<Account>();
        page.FindAll("button").Single(button => button.TextContent == "Edit profile").Click();

        Assert.Equal("login@example.test", page.Find("#profile-email").GetAttribute("value"));
        Assert.True(page.Find("#profile-email").HasAttribute("readonly"));
        Assert.Equal("login@example.test", page.Find("#email-change-current").GetAttribute("value"));
        Assert.Equal("/auth/change-credential", page.Find("#email-change-new").Closest("form")!.GetAttribute("action"));
        page.Find("#profile-username").Change("updated_name");
        page.Find(".profile-editor form button[type=submit]").Click();

        page.WaitForAssertion(() => Assert.Contains("Your profile has been updated", page.Markup));
        Assert.All(page.FindAll(".credential-fields"), fields => Assert.False(fields.HasAttribute("disabled")));
        using var patch = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("updated_name", patch.RootElement.GetProperty("username").GetString());
        Assert.False(patch.RootElement.TryGetProperty("email", out _));
    }

    [Fact]
    public void ProfileEditingLocksCredentialFormsBeforeAnyDraftFieldChangesAndCancelUnlocksThem()
    {
        Configure(supabase: true);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        Assert.All(page.FindAll(".credential-fields"), fields => Assert.False(fields.HasAttribute("disabled")));

        page.FindAll("button").Single(button => button.TextContent == "Edit profile").Click();

        Assert.Equal(2, page.FindAll(".credential-fields[disabled]").Count);
        Assert.Contains("Save or cancel your profile edits", page.Find("#credential-edit-hint").TextContent);
        page.Find("#profile-username").Change("unsaved_name");
        page.FindAll("button").Single(button => button.TextContent == "Cancel").Click();
        Assert.All(page.FindAll(".credential-fields"), fields => Assert.False(fields.HasAttribute("disabled")));
        Assert.DoesNotContain("unsaved_name", page.Markup);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(503)]
    public void FailedProfileSaveRetainsTheDraftAndKeepsCredentialNavigationUnavailable(int status)
    {
        Configure(supabase: true, handler: new StubHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Patch
            ? new HttpResponseMessage((HttpStatusCode)status)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Profile, System.Text.Encoding.UTF8, "application/json") })));
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.FindAll("button").Single(button => button.TextContent == "Edit profile").Click();
        page.Find("#profile-username").Change("unsaved_name");
        page.Find("#profile-address").Change("Unsaved address");

        page.Find(".profile-editor form button[type=submit]").Click();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".profile-error[role=alert]")));
        Assert.Equal("unsaved_name", page.Find("#profile-username").GetAttribute("value"));
        Assert.Equal("Unsaved address", page.Find("#profile-address").GetAttribute("value"));
        Assert.Equal(2, page.FindAll(".credential-fields[disabled]").Count);
        Assert.DoesNotContain("Your profile has been updated", page.Markup);
    }

    [Fact]
    public void SignedOutCopyDoesNotPromiseLocalProfileOrTripStorage()
    {
        Configure();
        var page = Render<Account>();
        Assert.Contains("Sign in to view your profile", page.Markup);
        Assert.Contains("connected to your TravelTracker account", page.Markup);
        Assert.DoesNotContain("stay on this device", page.Markup);
    }

    [Fact]
    public void SavedPictureStaysWhenProfileTextChangesAreCancelled()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aL1cAAAAASUVORK5CYII=");
        byte[]? picture = null;
        var profileWrites = 0;
        Configure(pictureEndpoint: true, handler: new StubHandler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/users/me/picture")
            {
                Assert.Equal("Bearer token", request.Headers.Authorization!.ToString());
                if (request.Method == HttpMethod.Put)
                {
                    picture = await request.Content!.ReadAsByteArrayAsync(token);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                if (picture is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                return response;
            }
            if (request.Method != HttpMethod.Get) profileWrites++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Profile, System.Text.Encoding.UTF8, "application/json") };
        }));
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        var widget = page.FindComponent<TravelTracker.Frontend.Components.Users.ProfilePicture>().Instance;
        page.Find("button").Click();
        page.Find("#profile-address").Change("unsaved text address");
        var file = Bunit.InputFileContent.CreateFromBinary(png, "private.png", contentType: "image/png");
        page.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(file);
        page.WaitForAssertion(() => Assert.Contains("Photo saved.", page.Markup));

        page.FindAll("button").Single(button => button.TextContent == "Cancel").Click();

        Assert.Same(widget, page.FindComponent<TravelTracker.Frontend.Components.Users.ProfilePicture>().Instance);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(png), page.Find(".profile-picture img").GetAttribute("src"));
        Assert.DoesNotContain("unsaved text address", page.Markup);
        Assert.Equal(0, profileWrites);
    }

    [Fact]
    public void SetupPendingDoesNotOfferSignIn()
    {
        Configure(configured: false);
        var page = Render<Account>();
        Assert.Contains("Sign-in is temporarily unavailable", page.Markup);
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public void SignInFailureUsesFixedSafeCopy()
    {
        Configure();
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("account?signin=not-authorized");
        var page = Render<Account>();
        Assert.Contains("Unable to sign in", page.Markup);
        Assert.Contains("authorize its access", page.Markup);
        Assert.DoesNotContain("not-authorized", page.Markup);
    }

    [Fact]
    public void ExpiredSessionReturnsToSignIn()
    {
        Configure();
        authentication.SignIn("expired-token", clock.GetUtcNow().AddMinutes(-1));
        var page = Render<Account>();
        Assert.Contains("Sign in to view your profile", page.Markup);
        Assert.Contains("session has expired", page.Markup);
    }

    [Fact]
    public void RejectedUsersTokenClearsPrivateProfile()
    {
        Configure(handler: StubHandler.Status(401));
        authentication.SignIn("rejected-token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.WaitForAssertion(() => Assert.Contains("Sign in to view your profile", page.Markup));
        Assert.DoesNotContain("traveller_1", page.Markup);
        Assert.Contains("session was rejected", page.Markup);
    }

    [Fact]
    public void SharedSessionRevocationClearsTheDisplayedProfile()
    {
        Configure();
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.WaitForAssertion(() => Assert.Contains("traveller_1", page.Markup));
        authentication.SignOut();
        page.WaitForAssertion(() => Assert.Contains("Sign in to view your profile", page.Markup));
        Assert.DoesNotContain("traveller_1", page.Markup);
    }

    [Fact]
    public void SessionExpiryWhileProfileIsOpenClearsProfileAndDraft()
    {
        Configure();
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.WaitForAssertion(() => Assert.Contains("traveller_1", page.Markup));
        page.Find("button").Click();
        page.Find("#profile-address").Change("unsaved_draft");

        clock.Advance(TimeSpan.FromMinutes(5));

        page.WaitForAssertion(() => Assert.Contains("Sign in to view your profile", page.Markup));
        Assert.DoesNotContain("traveller_1", page.Markup);
        Assert.DoesNotContain("1 Main St", page.Markup);
        Assert.DoesNotContain("unsaved_draft", page.Markup);
    }

    [Fact]
    public void SignedInProfileShowsReadOnlyRoleAndStatusAndOffersEdit()
    {
        Configure();
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        Assert.Contains("traveller_1", page.Markup);
        Assert.DoesNotContain("email", page.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Traveller", page.Markup);
        Assert.Contains("Active", page.Markup);
        page.Find("button").Click();
        Assert.NotNull(page.Find("#profile-address"));
        Assert.Empty(page.FindAll("#profile-username"));
        Assert.Empty(page.FindAll("input[name='role'], input[name='status']"));
    }

    [Fact]
    public void MissingWriteScopeMakesProfileReadOnly()
    {
        Configure();
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5), "users.read");
        var page = Render<Account>();
        Assert.Contains("does not have permission to edit", page.Markup);
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Contains("Edit profile", StringComparison.Ordinal));
    }

    [Fact]
    public void UsersReadForbiddenShowsDeniedStateWithoutProfileData()
    {
        Configure(handler: StubHandler.Status(403));
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));

        var page = Render<Account>();

        Assert.Contains("Profile access denied", page.Markup);
        Assert.Contains("does not have access to a profile", page.Markup);
        Assert.DoesNotContain("traveller_1", page.Markup);
        Assert.DoesNotContain("Profile details", page.Markup);
    }

    [Fact]
    public void FirstSignInWithoutUsersAccountShowsNoAccountStateWithoutRetry()
    {
        Configure(handler: StubHandler.Status(404));
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();

        page.WaitForAssertion(() => Assert.Contains("No TravelTracker profile yet", page.Markup));
        Assert.DoesNotContain("could not be loaded", page.Markup);
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Contains("Retry", StringComparison.Ordinal));
        Assert.Contains("Sign out", page.Markup);
    }

    [Fact]
    public async Task SuccessfulSavePatchesOnlyChangedOwnProfileFieldsAndRefreshesFromResponse()
    {
        HttpRequestMessage? patched = null;
        var handler = new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                patched = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"publicId":"00000000-0000-0000-0000-000000000101","role":"traveller","status":"active","username":"new_name","address":null}""") });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Profile) });
        });
        Configure(handler: handler);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.Find("button").Click();
        page.Find("#profile-address").Change("");
        page.Find("button[type='submit']").Click();
        page.WaitForAssertion(() => Assert.Contains("Your profile has been updated", page.Markup));
        Assert.Equal("PATCH", patched!.Method.Method);
        Assert.Equal("/api/v1/users/me", patched.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("address").ValueKind);
        Assert.DoesNotContain("role", body.RootElement.GetRawText());
        Assert.DoesNotContain("status", body.RootElement.GetRawText());
    }

    [Fact]
    public void ForbiddenPatchKeepsDraftVisibleAndDoesNotReportSuccess()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Patch
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Profile) }));
        Configure(handler: handler);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.Find("button").Click();
        page.Find("#profile-address").Change("draft_username");

        page.Find("button[type='submit']").Click();

        page.WaitForAssertion(() => Assert.Contains("no longer permits profile changes", page.Markup));
        Assert.Equal("draft_username", page.Find("#profile-address").GetAttribute("value"));
        Assert.DoesNotContain("Your profile has been updated", page.Markup);
    }

    [Fact]
    public void ForbiddenPatchFollowedByForbiddenReadClearsProfileAndShowsDeniedState()
    {
        var reads = 0;
        var handler = new StubHandler((request, _) => Task.FromResult(request.Method switch
        {
            var method when method == HttpMethod.Patch => new HttpResponseMessage(HttpStatusCode.Forbidden),
            _ when ++reads == 1 => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Profile) },
            _ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        }));
        Configure(handler: handler);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.Find("button").Click();
        page.Find("#profile-address").Change("draft_username");

        page.Find("button[type='submit']").Click();

        page.WaitForAssertion(() => Assert.Contains("Profile access denied", page.Markup));
        Assert.DoesNotContain("traveller_1", page.Markup);
        Assert.DoesNotContain("1 Main St", page.Markup);
        Assert.DoesNotContain("draft_username", page.Markup);
        Assert.DoesNotContain("Edit profile", page.Markup);
    }

    [Fact]
    public void ValidationKeepsFormAndDoesNotSendAnOverlongAddress()
    {
        var handler = StubHandler.Status(200, Profile);
        Configure(handler: handler);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.Find("button").Click();
        page.Find("#profile-address").Change(new string('x', 501));
        page.Find("button[type='submit']").Click();
        Assert.Contains("500 characters or fewer", page.Markup);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
    }

    [Fact]
    public void BannedProfileIsVisibleButReadOnly()
    {
        Configure(handler: StubHandler.Status(200, Profile.Replace("active", "banned")));
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        Assert.Contains("Read-only profile", page.Markup);
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Contains("Edit profile", StringComparison.Ordinal));
    }

    [Fact]
    public void DeactivatedProfileExplainsTheAccountStatusInsteadOfPermission()
    {
        Configure(handler: StubHandler.Status(200, Profile.Replace("active", "deactivated")));
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        Assert.Contains("Account deactivated", page.Markup);
        Assert.Contains("Deactivated", page.Markup);
        Assert.DoesNotContain("does not have permission to edit", page.Markup);
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Contains("Edit profile", StringComparison.Ordinal));
    }

    static void FillPasswordForm(IRenderedComponent<Account> page, string current, string next, string confirm)
    {
        page.Find("#password-current").Change(current);
        page.Find("#password-new").Change(next);
        page.Find("#password-confirm").Change(confirm);
    }

    static void SubmitPasswordForm(IRenderedComponent<Account> page) =>
        page.FindAll("button").Single(button => button.TextContent.Trim() == "Change password").Click();

    [Fact]
    public void PasswordFormIsOfferedOnlyWithTheWriteScope()
    {
        Configure();
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5), "users.read");
        var page = Render<Account>();
        page.WaitForAssertion(() => Assert.Contains("traveller_1", page.Markup));
        Assert.DoesNotContain("Change password", page.Markup);
    }

    [Fact]
    public void PasswordChangeIsReportedOnlyAfterTheServerConfirms()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>();
        string? posted = null;
        var handler = new StubHandler(async (request, token) =>
        {
            if (request.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Profile) };
            posted = await request.Content!.ReadAsStringAsync(token);
            return await release.Task;
        });
        Configure(handler: handler);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.WaitForAssertion(() => Assert.Contains("traveller_1", page.Markup));
        FillPasswordForm(page, "old secret", "new secret", "new secret");

        SubmitPasswordForm(page);

        page.WaitForAssertion(() => Assert.NotNull(posted));
        Assert.DoesNotContain("has been changed", page.Markup);
        release.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        page.WaitForAssertion(() => Assert.Contains("Your password has been changed", page.Markup));
        Assert.Contains("\"currentPassword\":\"old secret\"", posted);
        Assert.DoesNotContain("old secret", page.Markup);
        Assert.DoesNotContain("new secret", page.Markup);
    }

    [Fact]
    public void WrongCurrentPasswordShowsServerMessageAndKeepsTypedProfileDraft()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"errors":{"currentPassword":["The current password is not correct."]}}""", System.Text.Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Profile) }));
        Configure(handler: handler);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.WaitForAssertion(() => Assert.Contains("traveller_1", page.Markup));
        page.Find("button").Click();
        page.Find("#profile-address").Change("draft address");
        FillPasswordForm(page, "wrong secret", "new secret", "new secret");

        SubmitPasswordForm(page);

        page.WaitForAssertion(() => Assert.Contains("The current password is not correct.", page.Markup));
        Assert.Contains("draft address", page.Markup);
        Assert.DoesNotContain("Your password has been changed", page.Markup);
    }

    [Fact]
    public void ServerFailureIsNeverReportedAsSuccess()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Profile) }));
        Configure(handler: handler);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.WaitForAssertion(() => Assert.Contains("traveller_1", page.Markup));
        FillPasswordForm(page, "old secret", "new secret", "new secret");

        SubmitPasswordForm(page);

        page.WaitForAssertion(() => Assert.Contains("role=\"alert\"", page.Markup));
        Assert.DoesNotContain("has been changed", page.Markup);
    }

    [Fact]
    public void MismatchedConfirmationIsRejectedBeforeAnyRequest()
    {
        var posts = 0;
        var handler = new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) posts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Profile) });
        });
        Configure(handler: handler);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.WaitForAssertion(() => Assert.Contains("traveller_1", page.Markup));
        FillPasswordForm(page, "old secret", "new secret", "new secre");

        SubmitPasswordForm(page);

        page.WaitForAssertion(() => Assert.Contains("do not match", page.Markup));
        Assert.Equal(0, posts);
    }
}
