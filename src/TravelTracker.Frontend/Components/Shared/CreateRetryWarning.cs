using TravelTracker.Frontend.Api;

namespace TravelTracker.Frontend.Components.Shared;

/** Warns that a failed create may already have saved, so a retry could make a duplicate. */
public static class CreateRetryWarning
{
    /** Shown beside a retry offer after a create ended without a confirmed response. */
    public const string Message = "The previous attempt may have saved this. Check your saved list before retrying so you do not create a duplicate.";

    /**
     * Tells whether a failed create may have reached the service before the connection or service failed.
     * @param failure The failed create request.
     * @return True for network and unavailable failures, and for unexpected failures with no status, since a 2xx whose body or entity tag cannot be used may still have been saved.
     */
    public static bool Applies(ApiError failure) =>
        failure.Kind is FailureKind.Network or FailureKind.Unavailable || failure is { Kind: FailureKind.Unexpected, Status: null };
}
