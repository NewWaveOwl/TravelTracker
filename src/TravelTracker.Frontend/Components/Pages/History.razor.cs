using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Components.Shared;

namespace TravelTracker.Frontend.Components.Pages;

/** Lists saved trips for the authenticated traveller. */
public partial class History : IDisposable
{
    [Parameter] public bool Embedded { get; set; }
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] TripsClient Trips { get; set; } = default!;

    IReadOnlyList<TripSummaryDto> trips = [];
    CancellationTokenSource? request;
    ApiError? failure;
    bool loading = true, signedIn, disposed;

    FeedbackKind FailureFeedback => failure?.Kind switch
    {
        FailureKind.Network => FeedbackKind.Network,
        FailureKind.Unavailable => FeedbackKind.Unavailable,
        _ => FeedbackKind.Unexpected
    };

    protected override Task OnInitializedAsync()
    {
        Authentication.AuthenticationStateChanged += AuthenticationChanged;
        return ReloadAsync();
    }

    Task ReloadAsync() => LoadAsync(Authentication.GetAuthenticationStateAsync());

    /**
     * Replaces the visible history when the session changes.
     * @param state The replacement authentication state.
     */
    void AuthenticationChanged(Task<AuthenticationState> state) => _ = InvokeAsync(async () =>
    {
        var load = LoadAsync(state);
        if (!disposed) StateHasChanged();
        await load;
        if (!disposed) StateHasChanged();
    });

    /**
     * Loads history for the supplied session and discards any superseded response.
     * @param state The authentication state for this load.
     * @return Completion of the current read.
     */
    async Task LoadAsync(Task<AuthenticationState> state)
    {
        if (disposed) return;
        request?.Cancel();
        request?.Dispose();
        var current = request = new CancellationTokenSource();
        var token = current.Token;
        loading = true;
        signedIn = false;
        trips = [];
        failure = null;
        try
        {
            var user = (await state).User;
            if (token.IsCancellationRequested) return;
            signedIn = user.Identity?.IsAuthenticated == true;
            if (!signedIn) return;

            var loaded = await Trips.ListAsync(token);
            if (!token.IsCancellationRequested) trips = loaded;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ApiError error)
        {
            if (token.IsCancellationRequested) return;
            if (error.Status == 401) signedIn = false;
            else failure = error;
        }
        finally
        {
            if (!token.IsCancellationRequested) loading = false;
        }
    }

    /** Cancels the current request and detaches the session listener. */
    public void Dispose()
    {
        disposed = true;
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        request?.Cancel();
        request?.Dispose();
    }
}
