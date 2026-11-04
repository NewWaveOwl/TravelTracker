namespace TravelTracker.Frontend.Tests.Support;

/**
 * Answers every request from a delegate so tests never touch the network.
 *
 * @param respond Produces the response, or throws to simulate a transport failure.
 */
public sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    public static StubHandler Status(int status, string body = "") =>
        new((_, _) => Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)status) { Content = new StringContent(body) }));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return respond(request, cancellationToken);
    }
}
