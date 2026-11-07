using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TravelTracker.Api.References;
using TravelTracker.Api.Tests.Support;
using TravelTracker.Api.Trips;

namespace TravelTracker.Api.Tests.Trips;

/**
 * Proves the DI registration resolves a working graph and that invalid TripReferences settings fail validation.
 */
public sealed class TripReferenceRegistrationTests {
    /**
     * The registered validator and save service resolve and complete a valid save through the configured clients.
     */
    [Fact]
    public async Task Registration_ResolvesSaveServiceThatCompletesAValidSave() {
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
    public void Options_TimeoutDefaultsToFiveSeconds() {
        using var provider = TripFixture.CreateProvider(new StubUpstream(), new LogSink());

        Assert.Equal(TimeSpan.FromSeconds(5), provider.GetRequiredService<IOptions<TripReferenceOptions>>().Value.Timeout);
    }

    /**
     * A configured timeout replaces the default.
     */
    [Fact]
    public void Options_ConfiguredTimeoutReplacesDefault() {
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
    public void Options_InvalidSetting_FailsValidationWhenResolved(string key, string? value, string expectedFragment) {
        using var provider = TripFixture.CreateProvider(new StubUpstream(), new LogSink(),
            overrides: new Dictionary<string, string?> { [key] = value });

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<TripSaveService>());
        Assert.Contains(expectedFragment, error.Message);
    }

    /**
     * A rejected credential is never echoed back in the validation failure.
     */
    [Fact]
    public void Options_InvalidToken_FailureNeverEchoesTheValue() {
        using var provider = TripFixture.CreateProvider(new StubUpstream(), new LogSink(),
            overrides: new Dictionary<string, string?> { ["TripReferences:UsersServiceAccessToken"] = "leaked value" });

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<TripSaveService>());
        Assert.DoesNotContain("leaked value", error.Message);
    }
}
