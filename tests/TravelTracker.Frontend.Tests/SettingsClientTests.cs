using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Settings;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

public sealed class SettingsClientTests
{
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-06T10:00:00Z"));
    readonly SwitchableAuthentication auth;
    static readonly SettingsDto Saved = new("dark", 45.5m, -73.5m, 5, "train", "America/Montreal");

    public SettingsClientTests() => auth = new(new(clock));

    SettingsClient Client(HttpMessageHandler handler)
    {
        auth.SignIn("test-session-token", clock.GetUtcNow().AddMinutes(5));
        return new(new ProtectedClient(auth, new ServiceDirectory(new Dictionary<string, string> { ["Settings"] = "http://localhost:5107" }),
            new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, auth.Sessions));
    }

    static HttpResponseMessage Reply(HttpStatusCode status, object? body = null, string? etag = null)
    {
        var response = new HttpResponseMessage(status) { Content = body is null ? null : JsonContent.Create(body) };
        if (etag is not null) response.Headers.TryAddWithoutValidation("ETag", etag);
        return response;
    }

    [Fact]
    public async Task GetReadsSavedValuesWithTheirEntityTagAndSendsOnlyTheBearer()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Reply(HttpStatusCode.OK, new SettingsDto("system", 0, 0, 2, "car", "UTC"), "\"0\"")));
        var revision = await Client(handler).GetAsync();
        Assert.Equal(new SettingsDto("system", 0, 0, 2, "car", "UTC"), revision.Settings);
        Assert.Equal("\"0\"", revision.ETag);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/api/v1/settings", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer test-session-token", handler.LastRequest.Headers.Authorization!.ToString());
        Assert.False(handler.LastRequest.Headers.Contains("If-Match"));
    }

    [Fact]
    public async Task SaveReplacesOnlyPreferenceFieldsAndEchoesTheRevisionItRead()
    {
        JsonElement? sent = null;
        var handler = new StubHandler(async (request, token) =>
        {
            sent = await request.Content!.ReadFromJsonAsync<JsonElement>(token);
            return Reply(HttpStatusCode.OK, Saved, "\"4\"");
        });
        var revision = await Client(handler).SaveAsync(Saved, "\"3\"");
        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        Assert.Equal("\"3\"", handler.LastRequest.Headers.GetValues("If-Match").Single());
        Assert.Equal("\"4\"", revision.ETag);
        Assert.Equal(Saved, revision.Settings);
        Assert.Equal(new[] { "theme", "defaultMapLatitude", "defaultMapLongitude", "defaultMapZoom", "preferredTransportMode", "timeZone" }, sent!.Value.EnumerateObject().Select(property => property.Name));
    }

    [Theory]
    [InlineData(412, FailureKind.Unexpected, "These settings changed since you loaded them")]
    [InlineData(428, FailureKind.Unexpected, "must be reloaded before saving")]
    [InlineData(403, FailureKind.Permission, "does not allow changing settings")]
    [InlineData(503, FailureKind.Unavailable, "temporarily unavailable")]
    public async Task RejectedSavesCarryUsefulGuidanceAndAreNotRetried(int status, FailureKind kind, string guidance)
    {
        var calls = 0;
        var handler = new StubHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(Reply((HttpStatusCode)status));
        });
        var error = await Assert.ThrowsAsync<ApiError>(() => Client(handler).SaveAsync(Saved, "\"3\""));
        Assert.Equal(kind, error.Kind);
        Assert.Equal(status, error.Status);
        Assert.Contains(guidance, error.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ValidationKeepsServiceFieldErrorsByJsonName()
    {
        var handler = StubHandler.Status(400, """{"errors":{"defaultMapLatitude":["Latitude must be between -90 and 90."],"detail":["internal"]}}""");
        var error = await Assert.ThrowsAsync<ApiError>(() => Client(handler).SaveAsync(Saved, "\"3\""));
        Assert.Equal(FailureKind.Validation, error.Kind);
        Assert.Equal("Latitude must be between -90 and 90.", Assert.Single(error.Errors["defaultMapLatitude"]));
        Assert.False(error.Errors.ContainsKey("detail"));
        Assert.Equal("Check the highlighted settings. Your edits are kept.", error.Message);
    }

    [Fact]
    public async Task ExpiredOrRejectedTokenRevokesTheSessionAndStopsSaves()
    {
        var handler = StubHandler.Status(401);
        var error = await Assert.ThrowsAsync<ApiError>(() => Client(handler).SaveAsync(Saved, "\"3\""));
        Assert.Equal(FailureKind.Permission, error.Kind);
        Assert.Equal(401, error.Status);
        Assert.Equal("Your session was rejected. Sign in again.", error.Message);
        Assert.False(auth.Sessions.IsActive(auth.User));
    }

    [Fact]
    public async Task UnconfirmedNetworkSaveAsksForReloadWithoutRetryingOrLeakingDiagnostics()
    {
        var calls = 0;
        var handler = new StubHandler((_, _) =>
        {
            calls++;
            throw new HttpRequestException("secret transport diagnostic");
        });
        var error = await Assert.ThrowsAsync<ApiError>(() => Client(handler).SaveAsync(Saved, "\"3\""));
        Assert.Equal(FailureKind.Network, error.Kind);
        Assert.Contains("could not be confirmed", error.Message);
        Assert.DoesNotContain("secret", error.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ReadWithoutEntityTagCannotBeSaved()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Reply(HttpStatusCode.OK, Saved)));
        var error = await Assert.ThrowsAsync<ApiError>(() => Client(handler).GetAsync());
        Assert.Equal(FailureKind.Unexpected, error.Kind);
    }
}
