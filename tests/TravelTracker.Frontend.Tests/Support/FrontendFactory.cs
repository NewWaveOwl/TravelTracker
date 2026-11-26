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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace TravelTracker.Frontend.Tests.Support;

/**
 * hosts the real frontend in memory with the Users API sign-in pointed at FakeUsersAuth and Transport at a stub.
 *
 * @param keyDirectory Share one data protection key ring between hosts, so a second host accepts the first host's session cookie like a restarted app does.
 * @param sessionRegistry Optional shared registry for tests isolating discovery failure from restart invalidation.
 */
public sealed partial class FrontendFactory(string? keyDirectory = null, SessionRegistry? sessionRegistry = null) : WebApplicationFactory<Program>
{
    public const string ProtectedReadPath = "/test/protected-read";
    public const string SelfProfileJson = """{"publicId":"00000000-0000-0000-0000-000000000101","role":"traveller","status":"active","username":"traveller_1","address":"1 Main St"}""";

    FakeUsersAuth? users;

    /** answers the Users API login and register calls. */
    public FakeUsersAuth Users => users ??= new(Clock);

    /** answers protected transport and Users profile reads; its LastRequest shows what the app sent. */
    public StubHandler Transport { get; } = new((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(request.RequestUri?.AbsolutePath == "/api/v1/users/me" ? SelfProfileJson : """{"name":"Example Bus"}""")
    }));

    /** drives cookie expiry and the token expiry check without waiting. */
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Services:Users", "https://localhost:5101");
        builder.ConfigureServices(services =>
        {
            services.AddHttpClient<UsersAuthClient>().ConfigurePrimaryHttpMessageHandler(() => Users);
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

    /** Releases the fake Users API and the transport stub together with the host. */
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            users?.Dispose();
            Transport.Dispose();
        }
        base.Dispose(disposing);
    }

    /** a client that keeps cookies and never follows redirects, so each hop of the flow can be asserted. */
    public HttpClient CreateBrowser() => CreateDefaultClient(new CookieJarHandler(Clock));

    /**
     * signs in through the real form: loads the page for its antiforgery token, then posts the credentials.
     *
     * @param browser The cookie-keeping client.
     * @return The login response, which carries the session cookie.
     */
    public async Task<HttpResponseMessage> SignInAsync(HttpClient browser)
    {
        var page = await browser.GetStringAsync("/sign-in");
        return await browser.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "traveller_1", ["password"] = "fixture-password", ["__RequestVerificationToken"] = AntiforgeryTokenFrom(page)
        }));
    }

    /** reads the antiforgery token the account page renders into its sign-out form. */
    public static string AntiforgeryTokenFrom(string html) => HttpUtility.HtmlDecode(AntiforgeryField().Match(html).Groups[1].Value);

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();

    /**
     * stands in for a circuit: restores the signed-in user from the session cookie into the authentication
     * state exactly as circuit start does, then runs the same ProtectedClient call the account page makes.
     */
    sealed class ProtectedReadFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Path == "/test/granted-scope")
                {
                    var signedIn = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    await context.Response.WriteAsync(string.Join(" ", signedIn.Principal?.FindAll(TravelTracker.Frontend.Auth.AccessTokenClaims.GrantedScope).Select(claim => claim.Value) ?? []));
                    return;
                }
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
