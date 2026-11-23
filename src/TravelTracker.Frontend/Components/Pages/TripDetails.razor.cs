using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Components.Shared;
using TravelTracker.Frontend.Auth;

namespace TravelTracker.Frontend.Components.Pages;

/** Reopens one of the caller's own saved trips from the Trips service, so the details always come from stored data. */
public sealed partial class TripDetails : IDisposable
{
    [Inject] TripsClient Trips { get; set; } = default!;
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] ILogger<TripDetails> Logger { get; set; } = default!;

    /** Public identifier of the trip to show. */
    [Parameter] public Guid TripId { get; set; }

    /** Set by the new-trip page after the service confirmed a save; only shown once the trip itself has loaded. */
    [SupplyParameterFromQuery(Name = "saved")] public bool Saved { get; set; }

    TripDto? trip;
    ApiError? failure;
    bool loading = true, justSaved, canImport, disposed;
    Guid loadedId;
    CancellationTokenSource? request;

    FeedbackKind FailureFeedback => failure?.Status == 404 ? FeedbackKind.Empty : failure?.Kind switch
    {
        FailureKind.Permission => FeedbackKind.Permission,
        FailureKind.Network => FeedbackKind.Network,
        FailureKind.Unavailable => FeedbackKind.Unavailable,
        _ => FeedbackKind.Unexpected,
    };

    string? FailureTitle => failure?.Status == 404 ? "Trip not found" : null;

    bool CanRetry => failure is { Kind: FailureKind.Network or FailureKind.Unavailable or FailureKind.Unexpected } && failure.Status != 404;

    protected override async Task OnParametersSetAsync()
    {
        if (loadedId == TripId) return;
        loadedId = TripId;
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        Authentication.AuthenticationStateChanged += AuthenticationChanged;
        await LoadAsync();
    }

    /**
     * drops the loaded trip when the signed-in identity changes, then asks again as the new identity.
     * @param _state The latest validated authentication state, unused because the view reloads as the current identity.
     */
    void AuthenticationChanged(Task<AuthenticationState> _state) => _ = AuthenticationChangedAsync();

    /**
     * Reloads the trip as the new identity; failures are logged instead of left unobserved.
     * @return Completion of the reset and the reload.
     */
    async Task AuthenticationChangedAsync()
    {
        if (disposed) return;
        try
        {
            await InvokeAsync(async () =>
            {
                if (disposed) return;
                trip = null;
                justSaved = false;
                await LoadAsync();
                StateHasChanged();
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.LogError("Trip details account change failed ({FailureType})", exception.GetType().Name);
        }
    }

    async Task LoadAsync()
    {
        // a queued account change can arrive after disposal, when the request source is already closed
        if (disposed) return;
        request?.Cancel();
        request?.Dispose();
        var current = request = new CancellationTokenSource();
        loading = true;
        failure = null;
        try
        {
            var user = (await Authentication.GetAuthenticationStateAsync()).User;
            canImport = user.FindAll(AccessTokenClaims.GrantedScope).SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Contains("trips.write", StringComparer.Ordinal);
            var loaded = await Trips.GetAsync(TripId, current.Token);
            if (current != request) return;
            trip = loaded;
            justSaved = Saved;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            return;
        }
        catch (ApiError error)
        {
            if (current != request) return;
            trip = null;
            failure = error;
            Logger.LogWarning("Reading a trip failed as {Kind} (status {Status})", error.Kind, error.Status);
        }
        loading = false;
    }

    /**
     * shows a stored time with the offset the traveller entered, never shifted to the viewer's zone.
     * @param time A stored trip time.
     * @return The local clock time and its offset.
     */
    static string When(DateTimeOffset time) => time.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);

    /** cancels a read that is still running when the view closes. */
    public void Dispose()
    {
        disposed = true;
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        request?.Cancel();
        request?.Dispose();
    }
}
