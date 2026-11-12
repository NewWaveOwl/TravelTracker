using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Components.Layout;
using TravelTracker.Frontend.Components.Shared;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

public sealed class LayoutTests : BunitContext
{
    public LayoutTests()
    {
        Services.AddSingleton(new ServiceDirectory(new Dictionary<string, string> { ["Attractions"] = "http://attractions" }));
        Services.AddSingleton(new ServiceClient(new HttpClient(StubHandler.Status(503)), NullLogger<ServiceClient>.Instance));
        Services.AddLogging();
        var sessions = new TravelTracker.Frontend.Auth.SessionRegistry(TimeProvider.System);
        Services.AddSingleton(sessions);
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        Services.AddSingleton<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(new SwitchableAuthentication(sessions));
        Services.AddScoped<ProtectedClient>();
        Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Services.AddScoped<TravelTracker.Frontend.Api.Users.UsersClient>();
        Services.AddScoped<TravelTracker.Frontend.Services.AdminAccessState>();
    }

    [Fact]
    public void KeepsNavigationUsableAfterAnUnavailableService()
    {
        var layout = Render<MainLayout>(parameters => parameters.Add(page => page.Body, (RenderFragment)(builder =>
        {
            builder.OpenComponent<HealthCheck>(0);
            builder.CloseComponent();
        })));

        layout.WaitForAssertion(() => Assert.Equal("Service unavailable", layout.Find("h2").TextContent));
        var links = layout.FindAll("nav[aria-label='Main navigation'] a");
        Assert.Equal(new[] { "map", "trip", "history" }, links.Select(link => link.GetAttribute("href")));
        Assert.Equal("Map", links[0].TextContent);
    }

    [Fact]
    public void OpensTheLabelledMenuAndLinksToServiceStatus()
    {
        var header = Render<SiteHeader>();

        header.Find("button[aria-label='Menu']").Click();

        Assert.Equal("services", header.Find("#menu a[href='services']").GetAttribute("href"));
        Assert.Equal("true", header.Find("button[aria-label='Menu']").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void MarksTheCurrentPageInMainNavigation()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("history");

        var header = Render<SiteHeader>();

        Assert.Equal("page", header.Find("a[href='history']").GetAttribute("aria-current"));
        Assert.Null(header.Find("a[href='trip']").GetAttribute("aria-current"));
    }

    [Theory]
    [InlineData("trip")]
    [InlineData("new-trip")]
    [InlineData("new-trip/form")]
    [InlineData("trips/90dccf9b-ac00-4daf-8ca4-097fb263cc3d")]
    public void TripNavigationRemainsActiveForCurrentAndLegacyRoutes(string path)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        var header = Render<SiteHeader>();
        Assert.Equal("page", header.Find("a[href='trip']").GetAttribute("aria-current"));
        Assert.Equal("Trip", header.Find("a[href='trip']").TextContent);
        header.Find("button[aria-label=Menu]").Click();
        Assert.Equal("Trip", header.Find("#menu a[href='trip']").TextContent);
    }
}
