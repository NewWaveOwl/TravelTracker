using TravelTracker.Transport.Api.Contracts;
namespace TravelTracker.Transport.Api.Validation;
/**
 * Validates transport names and the documented mode vocabulary.
 */
public static class TransportModeValidator
{
    private static readonly HashSet<string> Codes = ["car", "bus", "train", "pedestrian", "bike", "plane"];
    /**
     * Checks the complete request before persistence.
     * @param request Writable entry fields.
     * @return The normalized supported mode code.
     */
    public static string Validate(CreateTransportModeRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var code = request.Code?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Codes.Contains(code)) errors["code"] = ["Use car, bus, train, pedestrian, bike or plane."];
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["A nonblank name is required."];
        if (errors.Count > 0) throw new InputValidationException(errors);
        return code;
    }
}
