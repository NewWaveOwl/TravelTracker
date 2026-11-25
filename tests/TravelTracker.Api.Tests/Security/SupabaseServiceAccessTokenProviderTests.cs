using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelTracker.Api.Tests.Support;
using TravelTracker.Security.Authentication;

namespace TravelTracker.Api.Tests.Security;

/**
 * Verifies automatic Supabase service-token caching, refresh margins, fallback behavior and log redaction.
 */
public sealed class SupabaseServiceAccessTokenProviderTests
{
    /**
     * When Supabase service refresh settings are absent, the configured static bearer token is returned without HTTP calls.
     */
    [Fact]
    public async Task GetTokenAsync_WithoutRefreshCredentials_ReturnsStaticToken()
    {
        var calls = 0;
        using var http = CreateClient(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Users:ServiceAccessToken"] = "static-service-token"
        });
        using var provider = CreateProvider(config, http, new ManualClock(), new LogSink());

        var token = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("static-service-token", token);
        Assert.Equal(0, calls);
    }

    /**
     * Whitespace-corrupted static tokens are rejected rather than returned to downstream Authorization headers.
     */
    [Fact]
    public async Task GetTokenAsync_StaticTokenWithWhitespace_ReturnsNull()
    {
        using var http = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Users:ServiceAccessToken"] = "invalid token with spaces"
        });
        using var provider = CreateProvider(config, http, new ManualClock(), new LogSink());

        var token = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Null(token);
    }

    /**
     * HasRefreshCredentials rejects plain HTTP or URLs carrying user-info, query or fragment components.
     *
     * @param url Candidate Supabase URL.
     * @param expected Whether refresh credentials should be accepted.
     */
    [Theory]
    [InlineData("https://project.supabase.test", true)]
    [InlineData("http://project.supabase.test", false)]
    [InlineData("https://user:pass@project.supabase.test", false)]
    [InlineData("https://project.supabase.test?leak=1", false)]
    [InlineData("https://project.supabase.test#frag", false)]
    public void HasRefreshCredentials_ValidatesCleanHttpsOrigin(string url, bool expected)
    {
        var config = CreateConfiguration(RefreshSettings(url: url));

        Assert.Equal(expected, SupabaseServiceAccessTokenProvider.HasRefreshCredentials(config));
    }

    /**
     * Tokens are cached while outside the five-minute refresh margin and refreshed once the clock enters that window.
     */
    [Fact]
    public async Task GetTokenAsync_CachesUntilFiveMinuteMarginThenRefreshes()
    {
        var calls = 0;
        using var http = CreateClient(request =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://project.supabase.test/auth/v1/token?grant_type=password", request.RequestUri?.ToString());
            Assert.Equal("sb_publishable_fixture", Assert.Single(request.Headers.GetValues("apikey")));
            return JsonResponse(HttpStatusCode.OK, $"{{\"access_token\":\"fresh-token-{calls}\",\"expires_in\":3600}}");
        });
        var clock = new ManualClock();
        using var provider = CreateProvider(CreateConfiguration(RefreshSettings()), http, clock, new LogSink());

        var first = await provider.GetTokenAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(54));
        var cached = await provider.GetTokenAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(2));
        var refreshed = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("fresh-token-1", first);
        Assert.Equal("fresh-token-1", cached);
        Assert.Equal("fresh-token-2", refreshed);
        Assert.Equal(2, calls);
    }

    /**
     * When a proactive refresh fails transiently inside the five-minute margin, a still-unexpired cached token keeps working.
     */
    [Fact]
    public async Task GetTokenAsync_TransientRefreshFailureBeforeExpiry_ReusesUnexpiredCachedToken()
    {
        var calls = 0;
        using var http = CreateClient(_ =>
        {
            calls++;
            return calls == 1
                ? JsonResponse(HttpStatusCode.OK, "{\"access_token\":\"initial-token\",\"expires_in\":3600}")
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("upstream secret detail")
                };
        });
        var clock = new ManualClock();
        var logs = new LogSink();
        using var provider = CreateProvider(CreateConfiguration(RefreshSettings(password: "super-secret-pw")), http, clock, logs);

        var initial = await provider.GetTokenAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(56));
        var fallback = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("initial-token", initial);
        Assert.Equal("initial-token", fallback);
        Assert.All(logs.Entries, entry =>
        {
            Assert.DoesNotContain("super-secret-pw", entry.Message);
            Assert.DoesNotContain("upstream secret detail", entry.Message);
            Assert.DoesNotContain("initial-token", entry.Message);
        });
    }

    /**
     * Once a cached token has passed expiry and upstream refresh fails, the provider falls back to any valid static token without echoing secrets.
     */
    [Fact]
    public async Task GetTokenAsync_RefreshFailureAfterExpiry_FallsBackToStaticTokenAndRedactsTransportErrors()
    {
        using var http = CreateClient(_ => throw new HttpRequestException("Host secret-password-leak unreachable"));
        var settings = RefreshSettings(password: "secret-password-leak");
        settings["Users:ServiceAccessToken"] = "bootstrap-token";
        var logs = new LogSink();
        using var provider = CreateProvider(CreateConfiguration(settings), http, new ManualClock(), logs);

        var token = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("bootstrap-token", token);
        var warning = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain("secret-password-leak", warning.Message);
        Assert.DoesNotContain("bootstrap-token", warning.Message);
    }

    /**
     * TripReferences validation accepts auto-refresh credentials even when the static UsersServiceAccessToken is blank.
     */
    [Fact]
    public void AddTripReferences_WithRefreshCredentialsAndEmptyStaticToken_PassesValidation()
    {
        var overrides = RefreshSettings();
        overrides["TripReferences:UsersServiceAccessToken"] = "";
        using var provider = TripFixture.CreateProvider(new StubUpstream(), new LogSink(), overrides: overrides);

        Assert.NotNull(provider.GetRequiredService<TravelTracker.Api.Trips.TripSaveService>());
        Assert.NotNull(provider.GetRequiredService<IServiceAccessTokenProvider>());
    }

    private static Dictionary<string, string?> RefreshSettings(string url = "https://project.supabase.test", string password = "fixture-service-password") =>
        new()
        {
            ["Supabase:Url"] = url,
            ["Supabase:PublishableKey"] = "sb_publishable_fixture",
            ["Supabase:ServiceEmail"] = "service@example.test",
            ["Supabase:ServicePassword"] = password
        };

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static SupabaseServiceAccessTokenProvider CreateProvider(
        IConfiguration configuration,
        HttpClient httpClient,
        TimeProvider timeProvider,
        LogSink logs)
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new CapturingLoggerProvider(logs)));
        return new SupabaseServiceAccessTokenProvider(
            configuration,
            "Users:ServiceAccessToken",
            loggerFactory.CreateLogger<SupabaseServiceAccessTokenProvider>(),
            httpClient,
            timeProvider);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new DelegatingStubHandler(responder));

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class DelegatingStubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
