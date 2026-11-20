using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

public sealed class UsersClientTests
{
    static readonly string Profile = """{"publicId":"00000000-0000-0000-0000-000000000101","role":"traveller","status":"active","username":"traveller","address":"1 Main St"}""";

    static (UsersClient Client, StubHandler Handler) Create(int status = 200, string? body = null)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-05T15:00:00Z"));
        var sessions = new SessionRegistry(clock);
        var authentication = new SwitchableAuthentication(sessions);
        authentication.SignIn("test-access-token", clock.GetUtcNow().AddMinutes(5));
        var handler = StubHandler.Status(status, body ?? Profile);
        var service = new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance);
        var protectedClient = new ProtectedClient(authentication,
            new ServiceDirectory(new Dictionary<string, string> { ["Users"] = "https://localhost:5101" }),
            service, clock, NullLogger<ProtectedClient>.Instance, sessions);
        var configuration = new ConfigurationBuilder().Build();
        return (new UsersClient(protectedClient, configuration), handler);
    }

    [Fact]
    public async Task GetMeUsesTheFixedOwnProfilePathAndBearerToken()
    {
        var (client, handler) = Create();

        var profile = await client.GetMeAsync();

        Assert.Equal("traveller", profile!.Username);
        Assert.Equal("GET", handler.LastRequest!.Method.Method);
        Assert.Equal("/api/v1/users/me", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer test-access-token", handler.LastRequest.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task PatchUsesOnlyTheAddressAndCanClearItWithNull()
    {
        var (client, handler) = Create();
        var profile = JsonSerializer.Deserialize<SelfUserProfile>(Profile, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var updated = await client.UpdateMeAsync(profile, new UpdateSelfProfileRequest(" "));

        Assert.Equal("traveller", updated.Username);
        Assert.Equal("PATCH", handler.LastRequest!.Method.Method);
        Assert.Equal("/api/v1/users/me", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer test-access-token", handler.LastRequest.Headers.Authorization!.ToString());
        using var json = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("address").ValueKind);
    }

    [Theory]
    [InlineData(400, FailureKind.Validation)]
    [InlineData(403, FailureKind.Permission)]
    [InlineData(409, FailureKind.Unexpected)]
    public async Task PatchPreservesValidationConflictAndPermissionStatuses(int status, FailureKind kind)
    {
        var (client, _) = Create(status, """{"errors":{"username":["Already used"]}}""");
        var profile = JsonSerializer.Deserialize<SelfUserProfile>(Profile, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var error = await Assert.ThrowsAsync<ApiError>(() => client.UpdateMeAsync(profile, new UpdateSelfProfileRequest("1 New Street")));

        Assert.Equal(status, error.Status);
        Assert.Equal(kind, error.Kind);
        if (status == 400) Assert.Contains("username", error.Errors.Keys);
    }
}
