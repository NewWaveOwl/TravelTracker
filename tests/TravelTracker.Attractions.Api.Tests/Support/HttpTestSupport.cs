using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TravelTracker.Attractions.Api.Tests;

/**
 * Checks shared creation and validation HTTP contracts.
 */
internal static class HttpTestSupport
{
    /**
     * Checks creation and reads the saved record at its Location.
     * @param client The isolated test client.
     * @param response The creation response.
     * @return The saved JSON record.
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

    /**
     * Checks the standard ASP.NET Core validation response.
     * @param response The rejected request's response.
     * @return Completion after checking status and field errors.
     */
    public static async Task AssertValidationAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").EnumerateObject().Any());
    }
}
