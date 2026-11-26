using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Frontend.Api;

namespace TravelTracker.Frontend.Tests;

/** Exercises the checked-in service configuration through the real frontend startup. */
public sealed class FrontendStartupTests
{
    [Fact]
    public async Task ProductionStartsWithSixCoreServicesAndNoRoutingOverride()
    {
        using var host = Host("Production");
        using var browser = host.CreateClient(new() { BaseAddress = new("https://localhost") });

        using var response = await browser.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Routing", host.Services.GetRequiredService<ServiceDirectory>().Names);
    }

    [Fact]
    public void DevelopmentUsesTheLocalRoutingDefault()
    {
        using var host = Host("Development");
        var environment = host.Services.GetRequiredService<IWebHostEnvironment>();
        using var configuration = new ConfigurationManager();
        configuration.SetBasePath(environment.ContentRootPath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true);
        var urls = configuration.GetSection("Services").Get<Dictionary<string, string>>()!;

        ServiceDirectory.Validate(urls, production: false);

        var directory = new ServiceDirectory(urls);
        Assert.Equal("http://localhost:18088/v1/routes/capabilities",
            directory.ProtectedUrlFor("Routing", "/v1/routes/capabilities"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void ExplicitHttpsRoutingOverridesEnvironmentDefaults(string environment)
    {
        using var host = Host(environment, "https://routing.example.test");

        var directory = host.Services.GetRequiredService<ServiceDirectory>();

        Assert.Equal("https://routing.example.test/v1/routes/capabilities",
            directory.ProtectedUrlFor("Routing", "/v1/routes/capabilities"));
    }

    [Fact]
    public void ProductionRejectsAnExplicitLoopbackHttpRoutingUrl()
    {
        using var host = Host("Production", "http://localhost:18088");

        var failure = Assert.Throws<InvalidOperationException>(() => host.CreateClient());

        Assert.Contains("Services:Routing must be a credential-free HTTPS URL", failure.Message);
    }

    /**
     * Loads the real app settings with HTTPS core-service settings.
     * @param environment The host environment selecting its checked-in JSON defaults.
     * @param routing An explicit deployment override, or null to retain the checked-in default.
     * @return An isolated frontend host that never needs external services during these startup checks.
     */
    static WebApplicationFactory<Program> Host(string environment, string? routing = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            var settings = new Dictionary<string, string?>
            {
                ["Services:Accommodations"] = "https://accommodations.example.test",
                ["Services:Attractions"] = "https://attractions.example.test",
                ["Services:Settings"] = "https://settings.example.test",
                ["Services:Transport"] = "https://transport.example.test",
                ["Services:Trips"] = "https://trips.example.test",
                ["Services:Users"] = "https://users.example.test",
            };
            if (routing is not null) settings["Services:Routing"] = routing;
            foreach (var setting in settings) builder.UseSetting(setting.Key, setting.Value);
        });
}
