using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TravelTracker.Api.References;
using TravelTracker.Api.Tests.Support;
using TravelTracker.Api.Trips;

namespace TravelTracker.Api.Tests.Trips;

/**
 * Proves the DI registration resolves a working graph and that invalid TripReferences settings fail validation.
 */
public sealed class TripReferenceRegistrationTests
{
    /**
     * The registered validator and save service resolve and complete a valid save through the configured clients.
     */
    [Fact]
    public async Task Registration_ResolvesSaveServiceThatCompletesAValidSave()
    {
        var repository = new FakeTripRepository();
        using var provider = TripFixture.CreateProvider(TripFixture.HealthyUpstream(), new LogSink(), repository);

        Assert.NotNull(provider.GetRequiredService<TripReferenceValidator>());
        var outcome = await provider.GetRequiredService<TripSaveService>()
            .SaveAsync(TripFixture.Caller, TripFixture.CallerToken, TripFixture.OneWayDraft(), CancellationToken.None);

        Assert.Equal(TripReferenceStatus.Ready, outcome.Status);
        Assert.Single(repository.Stored);
    }

    /**
     * The per-call timeout defaults to five seconds when the section does not set one.
     */
    [Fact]
    public void Options_TimeoutDefaultsToFiveSeconds()
    {
        using var provider = TripFixture.CreateProvider(new StubUpstream(), new LogSink());

        Assert.Equal(TimeSpan.FromSeconds(5), provider.GetRequiredService<IOptions<TripReferenceOptions>>().Value.Timeout);
    }

    /**
     * A configured timeout replaces the default.
     */
    [Fact]
    public void Options_ConfiguredTimeoutReplacesDefault()
    {
        using var provider = TripFixture.CreateProvider(new StubUpstream(), new LogSink(),
            overrides: new Dictionary<string, string?> { ["TripReferences:Timeout"] = "00:00:02" });

        Assert.Equal(TimeSpan.FromSeconds(2), provider.GetRequiredService<IOptions<TripReferenceOptions>>().Value.Timeout);
    }

    /**
     * Each invalid or missing setting fails validation with a message that names the setting.
     *
     * @param key The configuration key to override.
     * @param value The invalid value; null removes it.
     * @param expectedFragment Text the failure message must contain.
     */
    [Theory]
    [InlineData("TripReferences:UsersServiceAccessToken", null, "UsersServiceAccessToken")]
    [InlineData("TripReferences:UsersServiceAccessToken", "", "UsersServiceAccessToken")]
    [InlineData("TripReferences:UsersServiceAccessToken", "two words", "UsersServiceAccessToken")]
    [InlineData("TripReferences:UsersBaseUrl", "http://users.test/", "UsersBaseUrl must be an absolute HTTPS URL")]
    [InlineData("TripReferences:UsersBaseUrl", "/relative", "UsersBaseUrl")]
    [InlineData("TripReferences:AccommodationsBaseUrl", "ftp://accommodations.test/", "AccommodationsBaseUrl")]
    [InlineData("TripReferences:AttractionsBaseUrl", null, "AttractionsBaseUrl")]
    [InlineData("TripReferences:TransportBaseUrl", "not a url", "TransportBaseUrl")]
    [InlineData("TripReferences:Timeout", "00:00:00", "Timeout")]
    [InlineData("TripReferences:Timeout", "00:01:01", "Timeout")]
    public void Options_InvalidSetting_FailsValidationWhenResolved(string key, string? value, string expectedFragment)
    {
        using var provider = TripFixture.CreateProvider(new StubUpstream(), new LogSink(),
            overrides: new Dictionary<string, string?> { [key] = value });

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<TripSaveService>());
        Assert.Contains(expectedFragment, error.Message);
    }

    /**
     * The timeout limits are inclusive: a value just above zero and exactly sixty seconds are accepted.
     *
     * @param timeout The accepted boundary value.
     */
    [Theory]
    [InlineData("00:00:01")]
    [InlineData("00:01:00")]
    public void Options_TimeoutAtTheBoundary_IsAccepted(string timeout)
    {
        using var provider = TripFixture.CreateProvider(new StubUpstream(), new LogSink(),
            overrides: new Dictionary<string, string?> { ["TripReferences:Timeout"] = timeout });

        Assert.Equal(TimeSpan.Parse(timeout), provider.GetRequiredService<IOptions<TripReferenceOptions>>().Value.Timeout);
    }

    /**
     * Plain http is accepted for the local catalogue hosts but is logged as a warning that names the service, never the address.
     */
    [Fact]
    public void Options_PlainHttpCatalogue_IsAcceptedButWarned()
    {
        var logs = new LogSink();
        using var provider = TripFixture.CreateProvider(new StubUpstream(), logs,
            overrides: new Dictionary<string, string?> { ["TripReferences:TransportBaseUrl"] = "http://transport.test:5105" });

        _ = provider.GetRequiredService<IOptions<TripReferenceOptions>>().Value;

        var warning = Assert.Single(logs.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains("Transport", warning.Message);
        Assert.DoesNotContain("transport.test", warning.Message);
    }

    /**
     * A rejected credential is never echoed back in the validation failure.
     */
    [Fact]
    public void Options_InvalidToken_FailureNeverEchoesTheValue()
    {
        using var provider = TripFixture.CreateProvider(new StubUpstream(), new LogSink(),
            overrides: new Dictionary<string, string?> { ["TripReferences:UsersServiceAccessToken"] = "leaked value" });

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<TripSaveService>());
        Assert.DoesNotContain("leaked value", error.Message);
    }
}
