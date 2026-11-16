using TravelTracker.Frontend.Api;

namespace TravelTracker.Frontend.Components.Shared;

/** Maps failure kinds to the heading shown above an error summary. */
public static class ErrorHeadings
{
    /**
     * Picks the heading for a failed request.
     * @param failure The failure to describe, or null when nothing failed.
     * @return The user-facing heading.
     */
    public static string For(ApiError? failure) => failure?.Kind switch
    {
        FailureKind.Validation => "Check your details",
        FailureKind.Permission => "Permission denied",
        FailureKind.Network => "Unable to connect",
        FailureKind.Unavailable => "Service unavailable",
        _ => "Unable to complete the request",
    };
}
