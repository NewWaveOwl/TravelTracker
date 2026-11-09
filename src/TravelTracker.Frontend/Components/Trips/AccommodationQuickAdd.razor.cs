using Microsoft.AspNetCore.Components;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Accommodations;
using TravelTracker.Frontend.Features.Accommodations;

namespace TravelTracker.Frontend.Components.Trips;

/** Adds a private accommodation without leaving the itinerary and reports the server-confirmed record. */
public partial class AccommodationQuickAdd
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
    IReadOnlyDictionary<string, string[]> errors = new Dictionary<string, string[]>();
    string? failure;
    bool saving;

    async Task SaveAsync()
    {
        if (saving) return;
        failure = null;
        errors = draft.Validate();
        if (errors.Count != 0) return;
        saving = true;
        try
        {
            await Saved.InvokeAsync(await Accommodations.CreateAsync(draft.ToRequest()));
        }
        catch (ApiError error)
        {
            errors = error.Errors;
            failure = error.Message;
            Logger.LogWarning("Adding an accommodation failed as {Kind} (status {Status})", error.Kind, error.Status);
        }
        finally { saving = false; }
    }
}
