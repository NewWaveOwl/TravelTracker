using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Accommodations;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Contract tests exercise the frontend HTTP boundary without calling the service or database. */
public sealed class AccommodationsClientTests
{
    static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000321");
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-08T10:00:00Z"));
    readonly SwitchableAuthentication auth;

    public AccommodationsClientTests()
    {
        auth = new(new SessionRegistry(clock));
        auth.SignIn("test-token", clock.GetUtcNow().AddMinutes(10));
        ((ClaimsIdentity)auth.User.Identity!).AddClaim(new Claim(AccessTokenClaims.GrantedScope, "catalogue.read catalogue.write"));
    }

    AccommodationsClient Client(StubHandler handler) => new(new ProtectedClient(auth,
        new ServiceDirectory(new Dictionary<string, string> { ["Accommodations"] = "http://localhost:5106" }),
        new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock,
        NullLogger<ProtectedClient>.Instance, auth.Sessions));

    [Fact]
    public async Task SearchEncodesOneLiteralNameQueryAndReturnsPublicDtos()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("/api/v1/accommodations?name=Toronto%20%26%20Rye", request.RequestUri!.PathAndQuery);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { new AccommodationDto(Id, "Bay Hotel", "Toronto", 45.5m, -73.5m, "shared") }) });
        });

        var result = await Client(handler).SearchAsync(" Toronto & Rye ");

        Assert.Equal("Bay Hotel", Assert.Single(result).Name);
    }

    [Fact]
    public async Task SearchBlankNameBrowsesWithoutQuery()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("/api/v1/accommodations", request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<AccommodationDto>()) });
        });

        Assert.Empty(await Client(handler).SearchAsync("  "));
    }

    [Fact]
    public async Task GetUsesPublicGuidAndReturnsVisibleDetail()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal($"/api/v1/accommodations/{Id:D}", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new AccommodationDto(Id, "Bay Hotel", "Toronto", 45.5m, -73.5m, "private")) });
        });

        var result = await Client(handler).GetAsync(Id);

        Assert.Equal(Id, result.AccommodationId);
    }

    [Fact]
    public async Task CreateSendsOnlyWritableFieldsAndUsesConfirmedPublicId()
    {
        Dictionary<string, JsonElement>? body = null;
        var handler = new StubHandler(async (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v1/accommodations", request.RequestUri!.AbsolutePath);
            body = await request.Content!.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new AccommodationDto(Id, "Bay Hotel", "Toronto", 45.5m, -73.5m, "private")),
            };
        });

        var result = await Client(handler).CreateAsync(new("Bay Hotel", "Toronto", 45.5m, -73.5m, "private"));

        Assert.Equal(Id, result.AccommodationId);
        Assert.Equal(new[] { "address", "lat", "lon", "name", "visibility" }, body!.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("owner", body.Keys);
        Assert.DoesNotContain("userId", body.Keys);
        Assert.DoesNotContain("role", body.Keys);
        Assert.Equal("private", body["visibility"].GetString());
    }

    [Theory]
    [InlineData("/api/v1/accommodations?name=x&name=y")]
    [InlineData("/api/v1/accommodations?name=x#fragment")]
    [InlineData("/api/v1/accommodations?next=https://evil.test")]
    [InlineData("/api/v1/accommodations?name=%XX")]
    public void ProtectedDestinationRejectsUnsupportedAccommodationFilters(string path)
    {
        var directory = new ServiceDirectory(new Dictionary<string, string> { ["Accommodations"] = "https://accommodations.example" });

        Assert.Throws<ApiError>(() => directory.ProtectedUrlFor("Accommodations", path));
    }
}
