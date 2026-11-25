using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TravelTracker.Security.Authentication;

/**
 * Dependency-injection helpers for registering the internal Users service access-token provider.
 */
public static class ServiceAccessTokenExtensions
{
    /**
     * Registers a singleton service-token provider that refreshes from Supabase when configured or falls back to a static token key.
     *
     * @param services The host service collection.
     * @param staticTokenKey Configuration key holding a fallback static bearer token.
     * @param configuration Optional explicit configuration instance; defaults to the container's IConfiguration.
     * @return The service collection for chaining.
     */
    public static IServiceCollection AddServiceAccessTokenProvider(
        this IServiceCollection services,
        string staticTokenKey = "Users:ServiceAccessToken",
        IConfiguration? configuration = null)
    {
        services.AddSingleton<IServiceAccessTokenProvider>(provider =>
            new SupabaseServiceAccessTokenProvider(
                configuration ?? provider.GetRequiredService<IConfiguration>(),
                staticTokenKey,
                provider.GetRequiredService<ILogger<SupabaseServiceAccessTokenProvider>>(),
                timeProvider: provider.GetService<TimeProvider>()));
        return services;
    }
}
