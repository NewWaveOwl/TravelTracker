using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using TravelTracker.Trips.Api.TicketImports;
using TravelTracker.Trips.Api.Contracts;
using TravelTracker.Trips.Data.TicketImports;
using TravelTracker.Trips.Api.Tests.Support;
using UglyToad.PdfPig;

namespace TravelTracker.Trips.Api.Tests;

/** Proves the ticket boundary through the real signed-JWT HTTP host and synthetic PDFs. */
public sealed class TicketImportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upload_StaticTicket_ReturnsEditableProposalsAndPrivateSourceWithoutChangingTrip(bool compressed)
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        var before = await client.GetAsync($"/api/v1/trips/{trip.TripId}");
        var original = await before.Content.ReadAsStringAsync();
        var bytes = TicketPdfFixture.Build(compressed: compressed);
        using var response = await Upload(client, trip.TripId, bytes, "../Test%20ticket.pdf");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store, private", response.Headers.CacheControl?.ToString());
        var draft = await response.Content.ReadFromJsonAsync<TicketImportDraftResponse>();
        Assert.NotNull(draft);
        Assert.Equal(trip.TripId, draft.TripId);
        Assert.Equal("Test ticket.pdf", draft.FileName);
        Assert.InRange(draft.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
        Assert.Equal(12, draft.Fields.Count);
        Assert.All(draft.Fields, field => Assert.Equal("extracted", field.Status));
        Assert.Equal("Test Rail", draft.Fields.Single(field => field.Name == "operator").Value);
        Assert.Equal("08:35", draft.Fields.Single(field => field.Name == "departureTime").Value);
        Assert.Equal("America/Toronto", draft.Fields.Single(field => field.Name == "timeZone").Value);
        Assert.DoesNotContain("issuer", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        using var document = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Equal(bytes, await document.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", document.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", document.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("sandbox", document.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", document.Headers.GetValues("X-Content-Type-Options").Single());
        var after = await client.GetAsync($"/api/v1/trips/{trip.TripId}");
        Assert.Equal(original, await after.Content.ReadAsStringAsync());
        Assert.Equal(before.Headers.ETag, after.Headers.ETag);
        Assert.Equal(1, factory.Repository.Count);
    }

    [Fact]
    public async Task Upload_MissingAmbiguousAndUnrecognisedFields_AreFlaggedWithoutGuessing()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        using var response = await Upload(client, trip.TripId, TicketPdfFixture.Build([
            "Ticket type: Train", "From: Station A", "To: Station B", "Departure date: 11/12/26",
            "Arrival time: 09:00", "Arrival time: 10:00", "Time zone: EST", "Passport: PRIVATE-UNSUPPORTED", "Seat:"]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<TicketImportDraftResponse>())!;
        AssertField(draft, "departureDate", null, "uncertain");
        AssertField(draft, "arrivalTime", null, "uncertain");
        AssertField(draft, "timeZone", null, "uncertain");
        AssertField(draft, "arrivalDate", null, "missing");
        AssertField(draft, "seat", null, "missing");
        AssertField(draft, "operator", null, "missing");
        Assert.DoesNotContain("PRIVATE-UNSUPPORTED", await response.Content.ReadAsStringAsync());
        Assert.Equal(12, draft.Fields.Count);
    }

    [Theory]
    [InlineData("bob")]
    [InlineData("admin")]
    public async Task UploadReadAndCancel_OtherOwnerAndAdministrator_LookAbsent(string subject)
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var owner = factory.CreateClientFor();
        using var upload = await Upload(owner, trip.TripId, TicketPdfFixture.Build());
        var draft = (await upload.Content.ReadFromJsonAsync<TicketImportDraftResponse>())!;
        using var other = factory.CreateClientFor(subject);
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(other, trip.TripId, Encoding.ASCII.GetBytes("not a PDF"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(upload.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/trips/{trip.TripId}/ticket-imports/{draft.ImportId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(upload.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/trips/{trip.TripId}/ticket-imports/{draft.ImportId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(upload.Headers.Location)).StatusCode);
    }

    [Fact]
    public async Task Upload_SameSubjectDifferentOwnerIssuer_LooksAbsentBeforeParsing()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice", "https://another-issuer.example.test");
        using var client = factory.CreateClientFor();
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(client, trip.TripId, Encoding.ASCII.GetBytes("not PDF"))).StatusCode);
        Assert.Equal(1, factory.Repository.Count);
    }

    [Fact]
    public async Task DocumentAndCancel_ImportIdUsedUnderAnotherOwnedTrip_LookAbsent()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        var otherTrip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        using var upload = await Upload(client, trip.TripId, TicketPdfFixture.Build());
        var draft = (await upload.Content.ReadFromJsonAsync<TicketImportDraftResponse>())!;
        var tampered = $"/api/v1/trips/{otherTrip.TripId}/ticket-imports/{draft.ImportId}";
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(tampered + "/document")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(tampered)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(upload.Headers.Location)).StatusCode);
    }

    [Theory]
    [InlineData("trips.write", HttpStatusCode.Forbidden, HttpStatusCode.NoContent)]
    [InlineData("trips.read", HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [InlineData(null, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized)]
    public async Task DocumentAndCancel_RequireIndependentReadAndWriteScopes(string? scopes, HttpStatusCode expectedRead, HttpStatusCode expectedCancel)
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var owner = factory.CreateClientFor();
        using var upload = await Upload(owner, trip.TripId, TicketPdfFixture.Build());
        var draft = (await upload.Content.ReadFromJsonAsync<TicketImportDraftResponse>())!;
        using var restricted = factory.CreateClientFor(scopes: scopes);
        Assert.Equal(expectedRead, (await restricted.GetAsync(upload.Headers.Location)).StatusCode);
        Assert.Equal(expectedCancel, (await restricted.DeleteAsync($"/api/v1/trips/{trip.TripId}/ticket-imports/{draft.ImportId}")).StatusCode);
    }

    [Theory]
    [InlineData("banned", HttpStatusCode.OK)]
    [InlineData("deactivated", HttpStatusCode.Forbidden)]
    [InlineData("unavailable", HttpStatusCode.ServiceUnavailable)]
    public async Task DocumentAndCancel_RecheckStandingAfterUpload(string standing, HttpStatusCode read)
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var owner = factory.CreateClientFor();
        using var upload = await Upload(owner, trip.TripId, TicketPdfFixture.Build());
        var draft = (await upload.Content.ReadFromJsonAsync<TicketImportDraftResponse>())!;
        if (standing == "unavailable") factory.Upstream.UsersUnavailable = true;
        else factory.Upstream.Account("alice", TripTestData.AliceId, standing, "traveller");
        Assert.Equal(read, (await owner.GetAsync(upload.Headers.Location)).StatusCode);
        Assert.Equal(standing == "unavailable" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden,
            (await owner.DeleteAsync($"/api/v1/trips/{trip.TripId}/ticket-imports/{draft.ImportId}")).StatusCode);
    }

    [Theory]
    [InlineData("banned")]
    [InlineData("deactivated")]
    [InlineData("unknown")]
    public async Task Upload_InactiveOrUnknownAccount_IsForbiddenBeforeParsing(string subject)
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, subject);
        using var client = factory.CreateClientFor(subject);
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(client, trip.TripId, [])).StatusCode);
    }

    [Fact]
    public async Task Upload_UsersUnavailable_FailsClosedBeforeParsing()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        factory.Upstream.UsersUnavailable = true;
        using var client = factory.CreateClientFor();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Upload(client, trip.TripId, [])).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("trips.read", HttpStatusCode.Forbidden)]
    [InlineData("catalogue.write", HttpStatusCode.Forbidden)]
    public async Task Upload_MissingAuthenticationOrWriteScope_IsRejectedBeforeUsersLookup(string? scopes, HttpStatusCode expected)
    {
        using var factory = NewFactory();
        using var client = factory.CreateClientFor(scopes: scopes);
        Assert.Equal(expected, (await Upload(client, Guid.NewGuid(), [])).StatusCode);
        Assert.Empty(factory.Upstream.Calls);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    public async Task Upload_InvalidRealBearer_IsUnauthorized(string failure)
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(failure: failure));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(client, Guid.NewGuid(), [])).StatusCode);
        Assert.Empty(factory.Upstream.Calls);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("multipart/form-data")]
    [InlineData("application/octet-stream")]
    public async Task Upload_OtherMediaType_IsUnsupported(string mediaType)
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Upload(client, trip.TripId, TicketPdfFixture.Build(), mediaType: mediaType)).StatusCode);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("truncated")]
    [InlineData("malformed")]
    [InlineData("empty-text")]
    [InlineData("unknown-fields")]
    [InlineData("pages")]
    [InlineData("encrypted")]
    [InlineData("escaped-encrypted")]
    [InlineData("javascript")]
    [InlineData("escaped-javascript")]
    [InlineData("embedded")]
    [InlineData("object-stream")]
    [InlineData("decompression")]
    [InlineData("huge-literal")]
    [InlineData("huge-compressed-literal")]
    [InlineData("form")]
    [InlineData("type3")]
    [InlineData("font-map")]
    [InlineData("xref-budget")]
    [InlineData("reference-budget")]
    [InlineData("comment-reference-budget")]
    [InlineData("header-junk")]
    public async Task Upload_UnsafeOrUnsupportedPdf_LeavesTripAndRevisionUntouched(string failure)
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        var before = await client.GetAsync($"/api/v1/trips/{trip.TripId}");
        var bytes = failure switch
        {
            "signature" => Encoding.ASCII.GetBytes("this is definitely not a PDF"),
            "truncated" => TicketPdfFixture.Build()[..100],
            "malformed" => Encoding.ASCII.GetBytes("%PDF-1.4\nnot PDF objects\n%%EOF"),
            "empty-text" => TicketPdfFixture.Build([]),
            "unknown-fields" => TicketPdfFixture.Build(["Unrelated: document"]),
            "pages" => TicketPdfFixture.Build(pages: 11),
            "encrypted" => TicketPdfFixture.Build(encrypted: true),
            "escaped-encrypted" => TicketPdfFixture.Build(extraName: "En#63rypt"),
            "javascript" => TicketPdfFixture.Build(extraName: "JavaScript"),
            "escaped-javascript" => TicketPdfFixture.Build(extraName: "J#61vaScript"),
            "embedded" => TicketPdfFixture.Build(extraName: "EmbeddedFiles"),
            "object-stream" => TicketPdfFixture.Build(extraName: "ObjStm"),
            "huge-literal" => TicketPdfFixture.Build(["Operator: " + new string('A', 2 * 1024 * 1024)]),
            "huge-compressed-literal" => TicketPdfFixture.Build(["Operator: " + new string('A', 2 * 1024 * 1024)], compressed: true),
            "form" => TicketPdfFixture.Build(extraName: "Form"),
            "type3" => TicketPdfFixture.Build(extraName: "Type3"),
            "font-map" => TicketPdfFixture.Build(extraName: "ToUnicode"),
            "xref-budget" => Encoding.ASCII.GetBytes("%PDF-1.4\nxref\n0 1000000\ntrailer\n%%EOF"),
            "reference-budget" => Encoding.ASCII.GetBytes("%PDF-1.4\n" + string.Join(' ', Enumerable.Repeat("1 0 R", 10_001)) + "\n%%EOF"),
            "comment-reference-budget" => Encoding.ASCII.GetBytes("%PDF-1.4\n" + string.Join(' ', Enumerable.Repeat("1 % structural comment\n0\0R", 10_001)) + "\n%%EOF"),
            "header-junk" => Encoding.ASCII.GetBytes("%PDF-1.7evil\n%%EOF"),
            _ => TicketPdfFixture.Build(compressed: true, inflatedPadding: 4 * 1024 * 1024)
        };
        if (failure == "encrypted")
        {
            using var encrypted = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false });
            Assert.True(encrypted.IsEncrypted);
        }
        var rejected = await Upload(client, trip.TripId, bytes);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.DoesNotContain("SYNTHETIC42", await rejected.Content.ReadAsStringAsync());
        var after = await client.GetAsync($"/api/v1/trips/{trip.TripId}");
        Assert.Equal(await before.Content.ReadAsStringAsync(), await after.Content.ReadAsStringAsync());
        Assert.Equal(before.Headers.ETag, after.Headers.ETag);
        Assert.Equal(1, factory.Repository.Count);
    }

    [Fact]
    public async Task Upload_TenPages_IsAcceptedAtTheDocumentedBoundary()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        Assert.Equal(HttpStatusCode.Created, (await Upload(client, trip.TripId, TicketPdfFixture.Build(pages: 10))).StatusCode);
    }

    [Fact]
    public async Task Upload_EmptyAndOversizeBodies_AreRefusedWithoutChangingTrip()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, trip.TripId, [])).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Upload(client, trip.TripId, new byte[MemoryTicketImportStore.MaxFileBytes + 1])).StatusCode);
        Assert.Equal(1, factory.Repository.Count);
    }

    [Fact]
    public async Task Upload_FifthDocumentFitsAndSixthHitsPrivateQuota()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        var store = factory.Services.GetRequiredService<ITicketImportStore>();
        for (var index = 0; index < 4; index++) Assert.NotNull(store.Add(TripsApiFactory.Issuer, "alice", trip.TripId, "ticket.pdf", TicketPdfFixture.Build()));
        Assert.Equal(HttpStatusCode.Created, (await Upload(client, trip.TripId, TicketPdfFixture.Build())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Upload(client, trip.TripId, TicketPdfFixture.Build())).StatusCode);
    }

    [Fact]
    public async Task Upload_StreamingBodyBeyondLimit_IsRefusedEvenWithoutContentLength()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/trips/{trip.TripId}/ticket-imports")
        {
            Content = new UnknownLengthPdfContent(new byte[MemoryTicketImportStore.MaxFileBytes + 1])
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        Assert.Null(request.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.SendAsync(request)).StatusCode);
        Assert.Equal(1, factory.Repository.Count);
    }

    [Fact]
    public async Task Upload_ExactlyTenMiBOfValidPdf_IsAccepted()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        var padding = MemoryTicketImportStore.MaxFileBytes - TicketPdfFixture.Build().Length;
        var candidate = TicketPdfFixture.Build(envelopePadding: padding);
        var bytes = TicketPdfFixture.Build(envelopePadding: padding + MemoryTicketImportStore.MaxFileBytes - candidate.Length);
        Assert.Equal(MemoryTicketImportStore.MaxFileBytes, bytes.Length);
        Assert.Equal(HttpStatusCode.Created, (await Upload(client, trip.TripId, bytes)).StatusCode);
    }

    [Fact]
    public async Task Upload_TwoRequestsParsing_ThirdIsBusyAndSlotsRecoverAfterCompletion()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var parser = new BlockingTicketPdfParser();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITicketPdfParser>();
            services.AddSingleton<ITicketPdfParser>(parser);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken());
        var first = Upload(client, trip.TripId, TicketPdfFixture.Build());
        var second = Upload(client, trip.TripId, TicketPdfFixture.Build());
        try
        {
            await parser.BothEntered.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.TooManyRequests, (await Upload(client, trip.TripId, TicketPdfFixture.Build())).StatusCode);
        }
        finally { parser.Dispose(); }
        Assert.Equal(HttpStatusCode.Created, (await first).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await second).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Upload(client, trip.TripId, TicketPdfFixture.Build())).StatusCode);
    }

    /**
     * Configures a new host without shared state or live dependencies.
     * @return Isolated factory with standard active and restricted accounts.
     */
    private static TripsApiFactory NewFactory()
    {
        var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        return factory;
    }

    /**
     * Sends raw PDF bytes using the same public upload contract as the Blazor client.
     * @param client Signed caller's client.
     * @param tripId Existing trip.
     * @param bytes Synthetic fixture bytes.
     * @param fileName Optional display metadata.
     * @param mediaType Upload media type.
     * @return HTTP response for status and contract assertions.
     */
    private static Task<HttpResponseMessage> Upload(HttpClient client, Guid tripId, byte[] bytes,
        string fileName = "ticket.pdf", string mediaType = "application/pdf")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/trips/{tripId}/ticket-imports")
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        request.Headers.Add("X-Ticket-File-Name", fileName);
        return client.SendAsync(request);
    }

    /**
     * Asserts one public uncertainty contract without relying on field order.
     * @param draft Returned draft.
     * @param name Supported field name.
     * @param value Expected explicit value or null.
     * @param status Expected uncertainty status.
     */
    private static void AssertField(TicketImportDraftResponse draft, string name, string? value, string status)
    {
        var field = Assert.Single(draft.Fields, field => field.Name == name);
        Assert.Equal(value, field.Value);
        Assert.Equal(status, field.Status);
    }
}
