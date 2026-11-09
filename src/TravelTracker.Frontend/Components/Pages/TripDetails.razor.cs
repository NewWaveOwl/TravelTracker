using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Components.Shared;

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
    bool loading = true, justSaved;
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
     * @param state The latest validated authentication state.
     */
    void AuthenticationChanged(Task<AuthenticationState> state) => _ = InvokeAsync(async () =>
    {
        trip = null;
        justSaved = false;
        await LoadAsync();
        StateHasChanged();
    });

    async Task LoadAsync()
    {
        request?.Cancel();
        request?.Dispose();
        var current = request = new CancellationTokenSource();
        loading = true;
        failure = null;
        try
        {
            var loaded = await Trips.GetAsync(TripId, current.Token);
            if (current != request) return;
            trip = loaded;
            justSaved = Saved;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { return; }
        catch (ApiError error)
        {
            if (current != request) return;
            trip = null;
            failure = error;
            Logger.LogWarning("Reading trip {TripId} failed as {Kind} (status {Status})", TripId, error.Kind, error.Status);
        }
        loading = false;
    }

    /**
     * shows a stored time with the offset the traveller entered, never shifted to the viewer's zone.
     * @param time A stored trip time.
     * @return The local clock time and its offset.
     */
    static string When(DateTimeOffset time) => time.ToString("yyyy-MM-dd HH:mm zzz", System.Globalization.CultureInfo.InvariantCulture);

    /** cancels a read that is still running when the view closes. */
    public void Dispose()
    {
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        request?.Cancel();
        request?.Dispose();
    }
}
