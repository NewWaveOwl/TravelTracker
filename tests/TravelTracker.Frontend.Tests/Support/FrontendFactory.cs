using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Auth;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace TravelTracker.Frontend.Tests.Support;

/**
 * Hosts the real frontend in memory with Auth0 pointed at FakeAuth0 and Transport at a stub.
 *
 * @param signInConfigured False to host the app with no Auth0 settings.
 * @param clientSecret Set to host the app as a regular web application with a client secret.
 * @param keyDirectory Share one data protection key ring between hosts, so a second host accepts the first host's session cookie like a restarted app does.
 * @param sessionRegistry Optional shared registry for tests isolating discovery failure from restart invalidation.
 */
public sealed partial class FrontendFactory(bool signInConfigured = true, string? clientSecret = null, string? keyDirectory = null, SessionRegistry? sessionRegistry = null) : WebApplicationFactory<Program>
{
    public const string ProtectedReadPath = "/test/protected-read";

    public FakeAuth0 Auth0 { get; } = new("test-client");

    /** Answers the protected Transport read; its LastRequest shows what the app sent. */
    public StubHandler Transport { get; } = StubHandler.Status(200, """{"name":"Example Bus"}""");

    /** Drives cookie expiry and the token expiry check without waiting. */
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        if (signInConfigured)
        {
            builder.UseSetting("Auth0:Domain", "tenant.example.test");
            builder.UseSetting("Auth0:ClientId", "test-client");
            builder.UseSetting("Auth0:Audience", "traveltracker-api");
            if (clientSecret is not null)
                builder.UseSetting("Auth0:ClientSecret", clientSecret);
        }
        else
        {
            builder.UseSetting("Auth0:Domain", "");
        }
        builder.ConfigureServices(services =>
        {
            services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options => options.BackchannelHttpHandler = Auth0);
            services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options => options.TimeProvider = Clock);
            services.AddSingleton<TimeProvider>(Clock);
            if (sessionRegistry is not null)
                services.AddSingleton(sessionRegistry);
            services.AddHttpClient<ServiceClient>().ConfigurePrimaryHttpMessageHandler(() => Transport);
            services.AddTransient<IStartupFilter, ProtectedReadFilter>();
            if (keyDirectory is not null)
                services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory)).SetApplicationName("traveltracker-tests");
        });
    }

    /** A client that keeps cookies and never follows redirects, so each hop of the flow can be asserted. */
    public HttpClient CreateBrowser() => CreateDefaultClient(new CookieJarHandler());

    /**
     * Starts sign-in and records the nonce and PKCE challenge the fake tenant needs.
     *
     * @param browser The cookie-keeping client.
     * @return The authorize URL the app redirected to.
     */
    public async Task<Uri> StartSignInAsync(HttpClient browser)
    {
        var response = await browser.GetAsync("/auth/login");
        var authorize = response.Headers.Location!;
        var query = HttpUtility.ParseQueryString(authorize.Query);
        Auth0.Nonce = query["nonce"];
        Auth0.CodeChallenge = query["code_challenge"];
        State = query["state"];
        return authorize;
    }

    public string? State { get; private set; }

    /** Returns from the tenant with an authorization code, as the browser would. */
    public Task<HttpResponseMessage> CompleteSignInAsync(HttpClient browser) => browser.GetAsync($"/signin-oidc?code=fake-code&state={Uri.EscapeDataString(State!)}");

    /** Reads the antiforgery token the account page renders into its sign-out form. */
    public static string AntiforgeryTokenFrom(string html) => HttpUtility.HtmlDecode(AntiforgeryField().Match(html).Groups[1].Value);

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();

    /**
     * Stands in for a circuit: restores the signed-in user from the session cookie into the authentication
     * state exactly as circuit start does, then runs the same ProtectedClient call the account page makes.
     */
    sealed class ProtectedReadFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Path != ProtectedReadPath)
                {
                    await continuation();
                    return;
                }
                var principal = (await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)).Principal ?? new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
                ((IHostEnvironmentAuthenticationStateProvider)context.RequestServices.GetRequiredService<AuthenticationStateProvider>()).SetAuthenticationState(Task.FromResult(new AuthenticationState(principal)));
                try
                {
                    await context.RequestServices.GetRequiredService<ProtectedClient>().GetJsonAsync<System.Text.Json.JsonElement>("Transport", "/api/v1/transport-modes/00000000-0000-0000-0000-000000000101");
                    await context.Response.WriteAsync("ok");
                }
                catch (ApiError error)
                {
                    context.Response.StatusCode = error.Status ?? 500;
                    await context.Response.WriteAsync(error.Message);
                }
            });
            next(app);
        };
    }
}
