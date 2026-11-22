using System.Net;
using System.Net.Http.Headers;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Users;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Proves native file selection, confirmed photo rendering and private-state lifetime with isolated HTTP contracts. */
public sealed class ProfilePictureTests : BunitContext
{
    static readonly byte[] OldPicture = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aL1cAAAAASUVORK5CYII=");
    static readonly byte[] NewPicture = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    static readonly SelfUserProfile Profile = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "traveller", "active", "alice_morgan");
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-22T10:00:00Z"));
    readonly SwitchableAuthentication authentication;

    public ProfilePictureTests() => authentication = new(new SessionRegistry(clock));

    void Configure(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, string scopes = "users.read users.write")
    {
        authentication.SignIn("owner-token", clock.GetUtcNow().AddMinutes(5), scopes);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton(authentication.Sessions);
        Services.AddSingleton<TimeProvider>(clock);
        var client = new ProtectedClient(authentication,
            new ServiceDirectory(new Dictionary<string, string> { ["Users"] = "https://users.test" }),
            new ServiceClient(new HttpClient(new StubHandler(respond)), NullLogger<ServiceClient>.Instance), clock,
            NullLogger<ProtectedClient>.Instance, authentication.Sessions);
        Services.AddSingleton(new UsersClient(client, new ConfigurationBuilder().Build()));
        Services.AddLogging();
    }

    static HttpResponseMessage Picture(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return response;
    }

    IRenderedComponent<ProfilePicture> RenderPicture(bool editing = true, bool editable = true, SelfUserProfile? profile = null) =>
        Render<ProfilePicture>(parameters => parameters.Add(component => component.Profile, profile ?? Profile)
            .Add(component => component.Editable, editable).Add(component => component.Editing, editing));

    /**
     * Invokes the rendered native InputFile change contract with a bounded in-memory browser file.
     * @param panel The real rendered picture component.
     * @param file File supplied by the browser-selection boundary.
     * @return The asynchronous file-selection handler, allowing in-flight assertions.
     */
    static Task SelectAsync(IRenderedComponent<ProfilePicture> panel, IBrowserFile file) => panel.InvokeAsync(() =>
        panel.FindComponent<InputFile>().Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([file])));

    [Fact]
    public void MissingPictureShowsRealInitialsAndTheEditProfileHint()
    {
        Configure((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var panel = RenderPicture(editing: false);

        panel.WaitForAssertion(() => Assert.Equal("AM", panel.Find("[role=img]").TextContent));
        Assert.Contains("alice_morgan", panel.Markup);
        Assert.Contains("Add a profile picture in Edit profile.", panel.Markup);
        Assert.Empty(panel.FindAll("img, input[type=file]"));
    }

    [Fact]
    public void NativeUploadControlIsLabelledWithSupportedFormatsAndIndependentSaveHint()
    {
        Configure((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var panel = RenderPicture();
        var input = panel.Find("input[type=file]");

        Assert.Equal("Upload profile photo", input.GetAttribute("aria-label"));
        Assert.Equal("image/png,image/jpeg", input.GetAttribute("accept"));
        var hint = panel.Find($"#{input.GetAttribute("aria-describedby")}").TextContent;
        Assert.Contains("1 MiB", hint);
        Assert.Contains("512", hint);
        Assert.Contains("independently", hint);
    }

    [Fact]
    public async Task SelectingAFileKeepsOldPhotoUntilPutAndProtectedReadConfirmTheNewPhoto()
    {
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var updated = false;
        var writes = 0;
        Configure((request, _) =>
        {
            if (request.Method == HttpMethod.Put) { writes++; return completion.Task; }
            return Task.FromResult(Picture(updated ? NewPicture : OldPicture));
        });
        var panel = RenderPicture();
        var oldSource = panel.Find("img").GetAttribute("src");
        var oldInput = panel.Find("input[type=file]");
        var upload = SelectAsync(panel, new BrowserFile(NewPicture));
        panel.WaitForAssertion(() => Assert.True(panel.Find("input[type=file]").HasAttribute("disabled")));
        Assert.Equal(oldSource, panel.Find("img").GetAttribute("src"));
        Assert.DoesNotContain("Photo saved.", panel.Markup);
        await SelectAsync(panel, new BrowserFile(NewPicture));
        Assert.Equal(1, writes);

        updated = true;
        completion.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await upload;

        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(NewPicture), panel.Find("img").GetAttribute("src"));
        Assert.Contains("Photo saved.", panel.Find("[role=status]").TextContent);
        Assert.NotSame(oldInput, panel.Find("input[type=file]"));
        Assert.DoesNotContain("private-file-name", panel.Markup);
    }

    [Fact]
    public async Task CancellingProfileTextEditingDoesNotRemountAnInFlightNativePhotoSelection()
    {
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var updated = false;
        Configure((request, _) => request.Method == HttpMethod.Put ? completion.Task : Task.FromResult(Picture(updated ? NewPicture : OldPicture)));
        var panel = RenderPicture();
        var input = panel.FindComponent<InputFile>().Instance;
        var upload = SelectAsync(panel, new BrowserFile(NewPicture));

        panel.Render(parameters => parameters.Add(component => component.Editing, false));

        Assert.Same(input, panel.FindComponent<InputFile>().Instance);
        Assert.True(panel.Find("input[type=file]").HasAttribute("disabled"));
        updated = true;
        completion.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await upload;
        Assert.Empty(panel.FindAll("input[type=file]"));
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(NewPicture), panel.Find("img").GetAttribute("src"));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(413)]
    [InlineData(415)]
    [InlineData(409)]
    [InlineData(503)]
    public async Task RejectedUploadRetainsOldPhotoAndDoesNotClaimSuccess(int status)
    {
        Configure((request, _) => Task.FromResult(request.Method == HttpMethod.Get ? Picture(OldPicture) : new HttpResponseMessage((HttpStatusCode)status)));
        var panel = RenderPicture();
        var oldSource = panel.Find("img").GetAttribute("src");

        await SelectAsync(panel, new BrowserFile(NewPicture));

        Assert.Equal(oldSource, panel.Find("img").GetAttribute("src"));
        Assert.NotEmpty(panel.Find("[role=alert]").TextContent);
        Assert.DoesNotContain("Photo saved.", panel.Markup);
        Assert.False(panel.Find("input[type=file]").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(0, "image/png")]
    [InlineData(1048577, "image/png")]
    [InlineData(20, "image/svg+xml")]
    [InlineData(20, "image/gif")]
    public async Task InvalidSelectionIsRejectedBeforeUploadAndPreservesTheExistingPhoto(int length, string mediaType)
    {
        var writes = 0;
        Configure((request, _) => { if (request.Method == HttpMethod.Put) writes++; return Task.FromResult(Picture(OldPicture)); });
        var panel = RenderPicture();
        var oldSource = panel.Find("img").GetAttribute("src");

        await SelectAsync(panel, new BrowserFile(new byte[length], mediaType));

        Assert.Equal(0, writes);
        Assert.Equal(oldSource, panel.Find("img").GetAttribute("src"));
        Assert.NotEmpty(panel.Find("[role=alert]").TextContent);
    }

    [Fact]
    public async Task ConfirmedUploadWithFailedPreviewOffersAReadRetryWithoutUploadingAgain()
    {
        var writes = 0;
        var refreshFailed = true;
        Configure((request, _) =>
        {
            if (request.Method == HttpMethod.Put) { writes++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
            return Task.FromResult(writes == 0 ? Picture(OldPicture) : refreshFailed ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Picture(NewPicture));
        });
        var panel = RenderPicture();
        var oldSource = panel.Find("img").GetAttribute("src");

        await SelectAsync(panel, new BrowserFile(NewPicture));

        Assert.Contains("Photo saved.", panel.Find("[role=status]").TextContent);
        Assert.Contains("preview could not be refreshed", panel.Find("[role=alert]").TextContent);
        Assert.Equal(oldSource, panel.Find("img").GetAttribute("src"));
        refreshFailed = false;
        panel.Find("button").Click();
        panel.WaitForAssertion(() => Assert.Equal("data:image/png;base64," + Convert.ToBase64String(NewPicture), panel.Find("img").GetAttribute("src")));
        Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData("banned", "users.read users.write")]
    [InlineData("active", "users.read")]
    public void ReadOnlyAccountsCanViewTheirPictureWithoutAFileInput(string standing, string scopes)
    {
        Configure((_, _) => Task.FromResult(Picture(OldPicture)), scopes);
        var panel = RenderPicture(editable: standing == "active", profile: Profile with { Status = standing });

        Assert.Single(panel.FindAll("img"));
        Assert.Empty(panel.FindAll("input[type=file]"));
        Assert.Contains("read-only", panel.Markup);
    }

    [Fact]
    public async Task DeniedWriteKeepsAReadablePictureButRemovesUploadControls()
    {
        Configure((request, _) => Task.FromResult(request.Method == HttpMethod.Get ? Picture(OldPicture) : new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var panel = RenderPicture();
        var oldSource = panel.Find("img").GetAttribute("src");

        await SelectAsync(panel, new BrowserFile(NewPicture));

        Assert.Equal(oldSource, panel.Find("img").GetAttribute("src"));
        Assert.Empty(panel.FindAll("input[type=file]"));
        Assert.Contains("permission", panel.Find("[role=alert]").TextContent);
    }

    [Fact]
    public async Task ReadAccessLostAfterARejectedUploadClearsThePreviouslyVisiblePicture()
    {
        var reads = 0;
        Configure((request, _) => Task.FromResult(request.Method == HttpMethod.Get && ++reads == 1
            ? Picture(OldPicture) : new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var panel = RenderPicture();
        Assert.Single(panel.FindAll("img"));

        await SelectAsync(panel, new BrowserFile(NewPicture));

        Assert.Empty(panel.FindAll("img, input[type=file]"));
        Assert.Equal("false", panel.Find(".profile-picture").GetAttribute("aria-busy"));
        Assert.DoesNotContain("data:image", panel.Markup);
    }

    [Fact]
    public void DeniedReadRemovesPrivateImageAndDoesNotRemainBusy()
    {
        Configure((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var panel = RenderPicture();

        Assert.Empty(panel.FindAll("img, input[type=file]"));
        Assert.Equal("false", panel.Find(".profile-picture").GetAttribute("aria-busy"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignOutOrIdleExpiryClearsPictureAndNativeFileSelection(bool expiry)
    {
        Configure((_, _) => Task.FromResult(Picture(OldPicture)));
        var panel = RenderPicture();
        Assert.Single(panel.FindAll("img"));

        if (expiry) clock.Advance(TimeSpan.FromMinutes(5));
        else authentication.SignOut();

        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll("img, input[type=file]")));
        Assert.DoesNotContain("data:image", panel.Markup);
        Assert.Equal("TT", panel.Find("[role=img]").TextContent);
    }

    [Fact]
    public async Task NewSessionCannotDisplayALatePreviousOwnersPicture()
    {
        var oldResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Configure((request, _) => request.Headers.Authorization!.Parameter == "owner-token" ? oldResponse.Task : Task.FromResult(Picture(NewPicture)));
        var panel = RenderPicture();
        authentication.SignInAndNotify("replacement-token", clock.GetUtcNow().AddMinutes(5));
        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll("input[type=file]")));
        panel.Render(parameters => parameters.Add(component => component.Profile, Profile with { PublicId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Username = "bob_traveller" }));
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("img")));

        oldResponse.SetResult(Picture(OldPicture));
        await Task.Yield();

        panel.WaitForAssertion(() => Assert.Equal("data:image/png;base64," + Convert.ToBase64String(NewPicture), panel.Find("img").GetAttribute("src")));
        Assert.Contains("bob_traveller", panel.Find("img").GetAttribute("alt"));
    }

    [Fact]
    public async Task SignOutDuringUploadCannotPublishItsLateConfirmationOrReloadAnImage()
    {
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        Configure((request, _) =>
        {
            if (request.Method == HttpMethod.Put) return completion.Task;
            reads++;
            return Task.FromResult(Picture(OldPicture));
        });
        var panel = RenderPicture();
        var upload = SelectAsync(panel, new BrowserFile(NewPicture));
        panel.WaitForAssertion(() => Assert.True(panel.Find("input[type=file]").HasAttribute("disabled")));

        authentication.SignOut();
        completion.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await upload;

        Assert.Empty(panel.FindAll("img, input[type=file]"));
        Assert.DoesNotContain("Photo saved.", panel.Markup);
        Assert.Equal(1, reads);
    }

    /** A native browser-file contract with known bytes, independent of JavaScript or the local filesystem. */
    sealed class BrowserFile(byte[] bytes, string contentType = "image/png") : IBrowserFile
    {
        public string Name => "private-file-name.png";
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => bytes.Length;
        public string ContentType => contentType;
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            if (bytes.Length > maxAllowedSize) throw new IOException("File exceeds the browser read limit.");
            return new MemoryStream(bytes, writable: false);
        }
    }
}
