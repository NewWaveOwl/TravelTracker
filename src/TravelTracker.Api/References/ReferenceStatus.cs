namespace TravelTracker.Api.References;

/**
 * Outcome category of one call to another service's public HTTP contract.
 */
public enum ReferenceStatus {
    /** The record exists and the caller may use it. */
    Found,
    /** The record is absent, or private to someone else; the two look identical on purpose. */
    NotFound,
    /** The upstream service rejected the credential (401). */
    Unauthorized,
    /** The upstream service refused the request for this caller (403). */
    Forbidden,
    /** The upstream service could not answer in time; the caller must fail closed. */
    Unavailable
}
