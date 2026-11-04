using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace TravelTracker.Attractions.Api.Tests;

/**
 * Simulates unavailable metadata for the post-validation callback's failure-path test.
 */
internal sealed class UnavailableConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
{
    /**
     * Returns a retrieval failure whose sensitive detail must not reach diagnostics.
     * @param cancel Cancels metadata retrieval.
     * @return A failed configuration retrieval task.
     */
    public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
        Task.FromException<OpenIdConnectConfiguration>(new HttpRequestException("sensitive-test-marker"));
    public void RequestRefresh() { }
}
