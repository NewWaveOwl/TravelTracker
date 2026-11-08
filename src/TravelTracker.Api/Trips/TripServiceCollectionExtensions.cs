using TravelTracker.Trips.Data.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TravelTracker.Api.References;

namespace TravelTracker.Api.Trips;

/**
 * Registers the trip reference clients, validator and save service. The host still supplies ITripRepository.
 */
public static class TripServiceCollectionExtensions {
    /**
     * Binds and validates the TripReferences section, then registers typed HTTP clients for the four upstream
     * services and the trip services that use them. Invalid settings fail when the options are first resolved,
     * and ValidateOnStart makes a host fail at startup.
     *
     * @param services The API service registrations.
     * @param configuration Deployment configuration containing the TripReferences section.
     * @return The services for further composition.
     */
    public static IServiceCollection AddTripReferences(this IServiceCollection services, IConfiguration configuration) {
        services.AddOptions<TripReferenceOptions>().Bind(configuration.GetSection(TripReferenceOptions.SectionName))
            .Validate(options => IsAbsoluteUrl(options.AccommodationsBaseUrl, allowHttp: true),
                "TripReferences:AccommodationsBaseUrl must be an absolute http or https URL.")
            .Validate(options => IsAbsoluteUrl(options.AttractionsBaseUrl, allowHttp: true),
                "TripReferences:AttractionsBaseUrl must be an absolute http or https URL.")
            .Validate(options => IsAbsoluteUrl(options.TransportBaseUrl, allowHttp: true),
                "TripReferences:TransportBaseUrl must be an absolute http or https URL.")
            .Validate(options => IsAbsoluteUrl(options.UsersBaseUrl, allowHttp: false),
                "TripReferences:UsersBaseUrl must be an absolute HTTPS URL.")
            .Validate(options => IsBearerToken(options.UsersServiceAccessToken),
                "TripReferences:UsersServiceAccessToken is required and must be one bearer token with no spaces.")
            .Validate(options => options.Timeout > TimeSpan.Zero && options.Timeout <= TimeSpan.FromSeconds(60),
                "TripReferences:Timeout must be greater than zero and at most 60 seconds.")
            .ValidateOnStart();

        services.AddSingleton<ReferenceSender>();
        services.AddHttpClient<AccommodationReferenceClient>((provider, client) =>
            client.BaseAddress = BaseAddressOf(provider, options => options.AccommodationsBaseUrl));
        services.AddHttpClient<AttractionReferenceClient>((provider, client) =>
            client.BaseAddress = BaseAddressOf(provider, options => options.AttractionsBaseUrl));
        services.AddHttpClient<RoutePointReferenceClient>((provider, client) =>
            client.BaseAddress = BaseAddressOf(provider, options => options.TransportBaseUrl));
        services.AddHttpClient<TransportModeReferenceClient>((provider, client) =>
            client.BaseAddress = BaseAddressOf(provider, options => options.TransportBaseUrl));
        services.AddHttpClient<UsersStandingClient>((provider, client) =>
            client.BaseAddress = BaseAddressOf(provider, options => options.UsersBaseUrl));
        services.AddTransient<TripReferenceValidator>();
        services.AddTransient<TripSaveService>();
        return services;
    }

    /**
     * Reads one validated base address from the options, with a trailing slash so relative paths keep their segments.
     *
     * @param provider Resolves the validated options.
     * @param select Picks the base address for one upstream service.
     * @return The absolute base address.
     */
    private static Uri BaseAddressOf(IServiceProvider provider, Func<TripReferenceOptions, string?> select) {
        var address = select(provider.GetRequiredService<IOptions<TripReferenceOptions>>().Value)!;
        return new Uri(address.EndsWith('/') ? address : address + "/");
    }

    /**
     * Checks that a value is an absolute URL with an allowed scheme.
     *
     * @param value The configured value, possibly missing.
     * @param allowHttp Whether plain http is acceptable.
     * @return True when the value is an absolute http or https URL that satisfies the scheme rule.
     */
    private static bool IsAbsoluteUrl(string? value, bool allowHttp) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (allowHttp && uri.Scheme == Uri.UriSchemeHttp));

    /**
     * Checks that a value is present and is one token with no whitespace. The value itself is never echoed in errors.
     *
     * @param value The configured credential, possibly missing.
     * @return True when the credential is a single non-empty token.
     */
    private static bool IsBearerToken(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsWhiteSpace);
}
