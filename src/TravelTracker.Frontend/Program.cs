using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.TicketImports;
using TravelTracker.Frontend.Api.Accommodations;
using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Api.Settings;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Services;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning)
    .AddFilter("Microsoft.AspNetCore.Authentication.OpenIdConnect", LogLevel.None)
    .AddFilter("Microsoft.AspNetCore.Components.Server.Circuits", LogLevel.None);

var serviceUrls = builder.Configuration.GetSection("Services").Get<Dictionary<string, string>>()
    ?? throw new InvalidOperationException("The Services configuration section is missing.");
if (serviceUrls.Count == 0)
    throw new InvalidOperationException("The Services configuration section lists no services.");
var production = builder.Environment.IsProduction();
ServiceDirectory.Validate(serviceUrls, production);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(new ServiceDirectory(serviceUrls));
builder.Services.AddHttpClient<ServiceClient>().RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddScoped<AuthenticationStateProvider, SessionAuthenticationStateProvider>();
builder.Services.AddScoped<ProtectedClient>();
builder.Services.AddScoped<TripsClient>();
builder.Services.AddScoped<TicketImportsClient>();
builder.Services.AddScoped<TripPlacesClient>();
builder.Services.AddScoped<TransportClient>();
builder.Services.AddScoped<AttractionsClient>();
builder.Services.AddScoped<AccommodationsClient>();
builder.Services.AddScoped<UsersClient>();
builder.Services.AddScoped<AdminAccessState>();
builder.Services.AddScoped<SettingsClient>();
builder.Services.AddScoped<UserPreferences>();

// ponytail: disposed when the process exits, so the sign-in event handlers can keep logging through it while the app runs
using var startupLoggerFactory = LoggerFactory.Create(logging => logging.AddConfiguration(builder.Configuration.GetSection("Logging")).AddConsole());
var startupLogger = startupLoggerFactory.CreateLogger("TravelTracker.Frontend.Auth");
var provider = builder.Configuration["Authentication:Provider"];
var supabase = string.IsNullOrWhiteSpace(provider) || string.Equals(provider, "Supabase", StringComparison.OrdinalIgnoreCase)
    ? SupabaseSettings.TryRead(builder.Configuration) : null;
var auth0 = string.Equals(provider, "Auth0", StringComparison.OrdinalIgnoreCase)
    ? Auth0Settings.TryRead(builder.Configuration, startupLogger) : null;
if (production && supabase is null && auth0?.ClientSecret is null)
    throw new InvalidOperationException("Production sign-in requires valid Supabase:Url and Supabase:PublishableKey, or Auth0:Domain, ClientId, ClientSecret and Audience when Auth0 is selected.");
builder.Services.AddSingleton(new AuthSetup(auth0 is not null || supabase is not null, supabase));
if (supabase is not null)
{
    builder.Services.AddSupabaseAuth(supabase);
    startupLogger.LogInformation("Supabase sign-in configured for {Issuer}", supabase.Issuer);
}
else if (auth0 is null)
{
    // no schemes registered: every request stays anonymous and navigation keeps working
    builder.Services.AddAuthentication();
    startupLogger.LogWarning("Sign-in is not configured. Set Supabase:Url and Supabase:PublishableKey for Supabase Auth, or select Auth0 with valid Auth0:Domain, ClientId and Audience.");
}
else
{
    startupLogger.LogInformation("Auth0 sign-in configured.");
    builder.Services.AddAuth0(auth0, startupLogger);
}
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    ExceptionHandlingPath = "/Error",
    CreateScopeForErrors = true,
    SuppressDiagnosticsCallback = _ => true
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/account") || context.Request.Path == "/sign-in" || context.Request.Path.StartsWithSegments("/auth"))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
if (supabase is not null) app.UseRateLimiter();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapTicketDocument();
if (supabase is not null)
    app.MapSupabaseAuthEndpoints();
else if (auth0 is not null)
    app.MapAuthEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

/** Exposes the entry point to the integration tests. */
public partial class Program;
