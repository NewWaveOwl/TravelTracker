namespace TravelTracker.Frontend.Tests.Support;

/** A minimal browser cookie jar: replays every cookie to every request and drops cookies the server expires. */
public sealed class CookieJarHandler : DelegatingHandler
{
    readonly Dictionary<string, string> cookies = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            foreach (var setCookie in setCookies)
                Remember(setCookie);
        return response;
    }

    void Remember(string setCookie)
    {
        var parts = setCookie.Split(';', StringSplitOptions.TrimEntries);
        var pair = parts[0].Split('=', 2);
        var expired = parts.Any(part => part.StartsWith("expires=", StringComparison.OrdinalIgnoreCase) && DateTimeOffset.TryParse(part[8..], out var at) && at < DateTimeOffset.UtcNow);
        if (expired || pair[1].Length == 0)
            cookies.Remove(pair[0]);
        else
            cookies[pair[0]] = pair[1];
    }
}
