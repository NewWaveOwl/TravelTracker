namespace TravelTracker.Frontend.Tests.Support;

/**
 * Answers every request from a delegate so tests never touch the network.
 *
 * @param respond Produces the response, or throws to simulate a transport failure.
 */
public sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond = respond;

    public HttpRequestMessage? LastRequest { get; private set; }
    /** Captures the body before the HTTP client disposes the recorded request. */
    public string? LastRequestBody { get; private set; }

    /** Changes every later answer to this status, for tests that flip a service from healthy to rejecting. */
    public void RespondWith(int status, string body = "") => respond = (_, _) => Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)status) { Content = new StringContent(body) });

    /** Clears the recorded request so a test can assert that nothing new was sent. */
    public void Forget()
    {
        LastRequest = null;
        LastRequestBody = null;
    }

    public static StubHandler Status(int status, string body = "") =>
        new((_, _) => Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)status) { Content = new StringContent(body) }));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return await respond(request, cancellationToken);
    }
}
