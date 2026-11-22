using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

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
    public static WebApplicationBuilder Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });
        // API handlers record failure categories. Provider exception text can carry database or submitted values.
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None)
            .AddFilter("Npgsql", LogLevel.None)
            .AddFilter("Microsoft.AspNetCore.Authentication.JwtBearer", LogLevel.None)
            .AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        return builder;
    }
}
