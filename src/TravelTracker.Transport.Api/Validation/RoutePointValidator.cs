using TravelTracker.Transport.Api.Contracts;
namespace TravelTracker.Transport.Api.Validation;
/**
 * Validates reusable point types and location fields.
 */
public static class RoutePointValidator
{
    private static readonly HashSet<string> Types = ["accommodation", "attraction", "transport_stop"];
    /**
     * Checks the complete request before persistence.
     * @param request Writable location fields.
     * @return Its normalized point type.
     */
    public static string Validate(CreateRoutePointRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var type = request.Type?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Types.Contains(type)) errors["type"] = ["Use accommodation, attraction or transport_stop."];
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["A nonblank name is required."];
        if (request.Address is null) errors["address"] = ["Address must be text."];
        if (request.Lat is null or < -90 or > 90) errors["lat"] = ["A latitude between -90 and 90 is required."];
        if (request.Lon is null or < -180 or > 180) errors["lon"] = ["A longitude between -180 and 180 is required."];
        if (request.SourceId == Guid.Empty) errors["sourceId"] = ["An optional source identifier cannot be empty."];
        if (errors.Count > 0) throw new InputValidationException(errors);
        return type;
    }
}
