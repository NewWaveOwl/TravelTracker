using Microsoft.AspNetCore.Components;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Accommodations;
using TravelTracker.Frontend.Components.Shared;
using TravelTracker.Frontend.Features.Accommodations;

namespace TravelTracker.Frontend.Components.Trips;

/** Adds a private accommodation without leaving the itinerary and reports the server-confirmed record. */
public partial class AccommodationQuickAdd : IDisposable
{
    [Inject] AccommodationsClient Accommodations { get; set; } = default!;
    [Inject] ILogger<AccommodationQuickAdd> Logger { get; set; } = default!;

    /** Prefix for the field ids. */
    [Parameter, EditorRequired] public string Id { get; set; } = "";

    /** Raised once with the record the service confirmed. */
    [Parameter] public EventCallback<AccommodationDto> Saved { get; set; }

    /** Raised when the traveller backs out. */
    [Parameter] public EventCallback Cancelled { get; set; }

    readonly AccommodationDraft draft = new();
    readonly CancellationTokenSource closed = new();
    IReadOnlyDictionary<string, string[]> errors = new Dictionary<string, string[]>();
    string? failure;
    bool saving;
    bool pickingLocation;

    async Task SaveAsync()
    {
        if (saving || pickingLocation) return;
        failure = null;
        errors = draft.Validate();
        if (errors.Count != 0) return;
        saving = true;
        try
        {
            var created = await Accommodations.CreateAsync(draft.ToRequest(), closed.Token);
            // the itinerary closed while the save was in flight, so there is no parent left to receive the record
            if (closed.IsCancellationRequested)
            {
                Logger.LogWarning("An accommodation save was confirmed after the itinerary closed, so the record was not added to the trip");
                return;
            }
            await Saved.InvokeAsync(created);
        }
        catch (ApiError error)
        {
            errors = error.Errors;
            failure = CreateRetryWarning.Applies(error) ? $"{error.Message} {CreateRetryWarning.Message}" : error.Message;
            Logger.LogWarning("Adding an accommodation failed as {Kind} (status {Status})", error.Kind, error.Status);
        }
        catch (OperationCanceledException) when (closed.IsCancellationRequested)
        {
            Logger.LogWarning("An accommodation save was canceled while the itinerary closed, so its outcome is unknown");
        }
        finally
        {
            saving = false;
        }
    }

    /** Cancels a save that is still running when the itinerary closes. */
    public void Dispose()
    {
        closed.Cancel();
        closed.Dispose();
    }
}
