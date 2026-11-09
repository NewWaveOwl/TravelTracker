using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace TravelTracker.Journey.Tests.Support;

/**
 * one real TravelTracker API host with production wiring, its own PostgreSQL database and the shared journey identity.
 * outbound HTTP (issuer discovery, Users, catalogue references) goes through the in-process router instead of the network.
 * @param settings host specific configuration, such as the connection string and upstream addresses.
 * @param router resolves a host name to the in-process handler that serves it.
 * @typeparam TProgram the host's entry point type.
 */
internal sealed class JourneyHost<TProgram>(Dictionary<string, string?> settings, HostRouter router) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    /**
     * applies the shared issuer, the host settings and the router to the production host.
     * @param builder the host builder under test.
     */
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // hosts such as Trips read their connection string while composing services, before configuration callbacks run, so host settings must be set directly
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Bearer:Authority"] = JourneyIdentity.Issuer,
                ["Authentication:Bearer:Issuer"] = JourneyIdentity.Issuer,
                ["Authentication:Bearer:Audience"] = JourneyIdentity.Audience,
                ["Authentication:Bearer:ScopeClaimType"] = "scope",
                ["Authentication:Bearer:ClockSkewSeconds"] = "0",
                ["Authentication:Bearer:ValidAlgorithms:0"] = SecurityAlgorithms.RsaSha256,
                ["Database:ApplyMigrations"] = "true",
                ["Database:SeedExamples"] = "false"
            });
            // added last so the host's own settings win over the shared defaults above
            configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureTestServices(services =>
        {
            // hosts configure their own primary handlers, so the router is forced in after every client's builder has run
            services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new RouterFilter(router));
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.Backchannel = new HttpClient(router));
        });
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            // set JOURNEY_LOGS=1 to see every host's warnings and errors while diagnosing a failure
            if (Environment.GetEnvironmentVariable("JOURNEY_LOGS") == "1") logging.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });
    }
}

/**
 * replaces every named or typed client's primary handler with the in-process router, after the host's own configuration ran.
 * @param router the in-process router.
 */
internal sealed class RouterFilter(HostRouter router) : IHttpMessageHandlerBuilderFilter
{
    /**
     * wraps the host's builder configuration so the router is the last word on the primary handler.
     * @param next the host's own builder configuration.
     * @return the configuration that ends with the router in place.
     */
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        builder.PrimaryHandler = router;
    };
}
