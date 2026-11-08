using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace TravelTracker.Api.References;

/**
 * Builds bearer-authenticated requests to other services. Sending and answer mapping live in ReferenceSender.
 */
public static class ReferenceRequest {
    /**
     * Builds a GET request with a bearer credential.
     *
     * @param path Path relative to the client's base address, without a leading slash.
     * @param bearerToken Credential to send in the Authorization header.
     * @return A request that is sent once.
     */
    public static HttpRequestMessage Get(string path, string bearerToken) =>
        WithBearer(new HttpRequestMessage(HttpMethod.Get, path), bearerToken);

    /**
     * Builds a POST request with a JSON body and a bearer credential.
     *
     * @param path Path relative to the client's base address, without a leading slash.
     * @param bearerToken Credential to send in the Authorization header.
     * @param body The value serialized as JSON.
     * @return A request that is sent once.
     */
    public static HttpRequestMessage Post<TBody>(string path, string bearerToken, TBody body) =>
        WithBearer(new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }, bearerToken);

    /**
     * Adds the bearer Authorization header.
     *
     * @param request The request to decorate.
     * @param bearerToken The credential to send.
     * @return The same request.
     */
    private static HttpRequestMessage WithBearer(HttpRequestMessage request, string bearerToken) {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return request;
    }
}
