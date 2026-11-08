using System.Text.RegularExpressions;

namespace TravelTracker.Frontend.Api;

/**
 * Knows the public base URL of every backend service the frontend talks to.
 *
 * @param urls Service names mapped to their public base URLs.
 */
public sealed partial class ServiceDirectory(IReadOnlyDictionary<string, string> urls)
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

    /**
     * Builds the URL for a request that carries an access token, refusing anything that could leak it.
     *
     * @param service The configured service name.
     * @param path A relative versioned API path such as /api/v1/transport-modes/{id}.
     * @return The absolute URL; throws a categorized ApiError for a bad path or an insecure service URL.
     */
    public string ProtectedUrlFor(string service, string path)
    {
        var parts = path.Split('?', 2);
        if (!ProtectedPath().IsMatch(parts[0]) ||
            (parts.Length == 2 && !AllowedQuery(service, parts[0], parts[1])))
            throw new ApiError(FailureKind.Unexpected, "Unsupported API path.");
        if (!urls.TryGetValue(service, out var url) || !Uri.TryCreate(url, UriKind.Absolute, out var baseUri))
            throw new ApiError(FailureKind.Unexpected, "Invalid service configuration.");
        if (baseUri.UserInfo.Length > 0 || baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0)
            throw new ApiError(FailureKind.Unexpected, "Invalid service configuration.");
        if (baseUri.Scheme != Uri.UriSchemeHttps && !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback))
            throw new ApiError(FailureKind.Unexpected, "The API requires HTTPS.");
        return UrlFor(service, path);
    }

    [GeneratedRegex("^/api/v1/[a-z0-9/-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex ProtectedPath();

    [GeneratedRegex("^name=(?:[a-zA-Z0-9._~-]|%[0-9a-fA-F]{2})*$")]
    private static partial Regex NameQuery();

    static bool AllowedQuery(string service, string path, string query) =>
        service == "Attractions" && path == "/api/v1/attractions" && NameQuery().IsMatch(query) ||
        service == "Accommodations" && path == "/api/v1/accommodations" && NameQuery().IsMatch(query) ||
        service == "Transport" && (path == "/api/v1/transport-modes" && TransportQuery().IsMatch(query) || path == "/api/v1/route-points" && PointQuery().IsMatch(query));

    [GeneratedRegex("^name=(?:[a-zA-Z0-9._~-]|%[0-9a-fA-F]{2})*&code=(?:car|bus|train|pedestrian|bike|plane)?$")]
    private static partial Regex TransportQuery();

    [GeneratedRegex("^name=(?:[a-zA-Z0-9._~-]|%[0-9a-fA-F]{2})*&type=(?:accommodation|attraction|transport_stop)?$")]
    private static partial Regex PointQuery();
}
