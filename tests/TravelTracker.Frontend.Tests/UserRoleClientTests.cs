using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

public sealed class UserRoleClientTests
{
    [Fact]
    public async Task SendsReviewedRoleAndReasonToConfiguredUsersServiceOnly()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-04T12:00:00Z"));
        var auth = new SwitchableAuthentication(new(clock));
        auth.SignIn("test-token", clock.GetUtcNow().AddMinutes(5));
        var id = Guid.Parse("00000000-0000-0000-0000-000000000701");
        JsonElement body = default;
        var handler = new StubHandler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal($"https://localhost:5101/api/v1/users/{id:D}/role", request.RequestUri!.AbsoluteUri);
            body = await request.Content!.ReadFromJsonAsync<JsonElement>(token);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new UserProfile(id, "user@example.test", "administrator", "active")) };
        });
        using var httpClient = new HttpClient(handler);
        var directory = new ServiceDirectory(new Dictionary<string, string> { ["Users"] = "https://localhost:5101" });
        var protectedClient = new ProtectedClient(auth, directory, new ServiceClient(httpClient, NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, auth.Sessions);
        var client = new UsersClient(protectedClient, new ConfigurationBuilder().Build());
        var profile = await client.ChangeRoleAsync(id, "administrator", "traveller", "Approved by project owner");
        Assert.Equal("administrator", profile.Role);
        Assert.Equal(new[] { "role", "expectedRole", "reason" }, body.EnumerateObject().Select(property => property.Name));
        Assert.Equal("traveller", body.GetProperty("expectedRole").GetString());
        Assert.Equal("Approved by project owner", body.GetProperty("reason").GetString());
        Assert.Equal("Bearer test-token", handler.LastRequest!.Headers.Authorization!.ToString());
    }
}
