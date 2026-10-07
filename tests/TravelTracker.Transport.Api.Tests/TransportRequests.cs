using System.Text.Json.Nodes;
namespace TravelTracker.Transport.Api.Tests;
/**
 * Supplies manually entered schedules and stable seed references.
 */
public static class TransportRequests
{
    public const string Bus = "00000000-0000-0000-0000-000000000101";
    public const string Start = "00000000-0000-0000-0000-000000000201";
    public const string Destination = "00000000-0000-0000-0000-000000000202";
    public const string Stop = "00000000-0000-0000-0000-000000000203";

    /**
     * Builds a valid two-stop schedule with explicit UTC offsets.
     * @return A fresh schedule with two stops and explicit offsets.
     */
    public static JsonObject Route() => new()
    {
        ["name"] = " Scheduled Bus ", ["transportModeId"] = Bus,
        ["startPointId"] = Start, ["destinationPointId"] = Destination,
        ["departureAt"] = "2026-10-10T08:00:00-04:00", ["arrivalAt"] = "2026-10-10T16:00:00-04:00",
        ["boardingMinutes"] = 30,
        ["stops"] = new JsonArray(
            new JsonObject { ["pointId"] = Stop, ["arrivalAt"] = "2026-10-10T09:00:00-04:00", ["departureAt"] = "2026-10-10T10:30:00-04:00", ["boardingMinutes"] = 20, ["canLeaveVehicle"] = true },
            new JsonObject { ["pointId"] = Stop, ["arrivalAt"] = "2026-10-10T12:00:00-04:00", ["departureAt"] = "2026-10-10T13:20:00-04:00", ["boardingMinutes"] = 0, ["canLeaveVehicle"] = false })
    };
}
