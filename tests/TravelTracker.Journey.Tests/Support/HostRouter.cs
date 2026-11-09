namespace TravelTracker.Journey.Tests.Support;

/**
 * sends each outbound request to the in-process host that owns its name, so hosts reach each other without sockets.
 * unknown names fail loudly instead of reaching a real network.
 */
internal sealed class HostRouter : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpMessageHandler>> routes = new(StringComparer.OrdinalIgnoreCase);

    /**
     * registers the handler that serves a host name.
     * @param host the host name requests are addressed to.
     * @param resolve returns the handler, evaluated at request time so hosts may start later.
     */
    internal void Route(string host, Func<HttpMessageHandler> resolve) => routes[host] = resolve;

    /**
     * forwards the request to the matching in-process handler.
     * @param request the outbound request.
     * @param cancellationToken cancels the request.
     * @return the response from the owning host.
     */
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var host = request.RequestUri!.Host;
            if (!routes.TryGetValue(host, out var resolve))
                throw new InvalidOperationException($"journey router has no host named {host}; refusing to use the network.");
            using var invoker = new HttpMessageInvoker(resolve(), disposeHandler: false);
            return await invoker.SendAsync(request, cancellationToken);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"journey router failed for {request.Method} {request.RequestUri}: {exception}");
            throw;
        }
    }
}
