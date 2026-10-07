using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
namespace TravelTracker.Transport.Api.Tests;

/**
 * Supplies JSON HTTP helpers and assertions without external dependencies.
 */
public static class HttpTestSupport
{
    /**
     * Builds JSON request content.
     * @param json Raw JSON to send.
     * @return UTF-8 JSON request content.
     */
    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    /**
     * Reads the current collection size.
     * @param client Test HTTP client.
     * @param path Collection path.
     * @return Number of current records.
     */
    public static async Task<int> CountAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<JsonElement>(path)).GetArrayLength();

    /**
     * Checks the public validation response.
     * @param response Rejected request.
     * @return Completion after checking status and field errors.
     */
    public static async Task AssertValidationAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").EnumerateObject().Any());
    }

    /**
     * Checks creation and retrieves the saved representation.
     * @param client Test HTTP client.
     * @param response Successful creation.
     * @return The saved representation, verified at its Location.
     */
    public static async Task<JsonElement> AssertCreatedAsync(HttpClient client, HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        using var details = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.Equal(saved.GetRawText(), (await details.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        return saved;
    }
}
