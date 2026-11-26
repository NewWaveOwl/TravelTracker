using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TravelTracker.Frontend.Tests.Support;

/** Answers the Users API login and register calls with a signed token, without network access. */
public sealed class FakeUsersAuth(TimeProvider clock) : HttpMessageHandler
{
    public const string Subject = "c614a53c-1ff7-43b3-bab0-37a68c806a72";
    readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /** Seconds the issued token lives; zero or less issues one that has already expired. */
    public int ExpiresInSeconds { get; set; } = 3600;
    public string Scope { get; set; } = "users.read users.write catalogue.read trips.read trips.write";
    public HttpStatusCode? Reject { get; set; }
    public bool Unreachable { get; set; }
    public int LoginRequests { get; private set; }
    public int RegisterRequests { get; private set; }
    public string? LastUsername { get; private set; }
    public string? LastToken { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        if (Unreachable) throw new HttpRequestException("users-service-private-diagnostic");
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/v1/auth/login") LoginRequests++;
        else if (path == "/api/v1/auth/register") RegisterRequests++;
        else return new HttpResponseMessage(HttpStatusCode.NotFound);
        var submitted = await request.Content!.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
        LastUsername = submitted!["username"];
        if (Reject is { } status) return new HttpResponseMessage(status) { Content = new StringContent("private-provider-diagnostic") };

        var now = clock.GetUtcNow().UtcDateTime;
        LastToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity([new("sub", Subject), new("scope", Scope), new("jti", Guid.NewGuid().ToString())]),
            NotBefore = now.AddHours(-1),
            IssuedAt = now.AddHours(-1),
            Expires = now.AddSeconds(ExpiresInSeconds),
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(key), SecurityAlgorithms.EcdsaSha256),
        });
        return new HttpResponseMessage(path.EndsWith("register") ? HttpStatusCode.Created : HttpStatusCode.OK)
        { Content = JsonContent.Create(new { accessToken = LastToken, expiresIn = ExpiresInSeconds, scope = Scope, tokenType = "Bearer" }) };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) key.Dispose();
        base.Dispose(disposing);
    }
}
