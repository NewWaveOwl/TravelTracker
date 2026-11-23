using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Exercises the private document proxy through real frontend cookie and session middleware. */
public sealed class TicketDocumentProxyTests
{
    const string Path = "/ticket-documents/51c5c9a1-2086-4ed0-a399-2408b0c1efb1/7c48ded8-4fef-4b12-be2e-22cb67fcf223";

    [Fact]
    public async Task AnonymousDocumentDownloadIsRefusedWithoutSendingToBackend()
    {
        using var fixture = new FrontendFactory();
        using var browser = fixture.CreateBrowser();
        var response = await browser.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Null(fixture.Transport.LastRequest);
    }

    [Fact]
    public async Task SignedInDocumentDownloadUsesProtectedBackendAndSafeAttachmentHeaders()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nprivate synthetic fixture\n%%EOF");
        using var fixture = new FrontendFactory();
        using var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("/api/v1/trips/51c5c9a1-2086-4ed0-a399-2408b0c1efb1/ticket-imports/7c48ded8-4fef-4b12-be2e-22cb67fcf223/document", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.False(string.IsNullOrWhiteSpace(request.Headers.Authorization?.Parameter));
            var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return Task.FromResult(result);
        });
        using var host = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHttpClient<ServiceClient>().ConfigurePrimaryHttpMessageHandler(() => handler)));
        using var browser = host.CreateDefaultClient(new CookieJarHandler(fixture.Clock));
        await fixture.StartSignInAsync(browser);
        var signedIn = await fixture.CompleteSignInAsync(browser);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);

        var response = await browser.GetAsync(Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("sandbox", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.DoesNotContain("Authorization", response.Headers.Select(header => header.Key));
    }

    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    [InlineData(503)]
    public async Task BackendRefusalNeverReturnsDocumentOrPrivateDiagnostics(int status)
    {
        using var fixture = new FrontendFactory();
        using var handler = StubHandler.Status(status, "PRIVATE PDF CONTENT AND DIAGNOSTICS");
        using var host = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHttpClient<ServiceClient>().ConfigurePrimaryHttpMessageHandler(() => handler)));
        using var browser = host.CreateDefaultClient(new CookieJarHandler(fixture.Clock));
        await fixture.StartSignInAsync(browser);
        await fixture.CompleteSignInAsync(browser);

        var response = await browser.GetAsync(Path);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.DoesNotContain("PRIVATE PDF", await response.Content.ReadAsStringAsync());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }
}
