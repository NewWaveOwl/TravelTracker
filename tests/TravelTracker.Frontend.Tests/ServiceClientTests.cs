using Microsoft.Extensions.Logging.Abstractions;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

public sealed class ServiceClientTests
{
    static ServiceClient ClientFor(StubHandler handler) => new(new HttpClient(handler), NullLogger<ServiceClient>.Instance);

    static (ServiceClient Client, RecordingLogger<ServiceClient> Logger) RecordedClientFor(StubHandler handler)
    {
        var logger = new RecordingLogger<ServiceClient>();
        return (new ServiceClient(new HttpClient(handler), logger), logger);
    }

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
    public async Task LogsTheTransportCauseOfANetworkFailure()
    {
        var cause = new HttpRequestException("offline");
        var (client, logger) = RecordedClientFor(new StubHandler((_, _) => throw cause));

        await Assert.ThrowsAsync<ApiError>(() => client.GetJsonAsync<object>("http://service/api/v1/trips/example"));

        var entry = Assert.Single(logger.Entries);
        Assert.Same(cause, entry.Exception);
        Assert.Contains("/api/v1/trips", entry.Message);
        Assert.DoesNotContain("example", entry.Message);
    }

    [Fact]
    public async Task LogsOnlyTheThreeLeadingSegmentsOfANestedRecordRoute()
    {
        var cause = new HttpRequestException("offline");
        var (client, logger) = RecordedClientFor(new StubHandler((_, _) => throw cause));

        await Assert.ThrowsAsync<ApiError>(() => client.GetJsonAsync<object>("http://service/api/v1/users/00000000-0000-0000-0000-000000000042/role"));

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("/api/v1/users", entry.Message);
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000042", entry.Message);
        Assert.DoesNotContain("role", entry.Message);
    }

    [Fact]
    public async Task KeepsTheValidationStatusWhenTheErrorBodyCannotBeRead()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) { Content = new UnreadableContent() };
        var (client, logger) = RecordedClientFor(new StubHandler((_, _) => Task.FromResult(response)));

        var error = await Assert.ThrowsAsync<ApiError>(() => client.GetJsonAsync<object>("http://service/health"));

        Assert.Equal(FailureKind.Validation, error.Kind);
        Assert.Equal(400, error.Status);
        Assert.Empty(error.Errors);
        Assert.Contains(logger.Entries, entry => entry.Exception is HttpRequestException);
    }

    [Fact]
    public async Task ReportsMalformedResponses()
    {
        var error = await Assert.ThrowsAsync<ApiError>(() => ClientFor(StubHandler.Status(200, "invalid")).GetJsonAsync<object>("http://service/health"));

        Assert.Equal(FailureKind.Unexpected, error.Kind);
    }

    [Theory]
    [InlineData("x-unsupported-charset")]
    [InlineData("utf-7")]
    public async Task ReportsSuccessBodiesWithUnreadableCharsetAsUnexpected(string charset)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = charset };
        var handler = new StubHandler((_, _) => Task.FromResult(response));

        var error = await Assert.ThrowsAsync<ApiError>(() => ClientFor(handler).GetJsonAsync<object>("http://service/health"));

        Assert.Equal(FailureKind.Unexpected, error.Kind);
    }

    [Fact]
    public async Task PreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHandler(async (_, token) =>
        {
            // the stub only ends when the client cancels, so the cancel really flows through the request
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });
        var pending = ClientFor(handler).GetJsonAsync<object>("http://service/health", cancellation.Token);

        await cancellation.CancelAsync();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.IsNotType<ApiError>(thrown);
    }

    /** a body whose connection drops while it is read, like a reset during an error response. */
    sealed class UnreadableContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => throw new HttpRequestException("connection reset");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
