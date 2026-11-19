using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.TicketImports;
using TravelTracker.Frontend.Api.Accommodations;
using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Api.Settings;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Api.Routing;
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
builder.Services.AddScoped<RoutingClient>();
builder.Services.AddScoped<UserPreferences>();
builder.Services.AddScoped<MapQualityPreference>();

var usersConfigured = serviceUrls.ContainsKey("Users");
builder.Services.AddSingleton(new AuthSetup(usersConfigured));
if (usersConfigured)
    builder.Services.AddPasswordAuth();
else
    // no schemes registered: every request stays anonymous and navigation keeps working
    builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();

foreach (var (name, url) in serviceUrls)
    app.Logger.LogInformation("Configured service {Service} at {Url}", name, url);
if (usersConfigured) app.Logger.LogInformation("Password sign-in goes through the Users API.");
else app.Logger.LogWarning("Sign-in is not configured. Add a Users entry to Services.");
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
if (usersConfigured) app.UseRateLimiter();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapTicketDocument();
if (usersConfigured)
    app.MapPasswordAuthEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

/** Exposes the entry point to the integration tests. */
public partial class Program;
