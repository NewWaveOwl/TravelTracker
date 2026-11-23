using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Frontend.Api.Routing;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Features.Trips;

namespace TravelTracker.Frontend.Components.Pages;

/** Connects optional shared routing previews to the editable map without writing catalogue records. */
public partial class NewTrip
{
    RoutingClient? SharedRouting => Services.GetService<RoutingClient>();
    IReadOnlyList<TripPlaceDto> DraftLocationStops => PickedCoordinates is { Lat: >= -90 and <= 90, Lon: >= -180 and <= 180 } position
        ? [new(Guid.Empty, markerType ?? "attraction", string.IsNullOrWhiteSpace(placeName) ? "Selected location" : placeName,
            placeAddress, position.Lat, position.Lon)] : [];

    void PreviewManualPosition((decimal Lat, decimal Lon) position)
    {
        if (busy) return;
        (latitude, longitude, placeAddress) = (position.Lat, position.Lon, "");
        reverseAddressVersion = ++addressLookupSequence;
    }
    readonly Dictionary<(TripPlannerDraft.Stop From, TripPlannerDraft.Stop To), ConfirmedGeometry> confirmedGeometry = [];
    RouteSelection? previewSelection;
    TripPlannerDraft? previewDraft;
    long routingRevision, previewRevision;
    int previewRouteVersion;

    sealed record ConfirmedGeometry(RoutePlanRequest Request, RouteItinerary Itinerary, Guid TransportId);

    RoutePlanRequest? CurrentRouteRequest => CurrentLeg is { Mode: { } mode } leg && selected > 0 &&
        draft.DepartureFor(leg) is { } departure
        ? new(new(draft.Stops[selected - 1].Place.Lat, draft.Stops[selected - 1].Place.Lon),
            new(draft.Stops[selected].Place.Lat, draft.Stops[selected].Place.Lon), mode.Code, departure, RouteLocalTime.RouterZone(draft.TimeZone)) : null;

    RouteItinerary? MapRoutePreview
    {
        get
        {
            var active = previewSelection is { } selectedRoute && PreviewStillCurrent(selectedRoute) ? selectedRoute : null;
            var routes = confirmedGeometry.Where(pair => GeometryMatches(draft, pair.Key.From, pair.Key.To, pair.Value) &&
                !(active is not null && ReferenceEquals(pair.Key.From, draft.Stops[active.OriginIndex]) &&
                    ReferenceEquals(pair.Key.To, draft.Stops[active.DestinationIndex])))
                .Select(pair => pair.Value.Itinerary).ToList();
            if (active is not null)
                routes.Add(active.Itinerary);
            if (routes.Count == 0) return null;
            return new("session-routes", routes.Sum(route => route.DurationSeconds), routes.Sum(route => route.DistanceMeters),
                "estimated", routes.SelectMany(route => route.Legs).ToArray());
        }
    }

    void SelectRoutePreview(RouteSelection? selection)
    {
        previewSelection = selection;
        previewDraft = draft;
        previewRevision = routingRevision;
        previewRouteVersion = routeVersion;
    }

    bool PreviewStillCurrent(RouteSelection selection) =>
        ReferenceEquals(previewDraft, draft) && previewRevision == routingRevision && previewRouteVersion == routeVersion &&
        (uint)selection.OriginIndex < (uint)draft.Stops.Count && (uint)selection.DestinationIndex < (uint)draft.Stops.Count &&
        (draft.Stops[selection.OriginIndex].Place.Lat, draft.Stops[selection.OriginIndex].Place.Lon) ==
            (selection.Request.Origin.Lat, selection.Request.Origin.Lon) &&
        (draft.Stops[selection.DestinationIndex].Place.Lat, draft.Stops[selection.DestinationIndex].Place.Lon) ==
            (selection.Request.Destination.Lat, selection.Request.Destination.Lon) &&
        RouteLocalTime.RouterZone(draft.TimeZone) == selection.Request.TimeZone;

