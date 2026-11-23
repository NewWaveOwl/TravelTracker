namespace TravelTracker.Frontend.Api;

/**
 * A categorized failure from a public service call.
 *
 * @param kind The failure category the UI reacts to.
 * @param message A user-safe description of what went wrong.
 * @param status The HTTP status code, when a response was received.
 * @param errors Field-level messages keyed by public field name, when the service supplied any.
 * @param code An optional allowlisted machine code; never arbitrary backend diagnostics.
 */
public sealed class ApiError(FailureKind kind, string message, int? status = null, IReadOnlyDictionary<string, string[]>? errors = null, string? code = null) : Exception(message)
{
    /** the failure category the UI reacts to. */
    public FailureKind Kind { get; } = kind;

    /** the HTTP status code, or null when no response was received. */
    public int? Status { get; } = status;

    /** An allowlisted service code for more specific recovery guidance. */
    public string? Code { get; } = code;

    /** field-level messages keyed by public field name; empty when there are none. */
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}
