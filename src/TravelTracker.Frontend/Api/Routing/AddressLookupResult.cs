namespace TravelTracker.Frontend.Api.Routing;

/** A normalized address response from the shared routing backend. */
public sealed record AddressLookupResult(string Status, IReadOnlyList<AddressCandidate> Candidates, string Attribution);
