using System.Net;
using System.Net.Http.Headers;
using TravelTracker.Security.Hosting;
using TravelTracker.Settings.Api.Tests.Support;

namespace TravelTracker.Settings.Api.Tests;

/**
 * verifies that unknown routes answer 404 for every caller, that the shared fallback still guards matched endpoints,
 * and that the shared host builder anchors content to the application folder.
 */
public sealed class RoutingAndHostTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("settings.read")]
    [InlineData("catalogue.read catalogue.write")]
    public async Task UnknownRoute_ReturnsNotFoundForAnyCaller(string? scopes)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", scopes);

        using var response = await client.GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("settings.read", 403)]
    [InlineData("catalogue.read", 200)]
    public async Task EndpointWithoutPolicy_IsStillGuardedByTheFallback(string? scopes, int status)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", scopes);

        using var response = await client.GetAsync("/test/fallback");

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
    }

    [Fact]
    public void ApiHostBuilder_AnchorsContentRootToTheApplicationFolder()
    {
        var builder = ApiHostBuilder.Create([]);

        Assert.Equal(
            Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(builder.Environment.ContentRootPath).TrimEnd(Path.DirectorySeparatorChar));
    }
}
