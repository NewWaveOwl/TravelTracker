using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
namespace TravelTracker.Transport.Api.Tests;

/**
 * Creates an isolated HTTP host and store for each test.
 */
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /**
     * Configures an isolated test host.
     * @param builder The isolated test host builder.
     */
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}
