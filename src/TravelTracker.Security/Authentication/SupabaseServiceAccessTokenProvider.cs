using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TravelTracker.Security.Authentication;

/**
 * Caches a Supabase service-account JWT and refreshes it automatically before expiry, falling back to a static token when refresh credentials are absent.
 */
public sealed class SupabaseServiceAccessTokenProvider : IServiceAccessTokenProvider, IDisposable
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly IConfiguration _configuration;
    private readonly string _staticTokenKey;
    private readonly ILogger<SupabaseServiceAccessTokenProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /**
     * Creates a Supabase service-token provider backed by configuration and a dedicated or supplied HTTP client.
     *
     * @param configuration Deployment configuration containing Supabase refresh or static token keys.
     * @param staticTokenKey Configuration key for the fallback static service bearer token.
     * @param logger Safe diagnostics logger that omits tokens, passwords and provider bodies.
     * @param httpClient Optional HTTP client for deterministic tests; when null, a non-redirecting client is created and owned.
     * @param timeProvider Optional clock for deterministic lifetime calculations; defaults to system UTC time.
     */
    public SupabaseServiceAccessTokenProvider(
        IConfiguration configuration,
        string staticTokenKey,
        ILogger<SupabaseServiceAccessTokenProvider> logger,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        _configuration = configuration;
        _staticTokenKey = staticTokenKey;
        _logger = logger;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        })
        {
            Timeout = RequestTimeout
        };
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /**
     * Checks whether complete HTTPS Supabase service-account refresh credentials are configured.
     *
     * @param configuration Deployment configuration to inspect.
     * @return True when URL, publishable key, service email and service password are all present and valid for HTTPS refresh.
     */
    public static bool HasRefreshCredentials(IConfiguration configuration)
    {
        var url = configuration["Supabase:Url"];
        var publishableKey = configuration["Supabase:PublishableKey"];
        var email = configuration["Supabase:ServiceEmail"];
        var password = configuration["Supabase:ServicePassword"];
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && !string.IsNullOrWhiteSpace(publishableKey)
            && !string.IsNullOrWhiteSpace(email)
            && !string.IsNullOrWhiteSpace(password);
    }

    /**
     * Returns a valid bearer token, refreshing it under a single-flight lock when within five minutes of expiry.
     *
     * @param cancellationToken Cancels waiting or the upstream refresh request.
     * @return A task containing a single-token bearer credential, or null when neither refresh nor static credentials yield one.
     */
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (!HasRefreshCredentials(_configuration))
            return ReadStaticToken();

        var now = _timeProvider.GetUtcNow();
        var current = _cachedToken;
        if (!string.IsNullOrWhiteSpace(current) && now < _expiresAt - RefreshMargin)
            return current;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (!string.IsNullOrWhiteSpace(_cachedToken) && now < _expiresAt - RefreshMargin)
                return _cachedToken;

            var refreshed = await RefreshAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(refreshed))
                return refreshed;

            // If refresh failed transiently while the cached token is still unexpired, continue using it.
            if (!string.IsNullOrWhiteSpace(_cachedToken) && now < _expiresAt)
                return _cachedToken;

            return ReadStaticToken();
        }
        finally
        {
            _gate.Release();
        }
    }

    private string? ReadStaticToken()
    {
        var token = _configuration[_staticTokenKey];
        return string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace) ? null : token;
    }

    private async Task<string?> RefreshAsync(CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["Supabase:Url"]!.TrimEnd('/');
        var publishableKey = _configuration["Supabase:PublishableKey"]!;
        var email = _configuration["Supabase:ServiceEmail"]!;
        var password = _configuration["Supabase:ServicePassword"]!;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/auth/v1/token?grant_type=password")
            {
                Content = JsonContent.Create(new { email, password })
            };
            request.Headers.Add("apikey", publishableKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Supabase service token refresh failed with HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken) || payload.AccessToken.Any(char.IsWhiteSpace))
            {
                _logger.LogWarning("Supabase service token refresh returned an invalid access token.");
                return null;
            }

            var lifetimeSeconds = payload.ExpiresIn > 60 ? payload.ExpiresIn : 3600;
            _cachedToken = payload.AccessToken;
            _expiresAt = _timeProvider.GetUtcNow().AddSeconds(lifetimeSeconds);
            _logger.LogInformation("Refreshed Supabase service access token (valid for {LifetimeSeconds}s).", lifetimeSeconds);
            return _cachedToken;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Supabase service token refresh failed due to a transport or parsing error.");
            return null;
        }
    }

    /**
     * Releases the synchronization gate and any internally created HTTP client.
     */
    public void Dispose()
    {
        _gate.Dispose();
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
