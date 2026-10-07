using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace TravelTracker.Api.Tests;

/**
 * Creates an isolated in-memory HTTP host for each test.
 */
internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    /**
     * Configures the test host.
     * @param builder The isolated host builder.
     */
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}
