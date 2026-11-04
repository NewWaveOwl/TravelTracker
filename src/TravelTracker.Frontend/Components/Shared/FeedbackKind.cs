namespace TravelTracker.Frontend.Components.Shared;

/** The states a Feedback panel can announce; error kinds mirror FailureKind by name. */
public enum FeedbackKind
{
    Loading,
    Empty,
    Validation,
    Permission,
    Network,
    Unavailable,
    Unexpected,
    Success,
}
