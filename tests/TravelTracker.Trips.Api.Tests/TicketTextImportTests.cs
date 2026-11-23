using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using TravelTracker.Trips.Api.Contracts;
using TravelTracker.Trips.Api.TicketImports;
using TravelTracker.Trips.Domain.TicketImport;
using TravelTracker.Trips.Api.Tests.Support;

namespace TravelTracker.Trips.Api.Tests;

/** Proves the pasted-text ticket route through the signed-JWT host: owner-only, bounded, plain text, and never a trip change. */
public sealed class TicketTextImportTests
{
    private const string TextRoutePath = "/api/v1/trips/{0}/ticket-imports/text";

    [Fact]
    public async Task ParseText_CompleteTicket_ReturnsPdfShapedFieldsWithoutChangingTrip()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        var before = await client.GetAsync($"/api/v1/trips/{trip.TripId}");
        using var response = await ParseText(client, trip.TripId, Paste(
            "Reference: ABC123", "Mode: Train", "From: Gare de Lyon", "To: Nice Ville", "Time zone: Europe/Paris",
            "Departure: 2026-06-01 09:00", "Arrival: 2026-06-01 12:30"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store, private", response.Headers.CacheControl?.ToString());
        var draft = (await response.Content.ReadFromJsonAsync<TicketTextDraftResponse>())!;
        Assert.Equal(trip.TripId, draft.TripId);
        Assert.Equal(9, draft.Fields.Count);
        AssertField(draft, "ticketType", "train", "extracted");
        AssertField(draft, "bookingReference", "ABC123", "extracted");
        AssertField(draft, "from", "Gare de Lyon", "extracted");
        AssertField(draft, "to", "Nice Ville", "extracted");
        AssertField(draft, "departureDate", "2026-06-01", "extracted");
        AssertField(draft, "departureTime", "09:00", "extracted");
        AssertField(draft, "arrivalDate", "2026-06-01", "extracted");
        AssertField(draft, "arrivalTime", "12:30", "extracted");
        AssertField(draft, "timeZone", "Europe/Paris", "extracted");
        var after = await client.GetAsync($"/api/v1/trips/{trip.TripId}");
        Assert.Equal(await before.Content.ReadAsStringAsync(), await after.Content.ReadAsStringAsync());
        Assert.Equal(before.Headers.ETag, after.Headers.ETag);
        Assert.Equal(1, factory.Repository.Count);
    }

    [Fact]
    public async Task ParseText_PartialAndAmbiguousTicket_FlagsWithoutInventing()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        using var response = await ParseText(client, trip.TripId, Paste(
            "Mode: bus", "From: Central Station", "Departure: 2026-02-30 09:00", "Arrival: 2026-06-01 12:00", "Time zone: Mars/Olympus"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<TicketTextDraftResponse>())!;
        AssertField(draft, "ticketType", "bus", "extracted");
        AssertField(draft, "from", "Central Station", "extracted");
        AssertField(draft, "to", null, "missing");
        AssertField(draft, "bookingReference", null, "missing");
        AssertField(draft, "departureDate", null, "uncertain");
        AssertField(draft, "departureTime", null, "uncertain");
        AssertField(draft, "arrivalDate", null, "uncertain");
        AssertField(draft, "arrivalTime", null, "uncertain");
        AssertField(draft, "timeZone", null, "uncertain");
    }

    [Fact]
    public async Task ParseText_ByteOrderMarkBeforeFirstLabel_IsStillRead()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        using var response = await ParseText(client, trip.TripId, Encoding.UTF8.GetBytes("﻿Mode: bus"));
        var draft = (await response.Content.ReadFromJsonAsync<TicketTextDraftResponse>())!;
        AssertField(draft, "ticketType", "bus", "extracted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  \n")]
    [InlineData("Mode: bus\nNot a label\nFrom: \0 hidden")]
    public async Task ParseText_BlankOrNulInput_IsBadRequest(string text)
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        using var response = await ParseText(client, trip.TripId, Encoding.UTF8.GetBytes(text));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ParseText_TooManyCharacters_IsBadRequestWithinByteBound()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        var text = "From: " + new string('a', TicketTextParser.MaximumCharacters);
        Assert.Equal(HttpStatusCode.BadRequest, (await ParseText(client, trip.TripId, Encoding.UTF8.GetBytes(text))).StatusCode);
    }

    [Fact]
    public async Task ParseText_TooManyLines_IsBadRequest()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        var lines = Enumerable.Repeat("Mode: bus", TicketTextParser.MaximumLines + 1).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await ParseText(client, trip.TripId, Paste(lines))).StatusCode);
    }

    [Fact]
    public async Task ParseText_BytesBeyondBound_AreRefusedWithPayloadTooLarge()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        var tooManyCharacters = Encoding.UTF8.GetBytes(new string('e', TicketTextParser.MaximumCharacters + 1));
        var accented = Encoding.UTF8.GetBytes(new string('é', TicketImportService.MaximumTextBytes / 2 + 1));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await ParseText(client, trip.TripId, accented)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ParseText(client, trip.TripId, tooManyCharacters)).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, string.Format(TextRoutePath, trip.TripId))
        {
            Content = new UnknownLengthPdfContent(new byte[TicketImportService.MaximumTextBytes + 1])
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        Assert.Null(request.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.SendAsync(request)).StatusCode);
        Assert.Equal(1, factory.Repository.Count);
    }

    [Fact]
    public async Task ParseText_InvalidUtf8_IsBadRequest()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        Assert.Equal(HttpStatusCode.BadRequest, (await ParseText(client, trip.TripId, [0xFF, 0xFE, 0x41, 0x00])).StatusCode);
    }

    [Theory]
    [InlineData("text/html; charset=utf-8")]
    [InlineData("application/pdf")]
    [InlineData("multipart/form-data")]
    [InlineData("text/plain; charset=utf-16")]
    public async Task ParseText_OtherMediaTypeOrCharset_IsUnsupported(string mediaType)
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await ParseText(client, trip.TripId, Paste("Mode: bus"), mediaType)).StatusCode);
    }

    [Fact]
    public async Task ParseText_PastedMarkup_IsReturnedAsPlainStringsOnly()
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        const string script = "<script>alert('x')</script>";
        const string image = "<img src=x onerror=alert(1)>";
        using var response = await ParseText(client, trip.TripId, Paste($"From: {script}", $"To: {image}", "Mode: bus"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        var draft = (await response.Content.ReadFromJsonAsync<TicketTextDraftResponse>())!;
        AssertField(draft, "from", script, "extracted");
        AssertField(draft, "to", image, "extracted");
    }

    [Theory]
    [InlineData("bob")]
    [InlineData("admin")]
    public async Task ParseText_OtherOwnerOrAdministrator_LooksAbsent(string subject)
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var other = factory.CreateClientFor(subject);
        Assert.Equal(HttpStatusCode.NotFound, (await ParseText(other, trip.TripId, Paste("Mode: bus"))).StatusCode);
    }

    [Theory]
    [InlineData("banned", HttpStatusCode.Forbidden)]
    [InlineData("deactivated", HttpStatusCode.Forbidden)]
    [InlineData("unavailable", HttpStatusCode.ServiceUnavailable)]
    public async Task ParseText_InactiveOrUnavailableStanding_FailsClosed(string standing, HttpStatusCode expected)
    {
        using var factory = NewFactory();
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor();
        if (standing == "unavailable") factory.Upstream.UsersUnavailable = true;
        else factory.Upstream.Account("alice", TripTestData.AliceId, standing, "traveller");
        Assert.Equal(expected, (await ParseText(client, trip.TripId, Paste("Mode: bus"))).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("trips.read", HttpStatusCode.Forbidden)]
    public async Task ParseText_MissingAuthenticationOrWriteScope_IsRefusedBeforeUsersLookup(string? scopes, HttpStatusCode expected)
    {
        using var factory = NewFactory();
        using var client = factory.CreateClientFor(scopes: scopes);
        Assert.Equal(expected, (await ParseText(client, Guid.NewGuid(), Paste("Mode: bus"))).StatusCode);
        Assert.Empty(factory.Upstream.Calls);
    }

    /**
     * Configures a new host without shared state or live dependencies.
     * @return Isolated factory with the standard active owner account.
     */
    private static TripsApiFactory NewFactory()
    {
        var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        return factory;
    }

    /**
     * Joins labelled lines the way a paste arrives.
     * @param lines Lines of ticket text.
     * @return UTF-8 bytes of the pasted text.
     */
    private static byte[] Paste(params string[] lines) => Encoding.UTF8.GetBytes(string.Join('\n', lines));

    /**
     * Sends pasted text through the public text route.
     * @param client Signed caller's client.
     * @param tripId Existing trip.
     * @param bytes Pasted text bytes.
     * @param mediaType Request media type.
     * @return HTTP response for status and contract assertions.
     */
    private static Task<HttpResponseMessage> ParseText(HttpClient client, Guid tripId, byte[] bytes, string mediaType = "text/plain; charset=utf-8")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, string.Format(TextRoutePath, tripId))
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        return client.SendAsync(request);
    }

    /**
     * Asserts one proposed field by its stable name.
     * @param draft Returned draft.
     * @param name Supported field name.
     * @param value Expected explicit value or null.
     * @param status Expected status word.
     */
    private static void AssertField(TicketTextDraftResponse draft, string name, string? value, string status)
    {
        var field = Assert.Single(draft.Fields, field => field.Name == name);
        Assert.Equal(value, field.Value);
        Assert.Equal(status, field.Status);
    }
}
