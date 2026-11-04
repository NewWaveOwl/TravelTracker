namespace TravelTracker.Frontend.Api;

/**
 * Knows the public base URL of every backend service the frontend talks to.
 *
 * @param urls Service names mapped to their public base URLs.
 */
public sealed class ServiceDirectory(IReadOnlyDictionary<string, string> urls)
{
    public IEnumerable<string> Names => urls.Keys;

    /**
     * Builds the public URL for a path on a service.
     *
     * @param service The configured service name.
     * @param path The path below the service root, such as /health.
     * @return The absolute URL to request.
     */
    public string UrlFor(string service, string path) => $"{urls[service].TrimEnd('/')}/{path.TrimStart('/')}";
}
