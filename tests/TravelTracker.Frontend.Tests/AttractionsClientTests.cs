using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

public sealed class AttractionsClientTests
{
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-04T12:00:00Z"));
    readonly SwitchableAuthentication auth;
    static readonly Guid Id = Guid.Parse("019ac54b-3000-7000-8000-000000000043");
    static readonly AttractionDto Original = new(Id, "Museum", "Art", 4.7m, "100 Museum St", 45.5m, -73.5m, 120);

    public AttractionsClientTests() => auth = new(new(clock));

    AttractionsClient Client(StubHandler handler)
    {
        auth.SignIn("test-session-token", clock.GetUtcNow().AddMinutes(5));
        return new(new ProtectedClient(auth, new ServiceDirectory(new Dictionary<string, string> { ["Attractions"] = "http://localhost:5104" }),
            new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, auth.Sessions));
    }

    [Fact]
    public async Task SearchEncodesLiteralNameAsOneParameterAndPreservesServerOrder()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = JsonContent.Create(new[] { Original, Original with { AttractionId = Guid.NewGuid(), Name = "Z Museum" } }) }));
        var result = await Client(handler).SearchAsync(" Musée & ?name=x#rail% ");
        Assert.Equal("?name=Mus%C3%A9e%20%26%20%3Fname%3Dx%23rail%25", handler.LastRequest!.RequestUri!.Query);
        Assert.Equal("Bearer test-session-token", handler.LastRequest.Headers.Authorization!.ToString());
        Assert.Equal(new[] { "Museum", "Z Museum" }, result.Select(r => r.Name));
    }

    [Fact]
    public async Task CreateSearchEditAndReloadUseOnlyPublicHttpContracts()
    {
        AttractionDto? saved = null;
        var methods = new List<HttpMethod>();
        var handler = new StubHandler(async (request, token) =>
        {
            methods.Add(request.Method);
            if (request.Method == HttpMethod.Post)
            {
                var json = await request.Content!.ReadFromJsonAsync<JsonElement>(token);
                Assert.False(json.TryGetProperty("attractionId", out _));
                Assert.False(json.TryGetProperty("durationApproximation", out _));
                saved = Original with { Name = json.GetProperty("name").GetString()! };
            }
            if (request.Method == HttpMethod.Patch)
            {
                var json = await request.Content!.ReadFromJsonAsync<JsonElement>(token);
                Assert.Equal(new[] { "description", "rating" }, json.EnumerateObject().Select(p => p.Name));
                Assert.Equal(JsonValueKind.Null, json.GetProperty("description").ValueKind);
                Assert.Equal(JsonValueKind.Null, json.GetProperty("rating").ValueKind);
                saved = saved! with { Description = null, Rating = null };
            }
            var collection = request.RequestUri!.AbsolutePath.EndsWith("attractions") && request.Method == HttpMethod.Get;
            return new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK)
            { Content = collection ? JsonContent.Create(new[] { saved! }) : JsonContent.Create(saved) };
        });
        var client = Client(handler);
        var created = await client.CreateAsync(new() { ["name"] = "Museum", ["lat"] = 45.5m, ["lon"] = -73.5m, ["estimatedVisitMinutes"] = 120 });
        Assert.Equal(Id, created.AttractionId);
        Assert.Single(await client.SearchAsync("museum"));
        await client.UpdateAsync(Id, new() { ["description"] = null, ["rating"] = null });
        var reloaded = await client.GetAsync(Id);
        Assert.Null(reloaded.Description);
        Assert.Null(reloaded.Rating);
        Assert.Equal(Original.Address, reloaded.Address);
        Assert.Equal(120, reloaded.EstimatedVisitMinutes);
        Assert.Equal(new[] { HttpMethod.Post, HttpMethod.Get, HttpMethod.Patch, HttpMethod.Get }, methods);
    }

    [Fact]
    public async Task ValidationReturnsKnownFieldErrorsWithoutProviderDiagnostics()
    {
        var handler = StubHandler.Status(400, """{"detail":"private-provider-detail","errors":{"Rating":["Use 0 to 5."],"unknown":["internal detail"]}}""");
        var error = await Assert.ThrowsAsync<ApiError>(() => Client(handler).UpdateAsync(Id, new() { ["rating"] = 9 }));
        Assert.Equal(FailureKind.Validation, error.Kind);
        Assert.Equal("Use 0 to 5.", Assert.Single(error.Errors["rating"]));
        Assert.False(error.Errors.ContainsKey("unknown"));
        Assert.DoesNotContain("private-provider-detail", error.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"Invalid\"")]
    [InlineData("not json")]
    public async Task UnexpectedValidationBodyStillProducesAUsableApiError(string body)
    {
        var error = await Assert.ThrowsAsync<ApiError>(() => Client(StubHandler.Status(400, body)).CreateAsync(new()));
        Assert.Equal(FailureKind.Validation, error.Kind);
        Assert.Empty(error.Errors);
    }

    [Theory]
    [InlineData(401, FailureKind.Permission)]
    [InlineData(403, FailureKind.Permission)]
    [InlineData(503, FailureKind.Unavailable)]
    public async Task RejectedWritesNeverClaimSuccessOrRetry(int status, FailureKind kind)
    {
        var calls = 0;
        var handler = new StubHandler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)); });
        var error = await Assert.ThrowsAsync<ApiError>(() => Client(handler).CreateAsync(new() { ["name"] = "Museum" }));
        Assert.Equal(kind, error.Kind);
        Assert.Equal(1, calls);
        Assert.Equal(status != 401, auth.Sessions.IsActive(auth.User));
    }

    [Fact]
    public async Task AWriteResponseAfterSignOutIsDiscarded()
    {
        var handler = new StubHandler((_, _) =>
        {
            auth.SignOut();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(Original) });
        });
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiError>(() => Client(handler).CreateAsync(new()))).Status);
    }

    [Fact]
    public async Task UnconfirmedNetworkSaveWarnsAboutDuplicatesWithoutRetrying()
    {
        var calls = 0;
        var handler = new StubHandler((_, _) => { calls++; throw new HttpRequestException("secret provider diagnostic"); });
        var error = await Assert.ThrowsAsync<ApiError>(() => Client(handler).CreateAsync(new()));
        Assert.Equal(FailureKind.Network, error.Kind);
        Assert.Contains("before retrying", error.Message);
        Assert.Equal(1, calls);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Theory]
    [InlineData("/api/v1/attractions?name=ok&name=evil")]
    [InlineData("/api/v1/attractions?name=ok#fragment")]
    [InlineData("/api/v1/attractions?next=https://evil.test")]
    [InlineData("/api/v1/attractions?name=%XX")]
    public void TokenDestinationsRejectAmbiguousQueries(string path)
    {
        var directory = new ServiceDirectory(new Dictionary<string, string> { ["Attractions"] = "https://attractions.example" });
        Assert.Throws<ApiError>(() => directory.ProtectedUrlFor("Attractions", path));
    }
}
