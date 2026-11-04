namespace TravelTracker.Frontend.Api;

/**
 * A categorized failure from a public service call.
 *
 * @param kind The failure category the UI reacts to.
 * @param message A user-safe description of what went wrong.
 * @param status The HTTP status code, when a response was received.
 */
public sealed class ApiError(FailureKind kind, string message, int? status = null) : Exception(message)
{
    public FailureKind Kind { get; } = kind;

    public int? Status { get; } = status;
}
