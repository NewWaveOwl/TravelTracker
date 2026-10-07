**Title:** Add case-insensitive attraction name search

**Body:**

## Summary
Add read-only name search to the M1 public attraction catalogue. This draft proposes matching attraction names by case-insensitive substring.

## Proposed API
`GET /api/v1/attractions?name={query}`

- Trim leading and trailing whitespace. If `name` is absent or blank, return the full list.
- Match substrings case-insensitively using ordinal comparison for the seeded in-memory catalogue. Treat wildcard characters literally.
- Return results ordered by name (ordinal, case-insensitive), then attraction GUID. No matches returns `200 []`.
- Reject a trimmed query longer than 100 UTF-16 characters with `400 ProblemDetails`.
- Preserve the M1 JSON response shape and GUID IDs. Keep the existing-by-GUID detail route’s `404` behavior for an unknown ID. Do not mutate stored data.

## Acceptance criteria
- Search finds seeded attractions such as Montreal Museum, Science Centre, and Old Port regardless of query casing.
- Missing/blank query returns the full list; no match returns an empty array.
- Results have deterministic ordering, and wildcard characters are treated literally.
- A query over the length limit returns `400 ProblemDetails`; a valid query returns `200`.
- HTTP-level tests cover these cases, including the existing detail route’s unknown-ID response.

## Scope and dependencies
The M1 assignment calls for an independently runnable `Attractions.Api` on port 5104, with its own types and seeded store—not shared models, repositories, or context with separately owned services. The current repository instead has one `TravelTracker.Api`/`Data`/`Domain` layout and no routes, DI, or persistence implementation. **Track the minimal independent Attractions API prerequisite—seeded list, details, and health (`{ "status": "ok", "service": "Attractions" }`)—separately from this search feature.** Keep that prerequisite limited to Attractions; do not restructure all six services. This architecture choice needs confirmation before implementation.

The catalogue in this M1 scope is public seeded data only; this does not implement private/shared catalogue authorization or M2 access rules. The current baseline also has a compile-blocking `Id64` issue and no test project; triage direct blockers without expanding this feature into unrelated repairs. Proposed owner: Artiom, subject to confirmation.

## Pending confirmation
Priming questions about feature choice, preferred IDE, and baseline/dependency assumptions remain unanswered. This draft records recommendations, not user decisions.