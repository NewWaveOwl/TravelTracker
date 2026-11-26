namespace TravelTracker.Frontend.Features.Trips;

/**
 * what the browser reports about map drawing. optional fields stay null when the browser does not expose them, so the automatic choice treats them as unknown.
 * @param WebGl Whether a WebGL context can be created on this device.
 * @param DeviceMemoryGigabytes Approximate device memory in gigabytes, or null when unreported.
 * @param LogicalCores Logical processor count, or null when unreported.
 * @param CoarsePointer True when the primary pointer is touch or another coarse input, as on phones and tablets.
 */
public sealed record MapCapabilities(bool WebGl, double? DeviceMemoryGigabytes, int? LogicalCores, bool CoarsePointer);
