using Microsoft.Extensions.Logging.Abstractions;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

public sealed class ServiceClientTests
{
    static ServiceClient ClientFor(StubHandler handler) => new(new HttpClient(handler), NullLogger<ServiceClient>.Instance);

    [Fact]
    public async Task ReadsPublicHealthJsonWithoutCredentials()
    {
        var handler = StubHandler.Status(200, """{"status":"Healthy"}""");

        var result = await ClientFor(handler).GetJsonAsync<Dictionary<string, string>>("http://service/health");

        Assert.Equal("Healthy", result!["status"]);
        Assert.Equal("application/json", handler.LastRequest!.Headers.Accept.ToString());
        Assert.Null(handler.LastRequest.Headers.Authorization);
    }

    [Theory]
    [InlineData(400, FailureKind.Validation)]
    [InlineData(422, FailureKind.Validation)]
    [InlineData(401, FailureKind.Permission)]
    [InlineData(403, FailureKind.Permission)]
    [InlineData(503, FailureKind.Unavailable)]
    [InlineData(404, FailureKind.Unexpected)]
    public async Task ClassifiesHttpFailures(int status, FailureKind expected)
    {
        var error = await Assert.ThrowsAsync<ApiError>(() => ClientFor(StubHandler.Status(status)).GetJsonAsync<object>("http://service/health"));

        Assert.Equal(expected, error.Kind);
        Assert.Equal(status, error.Status);
    }

    [Fact]
    public async Task ReportsNetworkFailure()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("offline"));

        var error = await Assert.ThrowsAsync<ApiError>(() => ClientFor(handler).GetJsonAsync<object>("http://service/health"));

        Assert.Equal(FailureKind.Network, error.Kind);
    }

    [Fact]
    public async Task ReportsMalformedResponses()
    {
        var error = await Assert.ThrowsAsync<ApiError>(() => ClientFor(StubHandler.Status(200, "invalid")).GetJsonAsync<object>("http://service/health"));

        Assert.Equal(FailureKind.Unexpected, error.Kind);
    }

    [Fact]
    public async Task PreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new StubHandler((_, token) => Task.FromCanceled<HttpResponseMessage>(token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClientFor(handler).GetJsonAsync<object>("http://service/health", cancellation.Token));
    }
}
