using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);

var serviceUrls = builder.Configuration.GetSection("Services").Get<Dictionary<string, string>>()
    ?? throw new InvalidOperationException("The Services configuration section is missing.");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(new ServiceDirectory(serviceUrls));
builder.Services.AddHttpClient<ServiceClient>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddScoped<AuthenticationStateProvider, SessionAuthenticationStateProvider>();
builder.Services.AddScoped<ProtectedClient>();
builder.Services.AddScoped<TravelTracker.Frontend.Api.Trips.TripsClient>();
builder.Services.AddScoped<TravelTracker.Frontend.Api.Trips.TripPlacesClient>();
builder.Services.AddScoped<TravelTracker.Frontend.Api.Transport.TransportClient>();
builder.Services.AddScoped<TravelTracker.Frontend.Api.Attractions.AttractionsClient>();
builder.Services.AddScoped<TravelTracker.Frontend.Api.Accommodations.AccommodationsClient>();
builder.Services.AddScoped<TravelTracker.Frontend.Api.Users.UsersClient>();
builder.Services.AddScoped<TravelTracker.Frontend.Services.AdminAccessState>();
builder.Services.AddScoped<TravelTracker.Frontend.Api.Settings.SettingsClient>();
builder.Services.AddScoped<TravelTracker.Frontend.Services.UserPreferences>();

var auth0 = Auth0Settings.TryRead(builder.Configuration);
builder.Services.AddSingleton(new AuthSetup(auth0));
var startupLogger = LoggerFactory.Create(logging => logging.AddConfiguration(builder.Configuration.GetSection("Logging")).AddConsole()).CreateLogger("TravelTracker.Frontend.Auth");
if (auth0 is null)
{
    // no schemes registered: every request stays anonymous and navigation keeps working
    builder.Services.AddAuthentication();
    startupLogger.LogWarning("Auth0 is not configured; sign-in is disabled until Auth0:Domain, ClientId and Audience are set");
}
else
{
    startupLogger.LogInformation("Auth0 sign-in configured for {Authority} with audience {Audience}", auth0.Authority, auth0.Audience);
    builder.Services.AddAuth0(auth0, startupLogger);
}
builder.Services.AddAuthorization();

var app = builder.Build();

foreach (var (name, url) in serviceUrls)
    app.Logger.LogInformation("Configured service {Service} at {Url}", name, url);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
if (auth0 is not null)
    app.MapAuthEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

/** Exposes the entry point to the integration tests. */
public partial class Program;
