namespace TravelTracker.Frontend.Api.Transport;

/** Transport mode codes the Transport and Settings services accept. */
public static class TransportModes
{
    /** transport codes the service accepts, in display order. */
    public static readonly IReadOnlyList<string> Codes = ["car", "bus", "train", "pedestrian", "bike", "plane"];
}
