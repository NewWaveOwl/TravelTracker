using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.TicketImports;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Verifies protected raw PDF transport, bounded downloads and session revocation. */
public sealed class TicketImportClientTests
{
    static readonly Guid TripId = Guid.Parse("45b965a5-a71b-4ede-ab1d-60dd50169399");
    static readonly Guid ImportId = Guid.Parse("3cc76cca-706c-458f-a373-42b82b3824bb");
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-22T12:00:00Z"));
    readonly SwitchableAuthentication authentication;

    public TicketImportClientTests()
    {
        authentication = new(new SessionRegistry(clock));
        authentication.SignIn("private-token", clock.GetUtcNow().AddMinutes(10), "trips.read trips.write");
    }

    TicketImportsClient Client(StubHandler handler) => new(new ProtectedClient(authentication,
        new ServiceDirectory(new Dictionary<string, string> { ["Trips"] = "http://localhost:5108" }),
        new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock,
        NullLogger<ProtectedClient>.Instance, authentication.Sessions));

    [Fact]
    public async Task UploadSendsRawPdfWithBearerAndEncodedDisplayName()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\nsynthetic frontend transport fixture\n%%EOF");
        var expected = new TicketImportDto(ImportId, TripId, "Montréal.pdf", clock.GetUtcNow().AddMinutes(15), [new("operator", "UP Express", "extracted")]);
        var handler = new StubHandler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v1/trips/{TripId:D}/ticket-imports", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer private-token", request.Headers.Authorization!.ToString());
            Assert.Equal("application/pdf", request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal("Montr%C3%A9al.pdf", request.Headers.GetValues("X-Ticket-File-Name").Single());
            Assert.Equal(bytes, await request.Content.ReadAsByteArrayAsync(token));
            return new(HttpStatusCode.Created) { Content = JsonContent.Create(expected) };
        });
        using var stream = new MemoryStream(bytes);

        var actual = await Client(handler).UploadAsync(TripId, stream, "Montréal.pdf", CancellationToken.None);

        Assert.Equal(expected.ImportId, actual.ImportId);
        Assert.Equal(expected.Fields, actual.Fields);
    }

    [Fact]
    public async Task DownloadUsesOnlyPrivateConfiguredDocumentPath()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF");
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal($"/api/v1/trips/{TripId:D}/ticket-imports/{ImportId:D}/document", request.RequestUri!.AbsolutePath);
            Assert.Equal("private-token", request.Headers.Authorization!.Parameter);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return Task.FromResult(response);
        });

        Assert.Equal(bytes, await Client(handler).GetDocumentAsync(TripId, ImportId, CancellationToken.None));
    }

    [Fact]
    public async Task CancelAcceptsNoContentWithoutTryingToReadJson()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal($"/api/v1/trips/{TripId:D}/ticket-imports/{ImportId:D}", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        await Client(handler).CancelAsync(TripId, ImportId, CancellationToken.None);
    }

    [Theory]
    [InlineData(400, "invalid")]
    [InlineData(408, "timed out")]
    [InlineData(413, "too large")]
    [InlineData(415, "PDF document")]
    [InlineData(422, "could not read")]
    [InlineData(429, "storage is full")]
    [InlineData(503, "temporarily unavailable")]
    public async Task FailureMessagesExplainImportRecoveryWithoutServerDiagnostics(int status, string message)
    {
        using var stream = new MemoryStream([1]);
        var error = await Assert.ThrowsAsync<ApiError>(() => Client(StubHandler.Status(status, "secret diagnostics"))
            .UploadAsync(TripId, stream, "ticket.pdf", CancellationToken.None));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret diagnostics", error.Message);
        Assert.Equal(status, error.Status);
    }

    [Fact]
    public async Task SignedOutUploadNeverSendsPrivatePdf()
    {
        authentication.SignOut();
        var handler = StubHandler.Status(201, "{}");
        using var stream = new MemoryStream([1]);
        var error = await Assert.ThrowsAsync<ApiError>(() => Client(handler).UploadAsync(TripId, stream, "ticket.pdf", CancellationToken.None));
        Assert.Equal(401, error.Status);
        Assert.Null(handler.LastRequest);
    }

    [Theory]
    [InlineData(401, true)]
    [InlineData(403, false)]
    public async Task UploadKeepsExistingSessionConsequences(int status, bool revoked)
    {
        using var stream = new MemoryStream([1]);
        await Assert.ThrowsAsync<ApiError>(() => Client(StubHandler.Status(status)).UploadAsync(TripId, stream, "ticket.pdf", CancellationToken.None));
        Assert.Equal(revoked, !authentication.Sessions.IsActive(authentication.User));
    }

    [Fact]
    public async Task LatePrivateDownloadAfterSignOutIsRejected()
    {
        var handler = new StubHandler((_, _) =>
        {
            authentication.SignOut();
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return Task.FromResult(response);
        });
        var error = await Assert.ThrowsAsync<ApiError>(() => Client(handler).GetDocumentAsync(TripId, ImportId, CancellationToken.None));
        Assert.Equal(401, error.Status);
    }

    [Fact]
    public async Task OversizedOrWrongTypeDownloadIsRejected()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        response.Content.Headers.ContentLength = TicketImportsClient.MaximumBytes + 1;
        var handler = new StubHandler((_, _) => Task.FromResult(response));
        await Assert.ThrowsAsync<ApiError>(() => Client(handler).GetDocumentAsync(TripId, ImportId, CancellationToken.None));
    }
}
