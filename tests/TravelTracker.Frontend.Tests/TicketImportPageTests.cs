using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.TicketImports;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Pages;
using TravelTracker.Frontend.Features.TicketImports;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Exercises ticket import through the rendered picker, editable review and recovery controls. */
public sealed class TicketImportPageTests : BunitContext
{
    static readonly Guid TripId = Guid.Parse("aca0e8f8-b4b8-401f-8116-34984f309545");
    static readonly Guid ImportId = Guid.Parse("b52366ab-62db-4d4c-a094-89e88c08daaa");
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-22T12:00:00Z");
    readonly FakeTimeProvider clock = new(Now);
    SwitchableAuthentication authentication = default!;
    int uploads, deletes, otherWrites;
    static readonly TripDto Trip = new(TripId, "Toronto weekend", "America/Toronto", Now, Now.AddDays(1), false, []);

    void Configure(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? upload = null, string scopes = "trips.read trips.write users.read", string standing = "active")
    {
        authentication = new(new SessionRegistry(clock));
        authentication.SignIn("ticket-token", Now.AddMinutes(30), scopes);
        var handler = new StubHandler((request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/users/me")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new SelfUserProfile(Guid.NewGuid(), "traveller", standing)) });
            if (request.Method == HttpMethod.Post) { uploads++; return upload is null ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(Extracted()) }) : upload(request, token); }
            if (request.Method == HttpMethod.Delete) { deletes++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
            if (request.Method != HttpMethod.Get) otherWrites++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Trip) });
        });
        var client = new ProtectedClient(authentication,
            new ServiceDirectory(new Dictionary<string, string> { ["Trips"] = "http://localhost:5108", ["Users"] = "http://localhost:5106" }),
            new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock,
            NullLogger<ProtectedClient>.Instance, authentication.Sessions);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(new TripsClient(client));
        Services.AddSingleton(new TicketImportsClient(client));
        Services.AddSingleton(new UsersClient(client, new ConfigurationBuilder().Build()));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    IRenderedComponent<TicketImport> Page() => Render<TicketImport>(parameters => parameters.Add(page => page.TripId, TripId));
    static void Upload(IRenderedComponent<TicketImport> page) => page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("%PDF-1.7\n%%EOF", "ticket.pdf", contentType: "application/pdf"));

    static TicketImportDto Extracted() => new(ImportId, TripId, "ticket.pdf", Now.AddMinutes(15),
    [
        new("ticketType", "Train", "extracted"), new("traveler", "Test Traveller", "extracted"),
        new("operator", "UP Express", "extracted"), new("bookingReference", "REF123", "extracted"),
        new("from", "Pearson Airport", "extracted"), new("to", "Union Station", "extracted"),
        new("departureDate", "2026-11-22", "extracted"), new("departureTime", "08:35", "extracted"),
        new("arrivalDate", "2026-11-22", "extracted"), new("arrivalTime", "09:00", "uncertain"),
        new("timeZone", "America/Toronto", "extracted"), new("seat", null, "missing")
    ]);

    [Fact]
    public void SuccessfulUploadProducesEditableFlaggedDraftAndNeverSavesTrip()
    {
        Configure();
        var page = Page();
        Assert.Contains("10 MB and 10 pages", page.Markup);
        Upload(page);
        page.WaitForAssertion(() => Assert.Equal(12, page.FindAll(".ticket-field input").Count));
        Assert.Equal("UP Express", page.Find("#ticket-operator").GetAttribute("value"));
        Assert.Contains("needs-check", page.Find("#ticket-arrivalTime").ClassName);
        Assert.Contains("Not found", page.Find("#hint-seat").TextContent);
        Assert.Equal($"ticket-documents/{TripId:D}/{ImportId:D}", page.Find("a[target=_blank]").GetAttribute("href"));
        Assert.DoesNotContain("ticket-token", page.Markup);

        page.Find("#ticket-operator").Input("Corrected operator");
        page.Find("form").Submit();
        Assert.Contains("Ticket draft reviewed", page.Markup);
        Assert.Contains("Corrected operator", page.Markup);
        Assert.Contains("have not been saved", page.Markup);
        Assert.Equal(1, uploads);
        Assert.Equal(0, otherWrites);
    }

    [Fact]
    public void MissingRequiredFieldsStayEmptyAndPreventFinishingReview()
    {
        Configure();
        var page = Page();
        page.FindAll("button").Single(button => button.TextContent == "Enter manually").Click();
        Assert.All(page.FindAll(".ticket-field input"), input => Assert.Equal("", input.GetAttribute("value") ?? ""));
        page.Find("form").Submit();
        Assert.Contains("Complete the highlighted", page.Find("[role=alert]").TextContent);
        Assert.Equal("true", page.Find("#ticket-departureDate").GetAttribute("aria-invalid"));
        Assert.DoesNotContain("Ticket draft reviewed", page.Markup);
        Assert.Equal(0, uploads);
    }

    [Fact]
    public async Task ExtractingCanBeCancelledAndLateResponseNeverRestoresDraft()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Configure((_, _) => response.Task);
        var page = Page();
        var uploadTask = page.InvokeAsync(() => page.FindComponent<InputFile>().Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([new TestBrowserFile()])));
        page.WaitForAssertion(() => Assert.Contains("Reading your ticket", page.Markup));
        page.FindAll("button").Single(button => button.TextContent == "Cancel import").Click();
        Assert.Contains("Import cancelled", page.Markup);
        response.SetResult(new(HttpStatusCode.Created) { Content = JsonContent.Create(Extracted()) });
        await uploadTask;
        page.WaitForAssertion(() => Assert.Single(page.FindAll("input[type=file]")));
        Assert.Empty(page.FindAll(".ticket-field"));
    }

    [Theory]
    [InlineData(422)]
    [InlineData(503)]
    public void ExtractionFailureKeepsTripUntouchedAndOffersManualRecovery(int status)
    {
        Configure((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var page = Page();
        Upload(page);
        page.WaitForAssertion(() => Assert.Contains("We couldn’t read this PDF", page.Markup));
        Assert.Contains("existing trip is unchanged", page.Markup);
        Assert.Empty(page.FindAll("a[target=_blank]"));
        page.FindAll("button").Single(button => button.TextContent == "Enter manually").Click();
        Assert.Equal("", page.Find("#ticket-operator").GetAttribute("value") ?? "");
        Assert.Equal(0, otherWrites);
    }

    [Fact]
    public void ReadOnlyUserCannotSelectOrUploadPdf()
    {
        Configure(scopes: "trips.read");
        var page = Page();
        Assert.Contains("Read-only trip", page.Markup);
        Assert.Empty(page.FindAll("input[type=file]"));
        Assert.Equal(0, uploads);
    }

    [Theory]
    [InlineData("banned")]
    [InlineData("deactivated")]
    public void InactiveStandingHidesImportControlsEvenWithWriteScope(string standing)
    {
        Configure(standing: standing);
        var page = Page();
        Assert.Contains("Read-only trip", page.Markup);
        Assert.Empty(page.FindAll("input[type=file]"));
        Assert.Empty(page.FindAll(".ticket-field"));
        Assert.Equal(0, uploads);
    }

    [Fact]
    public void MissingUsersReadPermissionKeepsImportControlsReadOnly()
    {
        Configure(scopes: "trips.read trips.write");
        var page = Page();
        Assert.Contains("account status cannot be verified", page.Markup);
        Assert.Empty(page.FindAll("input[type=file]"));
        Assert.Equal(0, uploads);
    }

    [Fact]
    public async Task SessionSignOutClearsAllExtractedPrivateValues()
    {
        Configure();
        var page = Page();
        Upload(page);
        page.WaitForAssertion(() => Assert.Contains("UP Express", page.Markup));
        await page.InvokeAsync(authentication.SignOut);
        page.WaitForAssertion(() => Assert.DoesNotContain("UP Express", page.Markup));
        Assert.Empty(page.FindAll(".ticket-field"));
        Assert.Empty(page.FindAll("a[target=_blank]"));
    }

    [Fact]
    public async Task ExpiredSourceLinkDisappearsButEditableValuesRemain()
    {
        Configure();
        var page = Page();
        Upload(page);
        page.WaitForAssertion(() => Assert.Single(page.FindAll("a[target=_blank]")));
        await page.InvokeAsync(() => clock.Advance(TimeSpan.FromMinutes(15)));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("a[target=_blank]")));
        Assert.Equal("UP Express", page.Find("#ticket-operator").GetAttribute("value"));
        Assert.Contains("PDF expired", page.Markup);
    }

    [Fact]
    public void CancelReleasesSourceAndReturnsToUpload()
    {
        Configure();
        var page = Page();
        Upload(page);
        page.WaitForAssertion(() => Assert.Equal(12, page.FindAll(".ticket-field input").Count));
        page.FindAll("button").Single(button => button.TextContent == "Cancel import").Click();
        page.WaitForAssertion(() => Assert.Equal(1, deletes));
        Assert.Single(page.FindAll("input[type=file]"));
        Assert.DoesNotContain("UP Express", page.Markup);
    }

    [Fact]
    public void UnknownResponseFieldsAreNeverCopiedIntoDraft()
    {
        var extracted = Extracted() with { Fields = [new("passportNumber", "private unsupported", "extracted")] };
        var draft = TicketReviewDraft.From(extracted);
        Assert.Equal(12, draft.Fields.Length);
        Assert.All(draft.Fields, field => Assert.Equal("", field.Value));
        Assert.DoesNotContain(draft.Fields, field => field.Name == "passportNumber");
    }

    /** A synthetic browser stream for observing an unfinished asynchronous file-change event. */
    sealed class TestBrowserFile : IBrowserFile
    {
        public string Name => "ticket.pdf";
        public DateTimeOffset LastModified => Now;
        public long Size => 13;
        public string ContentType => "application/pdf";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            new MemoryStream(System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF"));
    }
}
