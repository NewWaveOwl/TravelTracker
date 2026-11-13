using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Auth;

namespace TravelTracker.Frontend.Tests.Support;

/** Hosts the production Supabase forms, token verification and cookie middleware against a fake provider. */
public sealed class SupabaseFrontendFactory(string? provider = "Supabase", bool validSettings = true) : WebApplicationFactory<Program>
{
    public FakeSupabaseAuth Provider { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Authentication:Provider", provider ?? "");
        builder.UseSetting("Supabase:Url", validSettings ? FakeSupabaseAuth.Origin : "http://invalid.example.test");
        builder.UseSetting("Supabase:PublishableKey", "sb_publishable_fixture");
        builder.UseSetting("Auth0:Domain", "legacy.example.test");
        builder.UseSetting("Auth0:ClientId", "legacy-client");
        builder.UseSetting("Auth0:Audience", "traveltracker-api");
        builder.ConfigureServices(services =>
        {
            services.AddHttpClient<SupabaseAuthClient>().ConfigurePrimaryHttpMessageHandler(() => Provider);
            services.AddHttpClient<ServiceClient>().ConfigurePrimaryHttpMessageHandler(() => new StubHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(FrontendFactory.SelfProfileJson) })));
        });
    }

    /**
     * Creates a browser whose cookies are restored through the production middleware on every request.
     * @return The cookie-keeping browser without automatic redirect following.
     */
    public HttpClient CreateBrowser() => CreateDefaultClient(new CookieJarHandler());
}
