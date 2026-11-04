using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Attractions.Api.Access;
using TravelTracker.Attractions.Api.Services;
using TravelTracker.Attractions.Domain.Models;

namespace TravelTracker.Attractions.Api.Tests;

/**
 * Exercises review regressions using EF InMemory for sequential application behavior only.
 */
public sealed class ReviewRegressionTests
{
    private static readonly Guid SharedId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData("", "alice")]
    [InlineData("https://issuer.example.test", "")]
    [InlineData("https://issuer.example.test", " ")]
    public async Task ListWithIncompleteIdentity_ReturnsNoRecords(string issuer, string subject)
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var caller = scope.ServiceProvider.GetRequiredService<CallerContext>();
        caller.Issuer = issuer;
        caller.Subject = subject;
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IAttractionService>().GetAllAsync());
    }

    [Fact]
    public async Task UsersRouteNotFound_IsUnavailableInsteadOfAccountDenialAndDoesNotWrite()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        factory.Users!.ForcedResponseCode = HttpStatusCode.NotFound;
        using var read = await client.GetAsync("/api/v1/attractions");
        using var write = await client.PostAsJsonAsync("/api/v1/attractions", new { name = "Must not persist", lat = 0, lon = 0, estimatedVisitMinutes = 1 });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, read.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, write.StatusCode);
        factory.Users.ForcedResponseCode = null;
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions?name=Must%20not%20persist"))!);
    }

    [Theory]
    [InlineData("{\"name\":\"A\",\"Name\":\"B\"}")]
    [InlineData("{\"name\":\"A\",\"name\":\"B\"}")]
    [InlineData("{\"rating\":1,\"RATING\":2}")]
    [InlineData("{\"description\":null,\"DESCRIPTION\":\"B\"}")]
    public async Task DuplicatePatchFields_AreRejectedWithoutChangingRecord(string body)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var url = $"/api/v1/attractions/{SharedId}";
        var before = await client.GetStringAsync(url);
        using var response = await client.PatchAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Duplicate field", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await client.GetStringAsync(url));
    }

    [Fact]
    public async Task BothUpdatePaths_DenyVisibleUnwritableSharedRecords()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var caller = scope.ServiceProvider.GetRequiredService<CallerContext>();
        caller.Issuer = ApiFactory.Issuer;
        caller.Subject = "alice";
        var service = scope.ServiceProvider.GetRequiredService<IAttractionService>();
        var before = await service.GetByIdAsync(SharedId);
        Assert.NotNull(before);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateAsync(SharedId, Values()));
        using var patch = JsonDocument.Parse("{\"name\":\"Denied\"}");
        Assert.True((await service.UpdatePartialAsync(SharedId, patch.RootElement)).Forbidden);
        Assert.Equal(before.Name, (await service.GetByIdAsync(SharedId))!.Name);
    }

    [Fact]
    public async Task BothUpdatePaths_HideOtherUsersPrivateRecords()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        factory.PrepareScope(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IAttractionService>();
        var saved = await service.CreateAsync(Values());
        scope.ServiceProvider.GetRequiredService<CallerContext>().Subject = "bob";
        Assert.Null(await service.UpdateAsync(saved.AttractionId.PublicId, Values()));
        using var patch = JsonDocument.Parse("{\"name\":\"Denied\"}");
        Assert.True((await service.UpdatePartialAsync(saved.AttractionId.PublicId, patch.RootElement)).NotFound);
    }

    /**
     * Provides valid independent writable values for authorization scenarios.
     * @return An attraction without server-issued identity.
     */
    private static Attraction Values() => new() { Name = "Denied", Address = "A", Latitude = 0, Longitude = 0, EstimatedVisitMinutes = 1 };
}
