namespace TravelTracker.Frontend.Api;

/** Categories of failure a public service call can end in. */
public enum FailureKind
{
    Validation,
    Permission,
    Network,
    Unavailable,
    Unexpected,
}
