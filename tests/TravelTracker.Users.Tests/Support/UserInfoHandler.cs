using System.Net;
using System.Net.Http.Json;

namespace TravelTracker.Users.Tests.Support;

/** Controlled provider response; does not bypass the API's actual JWT or email-binding checks. */
public sealed class UserInfoHandler : HttpMessageHandler
{
    public string Subject { get; set; } = "admin-subject";
    public string Email { get; set; } = UsersApiFactory.Email;
    public bool Verified { get; set; } = true;
    public string? RawResponse { get; set; }
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public bool Unavailable { get; set; }
    public int Calls { get; private set; }
    public Uri? Destination { get; private set; }
    public string? Authorization { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++; Destination = request.RequestUri; Authorization = request.Headers.Authorization?.ToString();
        if (Unavailable) throw new HttpRequestException("provider-secret-diagnostic");
        return Task.FromResult(new HttpResponseMessage(Status)
        { Content = RawResponse is null ? JsonContent.Create(new { sub = Subject, email = Email, email_verified = Verified }) : new StringContent(RawResponse) });
    }
}
