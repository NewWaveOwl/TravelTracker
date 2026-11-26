using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TravelTracker.Transport.Api.Tests.Support;

/** Provides a controlled Users service boundary for Transport authorization tests. */
internal sealed class UsersAccessBackchannelHandler : HttpMessageHandler
{
    internal static readonly Guid UserId = Guid.Parse("019ac550-1000-7000-8000-000000000099");
    internal Guid ResolvedUserId { get; set; } = UserId;
    internal string Status { get; set; } = "active";
    internal string Role { get; set; } = "administrator";
    internal bool Unavailable { get; set; }
    internal bool InvalidResponse { get; set; }
    internal bool UnknownIdentity { get; set; }
    internal int Calls { get; private set; }
    internal string? LastIssuer { get; private set; }
    internal string? LastSubject { get; private set; }
    internal List<string?> BearerTokens { get; } = [];
    internal List<string> Paths { get; } = [];

    /**
     * Returns scripted identity and account-standing responses for the trusted backend credential.
     * @param request The request sent by the Transport service.
     * @param cancellationToken Cancels request-body reading.
     * @return The scripted Users response.
     */
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Paths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
        BearerTokens.Add(request.Headers.Authorization?.Parameter);
        if (BearerTokens[^1] != ApiFactory.UsersServiceToken)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        if (Unavailable) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/internal/users/identity")
        {
            using var body = await JsonDocument.ParseAsync(await request.Content!.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            LastIssuer = body.RootElement.GetProperty("issuer").GetString();
            LastSubject = body.RootElement.GetProperty("subject").GetString();
            return UnknownIdentity
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonResponse(new { userId = ResolvedUserId });
        }

        if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == $"/internal/users/{ResolvedUserId:D}/access")
        {
            if (InvalidResponse) return JsonResponse(new { status = "active", role = "administrator" });
            return JsonResponse(new { userId = ResolvedUserId, status = Status, role = Role });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /**
     * Creates a JSON response for the isolated HTTP test host.
     * @param body The response object to serialize.
     * @return An HTTP 200 response containing the JSON body.
     */
    private static HttpResponseMessage JsonResponse<T>(T body) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(body)
    };
}
