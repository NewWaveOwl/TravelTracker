using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using TravelTracker.Frontend.Api.Users;

namespace TravelTracker.Frontend.Tests.Support;

/** Test-only owner-scoped HTTP storage for account journeys; it does not replace real API or PostgreSQL validation. */
public sealed class AccountWorkflowBackend : HttpMessageHandler
{
    readonly ConcurrentDictionary<string, SelfUserProfile> profiles = new();
    readonly ConcurrentDictionary<string, byte[]> pictures = new();
    int pictureReads, pictureWrites;

    public HttpStatusCode? ProfileWriteFailure { get; set; }
    public HttpStatusCode? PictureWriteFailure { get; set; }
    public int PictureReads => pictureReads;
    public int PictureWrites => pictureWrites;

    /**
     * Exchanges simulated public Users contracts for the subject in the fixture token.
     * @param request The frontend's authenticated HTTP request.
     * @param cancellationToken Cancels request-body reads.
     * @return A simulated profile or picture response; token validation remains the production frontend's responsibility.
     */
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: { } token } ||
            !new JsonWebTokenHandler().CanReadToken(token)) return new(HttpStatusCode.Unauthorized);
        var subject = new JsonWebToken(token).Subject;
        if (!Guid.TryParse(subject, out var publicId)) return new(HttpStatusCode.Unauthorized);
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/v1/users/me/picture")
        {
            if (request.Method == HttpMethod.Put)
            {
                Interlocked.Increment(ref pictureWrites);
                if (PictureWriteFailure is { } failure) return new(failure);
                pictures[subject] = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                return new(HttpStatusCode.NoContent);
            }
            Interlocked.Increment(ref pictureReads);
            if (!pictures.TryGetValue(subject, out var picture)) return new(HttpStatusCode.NotFound);
            return new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(picture) { Headers = { ContentType = new("image/png") } },
                Headers = { CacheControl = new() { Private = true, NoStore = true } }
            };
        }
        if (path != "/api/v1/users/me") return new(HttpStatusCode.NotFound);
        var profile = profiles.GetOrAdd(subject, _ => new(publicId, "traveller", "active", "fixture_traveller",
            "legacy-contact@example.test", "1 Fixture Street"));
        if (request.Method == HttpMethod.Patch)
        {
            if (ProfileWriteFailure is { } failure) return new(failure);
            var fields = await request.Content!.ReadFromJsonAsync<Dictionary<string, string?>>(cancellationToken);
            if (fields is null || fields.Keys.Any(key => key is not ("username" or "email" or "address")))
                return new(HttpStatusCode.BadRequest);
            profile = profile with
            {
                Username = fields.GetValueOrDefault("username", profile.Username),
                Email = fields.GetValueOrDefault("email", profile.Email),
                Address = fields.GetValueOrDefault("address", profile.Address)
            };
            profiles[subject] = profile;
        }
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(profile) };
    }
}
