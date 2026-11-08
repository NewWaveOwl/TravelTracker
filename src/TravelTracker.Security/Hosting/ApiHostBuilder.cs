using Microsoft.AspNetCore.Builder;

namespace TravelTracker.Security.Hosting;

/**
 * creates the web host builder shared by every TravelTracker API host.
 */
public static class ApiHostBuilder
{
    /**
     * creates a builder whose content root is the application's own folder, not the working directory.
     * without this, starting a built DLL from another folder skips appsettings.json and appsettings.{Environment}.json.
     * @param args host arguments, including any --environment or --urls overrides.
     * @return a builder ready for service registration.
     */
    public static WebApplicationBuilder Create(string[] args) => WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });
}
