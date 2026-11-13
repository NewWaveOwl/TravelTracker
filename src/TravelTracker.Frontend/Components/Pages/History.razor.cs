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
    [Inject] TimeProvider Clock { get; set; } = default!;

    IReadOnlyList<TripSummaryDto> trips = [];
    CancellationTokenSource? request;
    ApiError? failure;
    bool loading = true, signedIn, disposed;
    const int PageSize = 10;
    string search = "", status = "all", order = "past";
    DateTime? fromDate, throughDate;
    int pageIndex;
    bool InvalidDates => fromDate > throughDate;

    static string TripName(TripSummaryDto trip) => string.IsNullOrWhiteSpace(trip.Name) ? "Untitled trip" : trip.Name;

    /**
     * Derives the planned trip status from saved boundaries, without implying live location tracking.
     * @param trip A summary returned by the owner-scoped Trips API.
     * @return Completed, current or upcoming status key.
     */
    string Status(TripSummaryDto trip) => trip.End < Clock.GetUtcNow() ? "completed"
        : trip.Start > Clock.GetUtcNow() ? "upcoming" : "current";

    string StatusLabel(TripSummaryDto trip) => Status(trip) switch
    {
        "completed" => "Completed", "upcoming" => "Upcoming", _ => "In progress"
    };

    /**
     * Applies local filters to the owner's loaded summaries; no unsupported query contract is sent.
     * @return Matching trips in a deterministic order, before paging.
     */
    IReadOnlyList<TripSummaryDto> FilteredTrips()
    {
        var filtered = trips.Where(trip => TripName(trip).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
            && (fromDate is null || trip.End.Date >= fromDate.Value.Date)
            && (throughDate is null || trip.Start.Date <= throughDate.Value.Date)
            && (status == "all" || Status(trip) == status));
        var sorted = order switch
        {
            "name" => filtered.OrderBy(TripName, StringComparer.OrdinalIgnoreCase),
            "oldest" => filtered.OrderBy(trip => trip.Start),
            "newest" => filtered.OrderByDescending(trip => trip.Start),
            _ => filtered.OrderBy(trip => Status(trip) == "completed" ? 0 : 1)
                .ThenBy(trip => Status(trip) == "completed" ? -trip.Start.UtcTicks : trip.Start.UtcTicks)
        };
        return sorted.ThenBy(trip => trip.TripId).ToArray();
    }

    void ResetPage() => pageIndex = 0;

    void ClearFilters()
    {
        (search, status, order, fromDate, throughDate, pageIndex) = ("", "all", "past", null, null, 0);
    }

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
        ClearFilters();
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
        ResetPage();
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
