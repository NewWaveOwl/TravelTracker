using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Components;

var builder = WebApplication.CreateBuilder(args);

var serviceUrls = builder.Configuration.GetSection("Services").Get<Dictionary<string, string>>()
    ?? throw new InvalidOperationException("The Services configuration section is missing.");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(new ServiceDirectory(serviceUrls));
builder.Services.AddHttpClient<ServiceClient>();

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
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
