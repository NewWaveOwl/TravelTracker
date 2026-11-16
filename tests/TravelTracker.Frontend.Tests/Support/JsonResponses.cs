using System.Net;
using System.Net.Http.Json;

namespace TravelTracker.Frontend.Tests.Support;

/** builds JSON answers for stub handlers so each test file does not repeat the same wrapper. */
public static class JsonResponses
{
    /**
     * Serializes a body into a response with the given status.
     * @param body The value written as the JSON content.
     * @param status The HTTP status of the response.
     * @return A response carrying the JSON body.
     */
    public static HttpResponseMessage Json<T>(T body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(body) };
}
