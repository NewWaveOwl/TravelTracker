using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TravelTracker.Attractions.Api.Access;

namespace TravelTracker.Attractions.Api.Tests;

/** Verifies the public-ID Users lookup over the authenticated backend HTTP contract. */
public sealed class ScopedUsersAccessTests
{
    [Theory]
    [InlineData("active")]
    [InlineData("missing")]
    [InlineData("mismatched")]
    [InlineData("unavailable")]
    public async Task ResolvesValidatedIdentityAndRequiresMatchingLiveStanding(string scenario)
    {
        using var handler = new UsersHandler(scenario);
        using var http = new HttpClient(handler);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Users:InternalBaseUrl"] = "https://users.example.test",
            ["Users:ServiceAccessToken"] = "backend-token"
        }).Build();
        var client = new UsersAccessClient(http, configuration, NullLogger<UsersAccessClient>.Instance);

        var result = await client.ResolveAsync("https://issuer.example.test", "validated-subject", CancellationToken.None);

        if (scenario == "active") Assert.Equal(new UserAccessRecord("active", "traveller"), result);
        else if (scenario == "missing") Assert.Equal("deactivated", result?.Status);
        else Assert.Null(result);
        Assert.Equal(scenario == "missing" ? 1 : 2, handler.CallCount);
    }

    private sealed class UsersHandler(string scenario) : HttpMessageHandler
    {
        private readonly Guid userId = Guid.CreateVersion7();
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Assert.Equal("users.example.test", request.RequestUri?.Host);
            Assert.Equal("Bearer backend-token", request.Headers.Authorization?.ToString());
            if (CallCount == 1)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/internal/users/identity", request.RequestUri?.AbsolutePath);
                var identity = await request.Content!.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
                Assert.Equal("https://issuer.example.test", identity?["issuer"]);
                Assert.Equal("validated-subject", identity?["subject"]);
                return scenario == "missing" ? new(HttpStatusCode.NotFound) :
                    new(HttpStatusCode.OK) { Content = JsonContent.Create(new { userId }) };
            }

            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/internal/users/{userId:D}/access", request.RequestUri?.AbsolutePath);
            return scenario == "unavailable" ? new(HttpStatusCode.ServiceUnavailable) :
                new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        userId = scenario == "mismatched" ? Guid.NewGuid() : userId,
                        status = "active",
                        role = "traveller"
                    })
                };
        }
    }
}
