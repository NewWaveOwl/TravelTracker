using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

public sealed class ProtectedClientTests
{
    const string Path = "/api/v1/transport-modes/00000000-0000-0000-0000-000000000101";

    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));
    readonly SwitchableAuthentication authentication;

    public ProtectedClientTests() => authentication = new(new SessionRegistry(clock));

    ProtectedClient ClientFor(StubHandler handler, string serviceUrl = "http://localhost:5105") => new(
        authentication,
        new ServiceDirectory(new Dictionary<string, string> { ["Transport"] = serviceUrl }),
        new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance),
        clock,
        NullLogger<ProtectedClient>.Instance,
        authentication.Sessions);

    [Fact]
    public async Task SendsTheBearerTokenToTheConfiguredService()
    {
        authentication.SignIn("token-1", clock.GetUtcNow().AddMinutes(5));
        var handler = StubHandler.Status(200, """{"name":"Example Bus"}""");

        await ClientFor(handler).GetJsonAsync<object>("Transport", Path);

        Assert.Equal("Bearer token-1", handler.LastRequest!.Headers.Authorization!.ToString());
        Assert.Equal("http://localhost:5105/api/v1/transport-modes/00000000-0000-0000-0000-000000000101", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task RefusesWithoutSendingWhenSignedOut()
    {
        var handler = StubHandler.Status(200, "{}");

        var error = await Assert.ThrowsAsync<ApiError>(() => ClientFor(handler).GetJsonAsync<object>("Transport", Path));

        Assert.Equal(401, error.Status);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task RefusesWithoutSendingWhenTheTokenHasExpired()
    {
        authentication.SignIn("token-1", clock.GetUtcNow().AddMinutes(-1));
        var handler = StubHandler.Status(200, "{}");

        var error = await Assert.ThrowsAsync<ApiError>(() => ClientFor(handler).GetJsonAsync<object>("Transport", Path));

        Assert.Equal(401, error.Status);
        Assert.Null(handler.LastRequest);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/api/v2/transport-modes")]
    [InlineData("/api/v1/../admin")]
    [InlineData("https://evil.example/api/v1/x")]
    public async Task RejectsUnsupportedPathsBeforeAnyRequest(string path)
    {
        authentication.SignIn("token-1", clock.GetUtcNow().AddMinutes(5));
        var handler = StubHandler.Status(200, "{}");

        await Assert.ThrowsAsync<ApiError>(() => ClientFor(handler).GetJsonAsync<object>("Transport", path));

        Assert.Null(handler.LastRequest);
    }

    [Theory]
    [InlineData("http://transport.example")]
    [InlineData("https://user:pass@transport.example")]
    [InlineData("https://transport.example/?x=1")]
    public async Task RejectsInsecureOrAmbiguousServiceUrls(string serviceUrl)
    {
        authentication.SignIn("token-1", clock.GetUtcNow().AddMinutes(5));
        var handler = StubHandler.Status(200, "{}");

        await Assert.ThrowsAsync<ApiError>(() => ClientFor(handler, serviceUrl).GetJsonAsync<object>("Transport", Path));

        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task ReportsARejectedTokenAsASessionError()
    {
        authentication.SignIn("token-1", clock.GetUtcNow().AddMinutes(5));

        var error = await Assert.ThrowsAsync<ApiError>(() => ClientFor(StubHandler.Status(401)).GetJsonAsync<object>("Transport", Path));

        Assert.Equal((FailureKind.Permission, 401), (error.Kind, error.Status));
    }

    [Fact]
    public async Task ReportsMissingPermissionWithoutEndingTheSession()
    {
        authentication.SignIn("token-1", clock.GetUtcNow().AddMinutes(5));

        var error = await Assert.ThrowsAsync<ApiError>(() => ClientFor(StubHandler.Status(403)).GetJsonAsync<object>("Transport", Path));

        Assert.Equal((FailureKind.Permission, 403), (error.Kind, error.Status));
        Assert.NotNull(AccessTokenClaims.ActiveToken(authentication.User, clock.GetUtcNow()));
    }

    [Fact]
    public async Task DropsALateSuccessWhenSignOutHappensInFlight()
    {
        authentication.SignIn("token-1", clock.GetUtcNow().AddMinutes(5));
        var handler = new StubHandler((_, _) =>
        {
            authentication.SignOut();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
        });

        var error = await Assert.ThrowsAsync<ApiError>(() => ClientFor(handler).GetJsonAsync<object>("Transport", Path));

        Assert.Equal(401, error.Status);
    }

    [Fact]
    public async Task DoesNotFollowRedirectsWithTheToken()
    {
        authentication.SignIn("token-1", clock.GetUtcNow().AddMinutes(5));

        var error = await Assert.ThrowsAsync<ApiError>(() => ClientFor(StubHandler.Status(302)).GetJsonAsync<object>("Transport", Path));

        Assert.Equal(FailureKind.Unexpected, error.Kind);
    }
}
