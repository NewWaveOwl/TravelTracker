using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text;
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

    [Theory]
    [InlineData("/v1/places/search?q=private-address")]
    [InlineData("/v1/places/reverse?lat=45&lon=-73")]
    [InlineData("/v1/routes/capabilities")]
    [InlineData("/v1/routes/plan")]
    public async Task KnownRoutingSetupFailureUsesFixedGuidanceWithoutExposingOrLoggingBackendMessage(string path)
    {
        var (client, logger) = RecordedClientFor(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        { Content = JsonContent.Create(new { status = "authentication_unavailable", message = "private-diagnostic-sentinel" }) })));

        var failure = await Assert.ThrowsAsync<ApiError>(() => client.GetJsonAsync<object>("https://routing.example" + path));

        Assert.Equal((FailureKind.Unavailable, 503, "authentication_unavailable"), (failure.Kind, failure.Status, failure.Code));
        Assert.Contains("TravelTracker Supabase URL and key", failure.Message);
        Assert.DoesNotContain("private-diagnostic-sentinel", failure.Message);
        Assert.All(logger.Entries, entry =>
        {
            Assert.DoesNotContain("private-diagnostic-sentinel", entry.Message);
            Assert.DoesNotContain("private-address", entry.Message);
        });
    }

    [Fact]
    public async Task RoutingSetupCodeDoesNotChangeCoreServiceFailures()
    {
        var client = ClientFor(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        { Content = JsonContent.Create(new { status = "authentication_unavailable" }) })));

        var failure = await Assert.ThrowsAsync<ApiError>(() => client.GetJsonAsync<object>("https://users.example/api/v1/users/me"));

        Assert.Null(failure.Code);
        Assert.DoesNotContain("Supabase", failure.Message);
        Assert.Equal(503, failure.Status);
    }

    [Fact]
    public async Task OversizedStreamedRoutingFailureIsBoundedAndKeepsThe503Status()
    {
        using var stream = new FailureStream(Encoding.UTF8.GetBytes("{\"status\":\"authentication_unavailable\",\"message\":\"" + new string('x', 5000) + "\"}"));
        var content = new StreamContent(stream);
        content.Headers.ContentType = new("application/json");
        var client = ClientFor(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = content })));

        var failure = await Assert.ThrowsAsync<ApiError>(() => client.GetJsonAsync<object>("https://routing.example/v1/routes/capabilities"));

        Assert.Equal(503, failure.Status);
        Assert.Null(failure.Code);
        Assert.Equal(4097, stream.BytesRead);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"status\":\"other_failure\",\"message\":\"private-diagnostic-sentinel\"}")]
    public async Task UnknownOrMalformedRoutingFailureKeepsSafeStatusGuidance(string body)
    {
        var client = ClientFor(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") })));

        var failure = await Assert.ThrowsAsync<ApiError>(() => client.GetJsonAsync<object>("https://routing.example/v1/routes/capabilities"));

        Assert.Null(failure.Code);
        Assert.Equal(503, failure.Status);
        Assert.DoesNotContain("private-diagnostic-sentinel", failure.Message);
    }

    /** A non-seekable failure body that records the actual bounded read rather than advertising its length. */
    sealed class FailureStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += count;
            return count;
        }
    }

    [Fact]
    public async Task LogsTheTransportFailureTypeWithoutTheException()
    {
        var cause = new HttpRequestException("test-secret-sentinel");
        var (client, logger) = RecordedClientFor(new StubHandler((_, _) => throw cause));

        await Assert.ThrowsAsync<ApiError>(() => client.GetJsonAsync<object>("http://service/api/v1/trips/example"));

        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.Contains(nameof(HttpRequestException), entry.Message);
        Assert.DoesNotContain("test-secret-sentinel", entry.Message);
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
        Assert.Contains(logger.Entries, entry => entry.Message.Contains(nameof(HttpRequestException), StringComparison.Ordinal));
        Assert.All(logger.Entries, entry => Assert.Null(entry.Exception));
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
