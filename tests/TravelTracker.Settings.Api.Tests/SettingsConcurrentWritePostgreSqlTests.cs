using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Settings.Api.Tests.Support;

namespace TravelTracker.Settings.Api.Tests;

/**
 * proves If-Match compare-and-set holds when two writers really race on PostgreSQL, not only when they run one after another.
 * each iteration uses a fresh account so the races do not depend on each other.
 */
[Trait("Category", "PostgreSQL")]
public sealed class SettingsConcurrentWritePostgreSqlTests
{
    /** races per test run; each race starts both writers together behind a barrier. */
    private const int RaceIterations = 20;

    /**
     * races two replacements that carry the same If-Match value, released together by a barrier.
     * @param firstClient the first writer's authenticated client.
     * @param secondClient the second writer's authenticated client.
     * @param firstBody the first writer's preferences.
     * @param secondBody the second writer's preferences.
     * @param ifMatch the ETag both writers echo.
     * @return both responses, in the order the writers were passed in.
     */
    private static async Task<(HttpResponseMessage First, HttpResponseMessage Second)> RaceAsync(HttpClient firstClient,
        HttpClient secondClient, object firstBody, object secondBody, string ifMatch)
    {
        using var barrier = new Barrier(2);
        var first = Task.Run(() =>
        {
            barrier.SignalAndWait();
            return firstClient.PutSettingsAsync(firstBody, ifMatch);
        });
        var second = Task.Run(() =>
        {
            barrier.SignalAndWait();
            return secondClient.PutSettingsAsync(secondBody, ifMatch);
        });
        return (await first, await second);
    }

    /**
     * builds a complete preference body; only the theme and zoom vary between writers.
     * @param theme the theme to save.
     * @param zoom the default map zoom to save.
     * @return an anonymous body the settings endpoint accepts.
     */
    private static object Preferences(string theme, int zoom) => new
    {
        theme,
        defaultMapLatitude = 45.25m,
        defaultMapLongitude = -73.75m,
        defaultMapZoom = zoom,
        preferredTransportMode = "train",
        timeZone = "America/Montreal"
    };

    /**
     * asserts exactly one writer won with 200 and the other lost with 412, then checks the stored row is the winner's.
     * @param reader a client for the same account, used to read back the stored row.
     * @param race the two responses from the race.
     * @param expectedEntityTag the ETag the winning save must return.
     */
    private static async Task AssertOneWinnerAsync(HttpClient reader,
        (HttpResponseMessage First, HttpResponseMessage Second) race, string expectedEntityTag)
    {
        using var firstResponse = race.First;
        using var secondResponse = race.Second;
        var responses = new[] { firstResponse, secondResponse };
        var statuses = responses.Select(response => response.StatusCode).OrderBy(status => (int)status).ToArray();
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }, statuses);

        var winner = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(expectedEntityTag, winner.Headers.ETag?.Tag);
        var winnerBody = await winner.Content.ReadFromJsonAsync<JsonElement>();

        using var stored = await reader.GetAsync("/api/v1/settings");
        var storedBody = await stored.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);
        Assert.Equal(expectedEntityTag, stored.Headers.ETag?.Tag);
        Assert.Equal(winnerBody.GetRawText(), storedBody.GetRawText());
    }

    [Fact]
    public async Task ConcurrentReplaceWithSameIfMatch_OneWinsAndTheOtherGetsPreconditionFailed()
    {
        using var database = new PostgresTestDatabase();
        using var host = new ApiFactory(database.ConnectionString);
        for (var iteration = 0; iteration < RaceIterations; iteration++)
        {
            var subject = $"update-racer-{iteration}";
            using var seedClient = host.CreateClientFor(subject);
            using var seeded = await seedClient.PutSettingsAsync(Preferences("dark", 12), "\"0\"");
            Assert.Equal(HttpStatusCode.OK, seeded.StatusCode);

            var race = await RaceAsync(host.CreateClientFor(subject), host.CreateClientFor(subject),
                Preferences("light", 4), Preferences("system", 8), "\"1\"");
            await AssertOneWinnerAsync(seedClient, race, "\"2\"");
        }
    }

    [Fact]
    public async Task ConcurrentFirstSaveWithIfMatchZero_OneInsertsAndTheOtherGetsPreconditionFailed()
    {
        using var database = new PostgresTestDatabase();
        using var host = new ApiFactory(database.ConnectionString);
        for (var iteration = 0; iteration < RaceIterations; iteration++)
        {
            var subject = $"first-racer-{iteration}";
            using var reader = host.CreateClientFor(subject);

            var race = await RaceAsync(host.CreateClientFor(subject), host.CreateClientFor(subject),
                Preferences("light", 4), Preferences("system", 8), "\"0\"");
            await AssertOneWinnerAsync(reader, race, "\"1\"");
        }
    }
}
