using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using TravelTracker.Transport.Api.Tests.Support;

namespace TravelTracker.Transport.Api.Tests;

/**
 * Verifies transport entry creation, lookup and input validation through HTTP.
 */
public sealed class TransportModeTests
{
    [Fact]
    public async Task ActiveTravellerCanReadAndUsersReceivesOnlyValidatedIdentityUsingServiceCredential()
    {
        using var factory = new ApiFactory { SeedExamples = false };
        factory.Users.Role = "traveller";
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/transport-modes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, factory.Users.Calls);
        Assert.Equal(ApiFactory.Issuer, factory.Users.LastIssuer);
        Assert.Equal("test-traveller", factory.Users.LastSubject);
        Assert.All(factory.Users.BearerTokens, token => Assert.Equal(ApiFactory.UsersServiceToken, token));
        Assert.Equal(["/internal/users/identity", $"/internal/users/{UsersAccessBackchannelHandler.UserId:D}/access"], factory.Users.Paths);
    }

    [Theory]
    [InlineData("banned", false)]
    [InlineData("deactivated", false)]
    [InlineData("unknown", true)]
    public async Task NonActiveAccountCannotReadCatalogue(string status, bool unknownIdentity)
    {
        using var factory = new ApiFactory { SeedExamples = false };
        factory.Users.Status = status;
        factory.Users.UnknownIdentity = unknownIdentity;
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/transport-modes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("active", "administrator", false, HttpStatusCode.Created)]
    [InlineData("active", "traveller", false, HttpStatusCode.Forbidden)]
    [InlineData("banned", "administrator", false, HttpStatusCode.Forbidden)]
    [InlineData("deactivated", "administrator", false, HttpStatusCode.Forbidden)]
    [InlineData("active", "administrator", true, HttpStatusCode.Forbidden)]
    public async Task CreateModeRequiresActiveAdministratorFromUsersNotToken(string status, string liveRole,
        bool unknownIdentity, HttpStatusCode expectedStatus)
    {
        using var factory = new ApiFactory { SeedExamples = false };
        factory.Users.Status = status;
        factory.Users.Role = liveRole;
        factory.Users.UnknownIdentity = unknownIdentity;
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.CreateToken("catalogue.write", role: "administrator"));

        using var response = await client.PostAsJsonAsync("/api/v1/transport-modes",
            new { code = "plane", name = "Policy Test Airline" });

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus == HttpStatusCode.Created)
        {
            using var saved = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(saved.RootElement.TryGetProperty("role", out _));
            Assert.False(saved.RootElement.TryGetProperty("status", out _));
            Assert.False(saved.RootElement.TryGetProperty("ownerSubject", out _));
            Assert.False(saved.RootElement.TryGetProperty("issuer", out _));
            Assert.False(saved.RootElement.TryGetProperty("subject", out _));
            Assert.Equal("administrator", factory.Users.Role);
            Assert.Equal(ApiFactory.Issuer, factory.Users.LastIssuer);
            Assert.Equal("test-traveller", factory.Users.LastSubject);
            Assert.All(factory.Users.BearerTokens, token => Assert.Equal(ApiFactory.UsersServiceToken, token));
        }
        else
        {
            factory.Users.Status = "active";
            factory.Users.Role = "traveller";
            factory.Users.UnknownIdentity = false;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                factory.CreateToken("catalogue.read", role: "administrator"));
            var modes = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/transport-modes");
            Assert.Empty(modes!);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateModeUsersFailureFailsClosedAndPersistsNothing(bool invalidResponse)
    {
        using var factory = new ApiFactory { SeedExamples = false };
        factory.Users.Unavailable = !invalidResponse;
        factory.Users.InvalidResponse = invalidResponse;
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/transport-modes",
            new { code = "plane", name = "Must Not Persist" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        factory.Users.Unavailable = false;
        factory.Users.InvalidResponse = false;
        var modes = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/transport-modes");
        Assert.Empty(modes!);
    }

    /**
     * Reads a seeded transport entry.
     * @return Completion after checking the seed.
     */
    [Fact]
    public async Task GetModeById_KnownEntryReturnsSeed()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var saved = await client.GetFromJsonAsync<JsonElement>("/api/v1/transport-modes/00000000-0000-0000-0000-000000000101");
        Assert.Equal("bus", saved.GetProperty("code").GetString());
        Assert.Equal("Example Bus", saved.GetProperty("name").GetString());
    }

    /**
     * Returns 404 for an absent transport entry.
     * @return Completion after checking the lookup response.
     */
    [Fact]
    public async Task GetModeById_UnknownEntryReturns404()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/transport-modes/00000000-0000-0000-0000-000000000999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /**
     * Creates a named entry for each supported code and reads its Location.
     * @param code One of the six permitted transport codes.
     * @return Completion after checking creation and lookup.
     */
    [Theory]
    [InlineData("car")]
    [InlineData("bus")]
    [InlineData("train")]
    [InlineData("pedestrian")]
    [InlineData("bike")]
    [InlineData("plane")]
    public async Task CreateMode_SupportedCodeReturns201AndSavedLocation(string code)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/transport-modes",
            new { code, name = " Example Airline ", description = "Manual transport entry." });
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.NotEqual(Guid.Empty, saved.GetProperty("transportModeId").GetGuid());
        Assert.Equal(code, saved.GetProperty("code").GetString());
        Assert.Equal("Example Airline", saved.GetProperty("name").GetString());
        Assert.Equal("Manual transport entry.", saved.GetProperty("description").GetString());
    }

    /**
     * Rejects a create body that carries a field the contract does not define, before anything is stored.
     * @return Completion after checking the rejection.
     */
    [Fact]
    public async Task CreateMode_UnknownFieldReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/transport-modes", new { code = "bus", name = "Example", status = "active" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /**
     * Rejects an unsupported transport code.
     * @return Completion after checking validation.
     */
    [Fact]
    public async Task CreateMode_UnsupportedCodeReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/transport-modes", new { code = "rocket", name = "Example" });
        await HttpTestSupport.AssertValidationAsync(response, "code");
    }

    /**
     * Rejects a code that only matches before a trailing newline.
     * @return Completion after checking validation.
     */
    [Fact]
    public async Task CreateMode_CodeWithTrailingNewlineReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/transport-modes", new { code = "car\n", name = "Example" });
        await HttpTestSupport.AssertValidationAsync(response, "code");
    }

    /**
     * Rejects a whitespace-only transport name.
     * @return Completion after checking validation.
     */
    [Fact]
    public async Task CreateMode_BlankNameReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/transport-modes", new { code = "bus", name = "   " });
        await HttpTestSupport.AssertValidationAsync(response, "name");
    }
}
