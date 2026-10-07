using TravelTracker.Transport.Api.Contracts;
namespace TravelTracker.Transport.Api.Validation;

/**
 * Validates a complete schedule before references are resolved or anything is stored.
 */
public static class TransportRouteValidator
{
    /**
     * Checks required fields, chronological stops and preparation windows.
     * @param request The proposed scheduled route.
     */
    public static void Validate(CreateTransportRouteRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["A nonblank name is required."];
        CheckId(request.TransportModeId, "transportModeId", errors);
        CheckId(request.StartPointId, "startPointId", errors);
        CheckId(request.DestinationPointId, "destinationPointId", errors);
        if (request.DepartureAt is null) errors["departureAt"] = ["Departure is required."];
        if (request.ArrivalAt is null) errors["arrivalAt"] = ["Arrival is required."];
        if (request.BoardingMinutes is null or < 0) errors["boardingMinutes"] = ["Nonnegative preparation minutes are required."];
        if (request.DepartureAt is { } departure && request.ArrivalAt is { } arrival && arrival <= departure)
            errors["arrivalAt"] = ["Arrival must be after departure."];
        if (request.DepartureAt is { } origin && request.BoardingMinutes is >= 0)
        {
            try { _ = origin.ToUniversalTime().AddMinutes(-request.BoardingMinutes.Value); }
            catch (ArgumentOutOfRangeException) { errors["boardingMinutes"] = ["Preparation begins outside the timestamp range."]; }
        }
        if (request.Stops is null) errors["stops"] = ["Stops must be an array."];
        var previousDeparture = request.DepartureAt;
        for (var index = 0; index < (request.Stops?.Count ?? 0); index++)
        {
            var stop = request.Stops![index];
            var prefix = $"stops[{index}]";
            if (stop is null) { errors[prefix] = ["A stop object is required."]; continue; }
            CheckId(stop.PointId, prefix + ".pointId", errors);
            if (stop.ArrivalAt is null) errors[prefix + ".arrivalAt"] = ["Stop arrival is required."];
            if (stop.DepartureAt is null) errors[prefix + ".departureAt"] = ["Stop departure is required."];
            if (stop.BoardingMinutes is null or < 0) errors[prefix + ".boardingMinutes"] = ["Nonnegative preparation minutes are required."];
            if (stop.CanLeaveVehicle is null) errors[prefix + ".canLeaveVehicle"] = ["State whether leaving the vehicle is allowed."];
            if (stop.ArrivalAt is { } stopArrival && previousDeparture is { } previous && stopArrival <= previous)
                errors[prefix + ".arrivalAt"] = ["Stop arrival must follow the previous departure."];
            if (stop.DepartureAt is { } stopDeparture)
            {
                if (stop.ArrivalAt is { } reached && stopDeparture < reached)
                    errors[prefix + ".departureAt"] = ["Stop departure cannot precede its arrival."];
                if (request.ArrivalAt is { } end && stopDeparture >= end)
                    errors[prefix + ".departureAt"] = ["Stop departure must precede final arrival."];
                if (stop.ArrivalAt is { } at && stop.BoardingMinutes is >= 0 &&
                    stop.BoardingMinutes.Value > (stopDeparture - at).TotalMinutes)
                    errors[prefix + ".boardingMinutes"] = ["Preparation must fit the stop downtime."];
            }
            previousDeparture = stop.DepartureAt;
        }
        if (errors.Count > 0) throw new InputValidationException(errors);
    }

    /**
     * Checks that a supplied reference has a usable identifier.
     * @param id Supplied identifier.
     * @param field Public field name.
     * @param errors Collected field errors.
     */
    private static void CheckId(Guid? id, string field, Dictionary<string, string[]> errors)
    {
        if (id is null || id == Guid.Empty) errors[field] = ["A nonempty GUID is required."];
    }
}