    /**
     * Applies one explicitly confirmed provider route after rechecking saved transport and unchanged endpoints.
     * @param selection A current adjacent route preview with a public saved transport identifier.
     * @return True only when mode and exact times were applied together; catalogue and trip records are not saved here.
     */
    async Task<bool> ConfirmRouteAsync(RouteSelection selection)
    {
        if (busy || savingCatalogue || closed.IsCancellationRequested || HasUnconfirmedMarkerChanges ||
            previewSelection != selection || !PreviewStillCurrent(selection) || selection.DestinationIndex != selection.OriginIndex + 1 ||
            selection.Itinerary.Legs.Count == 0 || selection.Transport.TransportModeId == Guid.Empty ||
            !TimeZoneInfo.TryFindSystemTimeZoneById(draft.TimeZone.Trim(), out var zone))
            return false;
        var requestedDraft = draft;
        var requestedRevision = routingRevision;
        var version = routeVersion;
        busy = true;
        StateHasChanged();
        try
        {
            var mode = await Transport.GetTransportAsync(selection.Transport.TransportModeId, closed.Token);
            if (!ReferenceEquals(requestedDraft, draft) || requestedRevision != routingRevision || version != routeVersion ||
                previewSelection != selection || HasUnconfirmedMarkerChanges || !PreviewStillCurrent(selection) || mode.Code != selection.Request.Mode ||
                mode.TransportModeId != selection.Transport.TransportModeId || string.IsNullOrWhiteSpace(mode.Name)) return false;
            var starts = selection.Itinerary.Legs[0].DepartureTime;
            var ends = selection.Itinerary.Legs[^1].ArrivalTime;
            if (ends <= starts) return false;
            var localDeparture = TimeZoneInfo.ConvertTime(starts, zone);
            var localArrival = TimeZoneInfo.ConvertTime(ends, zone);
            var from = draft.Stops[selection.OriginIndex];
            var to = draft.Stops[selection.DestinationIndex];
            var leg = draft.LegTo(selection.DestinationIndex);
            RememberEdit();
            leg.Mode = new(mode.TransportModeId, mode.Code, mode.Name);
            leg.Departure = localDeparture.DateTime;
            leg.Arrival = localArrival.DateTime;
            leg.DurationMinutes = null;
            leg.DepartureOffset = new(localDeparture.DateTime, draft.TimeZone.Trim(), localDeparture.Offset);
            leg.ArrivalOffset = new(localArrival.DateTime, draft.TimeZone.Trim(), localArrival.Offset);
            selected = selection.DestinationIndex;
            previewSelection = null;
            CancelConnection();
            MarkEdited();
            confirmedGeometry[(from, to)] = new(selection.Request with { DepartureTime = starts },
                selection.Itinerary, mode.TransportModeId);
            notice = $"Selected route applied: {localDeparture:MMM d HH:mm:ss zzz} → {localArrival:MMM d HH:mm:ss zzz}. Save the trip when ready. Route geometry is kept for this session.";
            return true;
        }
        catch (ApiError error) when (version == routeVersion)
        {
            Logger.LogWarning("Route confirmation failed as {Kind} (status {Status})", error.Kind, error.Status);
            errors = ["The saved transport mode could not be confirmed. Your itinerary and entered times are unchanged."];
            actionFailed = true;
            return false;
        }
        catch (ApiError) when (version != routeVersion) { return false; }
        catch (OperationCanceledException) when (closed.IsCancellationRequested) { return false; }
        finally
        {
            if (version == routeVersion) { busy = false; StateHasChanged(); }
        }
    }

    static bool GeometryMatches(TripPlannerDraft candidate, TripPlannerDraft.Stop from, TripPlannerDraft.Stop to, ConfirmedGeometry geometry)
    {
        var index = candidate.Stops.ToList().IndexOf(from);
        if (index < 0 || index + 1 >= candidate.Stops.Count || !ReferenceEquals(candidate.Stops[index + 1], to)) return false;
        var leg = candidate.LegTo(index + 1);
        return leg.Mode?.TransportModeId == geometry.TransportId && leg.Mode.Code == geometry.Request.Mode &&
            candidate.DepartureFor(leg) == geometry.Request.DepartureTime &&
            candidate.DurationFor(leg) == geometry.Itinerary.Legs[^1].ArrivalTime - geometry.Itinerary.Legs[0].DepartureTime &&
            (from.Place.Lat, from.Place.Lon) == (geometry.Request.Origin.Lat, geometry.Request.Origin.Lon) &&
            (to.Place.Lat, to.Place.Lon) == (geometry.Request.Destination.Lat, geometry.Request.Destination.Lon) &&
            RouteLocalTime.RouterZone(candidate.TimeZone) == geometry.Request.TimeZone;
    }

    Action CaptureRoutingUndo()
    {
        var snapshot = confirmedGeometry.ToArray();
        return () =>
        {
            confirmedGeometry.Clear();
            foreach (var pair in snapshot) confirmedGeometry.Add(pair.Key, pair.Value);
            previewSelection = null;
            routingRevision++;
        };
    }

    void ClearRoutingGeometry()
    {
        previewSelection = null;
        confirmedGeometry.Clear();
        routingRevision++;
    }

    /** Rebinds still-matching transient geometry after the server returns fresh saved stop objects. */
    TripPlannerDraft RebindRoutingGeometry(TripPlannerDraft replacement)
    {
        var previous = confirmedGeometry.Where(pair => GeometryMatches(draft, pair.Key.From, pair.Key.To, pair.Value))
            .Select(pair => (Index: draft.Stops.ToList().IndexOf(pair.Key.From), pair.Value)).ToArray();
        confirmedGeometry.Clear();
        previewSelection = null;
        foreach (var item in previous)
            if (item.Index >= 0 && item.Index + 1 < replacement.Stops.Count &&
                GeometryMatches(replacement, replacement.Stops[item.Index], replacement.Stops[item.Index + 1], item.Value))
                confirmedGeometry[(replacement.Stops[item.Index], replacement.Stops[item.Index + 1])] = item.Value;
        return replacement;
    }

    /**
     * Previews the selected search candidate without saving or changing the existing marker.
     * @param candidate The address explicitly selected by the traveller.
     */
    void PreviewAddress(AddressCandidate candidate)
    {
        (latitude, longitude, placeAddress) = (candidate.Lat, candidate.Lon, candidate.Label);
        reverseAddressVersion = 0;
    }
}
