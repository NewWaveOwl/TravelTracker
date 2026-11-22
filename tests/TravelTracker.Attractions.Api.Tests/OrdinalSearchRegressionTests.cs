using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Attractions.Data;

namespace TravelTracker.Attractions.Api.Tests;

/** Proves that HTTP search preserves ordinal matching, ordering and private visibility across providers. */
public sealed class OrdinalSearchRegressionTests
{
    /**
     * Exercises the production query through the explicitly selected InMemory test adapter.
     * @param cultureName The request execution culture, including Turkish casing rules.
     * @return Completion after verifying the public search contract.
     */
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    public async Task InMemory_SearchAndOrderingRemainOrdinal(string cultureName)
    {
        using var factory = new ApiFactory { SeedExamples = false };
        using var scope = factory.Services.CreateScope();
        Assert.True(scope.ServiceProvider.GetRequiredService<AttractionsDbContext>().Database.IsInMemory());
        await AssertOrdinalSearchAsync(factory, cultureName);
    }

    /**
     * Runs the same HTTP contract against the existing isolated PostgreSQL fixture, without fallback.
     * @param cultureName The request execution culture.
     * @return Completion after verifying the relational host's public search contract.
     */
    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    public async Task PostgreSql_SearchAndOrderingRemainOrdinal(string cultureName)
    {
        using var factory = new ApiFactory(usePostgres: true) { SeedExamples = false };
        await AssertOrdinalSearchAsync(factory, cultureName);
    }

    /**
     * Checks Turkish-sensitive matching, punctuation and accented ordering, UUID ties and owner isolation over HTTP.
     * @param factory The isolated host with an explicitly selected persistence provider.
     * @param cultureName The culture flowing into requests without changing process-wide defaults.
     * @return Completion after asserting search results and restoring the caller's culture.
     */
    private static async Task AssertOrdinalSearchAsync(ApiFactory factory, string cultureName)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            using var client = factory.CreateClient();
            factory.Server.PreserveExecutionContext = true;
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "alice"));
            var first = await CreateAsync(client, "Istanbul");
            var second = await CreateAsync(client, "istanbul");
            var punctuation = await CreateAsync(client, "_Pier");
            var zebra = await CreateAsync(client, "Zebra");
            var accented = await CreateAsync(client, "\u00c9comuseum");

            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "bob"));
            await CreateAsync(client, "Istanbul hidden");
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "alice"));

            var caseTies = new[] { first, second }.Order().ToArray();
            var matches = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions?name=%20IST%20");
            Assert.NotNull(matches);
            Assert.Equal(caseTies, matches.Select(record => record.GetProperty("attractionId").GetGuid()).ToArray());

            var all = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions");
            Assert.NotNull(all);
            Assert.Equal(new[] { caseTies[0], caseTies[1], zebra, punctuation, accented },
                all.Select(record => record.GetProperty("attractionId").GetGuid()).ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    /**
     * Creates a private attraction through the public writable contract.
     * @param client The authenticated owner client.
     * @param name The name used by subsequent search assertions.
     * @return The server-generated public identifier of the confirmed creation.
     */
    private static async Task<Guid> CreateAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/attractions",
            new { name, lat = 45m, lon = -73m, estimatedVisitMinutes = 30 });
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        return saved.GetProperty("attractionId").GetGuid();
    }
}
