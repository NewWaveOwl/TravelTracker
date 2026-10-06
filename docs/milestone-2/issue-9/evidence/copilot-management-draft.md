I’ll reconcile the interview answers with the current spec and repository guidance, then return only the revised issue title and draft.

**Title:** Implement standalone attraction management and name search API

**Issue draft**

## Summary

Build a standalone Attractions API using C# and ASP.NET Core on .NET 10, running on port `5104` with seeded in-memory data. This feature covers attraction creation, listing, details, partial updates, and name search. It is scoped to the Attractions service; it does not implement the rest of TravelTracker’s planned services.

## Human-confirmed requirements

- Each attraction has a server-generated, immutable GUID; required name; optional description; optional manually entered decimal rating from `0` to `5` or `null`; address; latitude and longitude; and estimated visit duration in minutes.
- Provide `GET /health`, `GET /api/v1/attractions` (including name search), `GET /api/v1/attractions/{id}`, `POST /api/v1/attractions`, and `PATCH /api/v1/attractions/{id}`.
- Creation returns `201 Created`, the saved representation, and a `Location` pointing to the new detail URL. Updates return `200 OK` with the saved representation.
- Empty/whitespace names and ratings outside `0–5` are invalid (`400`); unknown IDs return `404`.
- Search trims the query and performs a case-insensitive literal substring match on names. Missing or blank search returns all records; no matches returns `200 []`. Order by name, then GUID.
- Changes remain available across requests until service restart. Do not add a delete endpoint.
- Verify every endpoint, including invalid input and unknown-ID cases.

## Contract and implementation details

Successful attraction responses use camelCase fields: `attractionId`, `name`, `description`, `rating`, `address`, `lat`, `lon`, and `estimatedVisitMinutes`. Optional values are `null` when absent. Use deterministic seeded records with fixed GUIDs; explicitly map internal latitude/longitude properties to `lat`/`lon`.

The following details complete the contract and are planning decisions, not additional quoted interview answers:

- Create requires `name`, `lat`, `lon`, and `estimatedVisitMinutes`; omitted address becomes an empty string, and omitted description/rating become `null`. Trim names before storing.
- PATCH accepts a partial JSON object: omitted fields stay unchanged; explicit `null` clears only `description` or `rating`. The ID is read-only. Reject empty PATCH objects, unknown fields, and attempts to set the ID.
- Validate the complete updated record before saving. Invalid requests must not partially change stored data. Coordinates must be within latitude `-90..90` and longitude `-180..180`; duration must be a nonnegative integer.
- Malformed GUIDs, malformed JSON, wrong field types, and invalid values return `400` with `application/problem+json` field errors. Unknown valid GUIDs return `404`.
- Search uses trimmed, literal, ordinal case-insensitive name matching; empty input returns the full catalogue. Repeated `name` parameters return `400`. There is no arbitrary 100-character query limit.
- The process-lifetime store must be safe for concurrent requests and return snapshots rather than expose mutable internal collections.

## Acceptance criteria

- Health works independently of other services and returns `{"status":"ok","service":"Attractions"}`.
- List, detail, search, create, and update return the specified fields, statuses, and deterministic ordering. Creation’s `Location` resolves to the created attraction.
- Search handles mixed case, surrounding spaces, empty/no-match queries, and literal wildcard/regex-looking characters. Updated names are reflected in search.
- Create and PATCH validate required fields, ratings, coordinates, duration, IDs, and request shape; failures leave the store unchanged.
- Valid PATCH preserves the ID and omitted fields; explicit `null` clears description/rating.
- Changes are visible to later requests and disappear on restart.

Use focused .NET 10 unit tests and HTTP integration tests, isolating stores per test host and checking response bodies, headers, validation, and absence of side effects. Keep the API independently runnable on port `5104`, with one use case or primary responsibility per file and the repository’s `/** @param @return */` documentation convention.

## Scope and repository note

The shared API currently has no implemented endpoints, and the repository has no test projects. Keep this service’s model and store independent rather than expanding the shared scaffold. Existing shared-solution build blockers are unrelated; report them separately rather than repairing them as part of this feature. Out of scope: DELETE, database persistence, authentication/privacy redesign, UI, external ratings, and changes to other services.
