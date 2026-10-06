# Attraction name search specification

Planning deliverable for [issue #9](https://github.com/NewWaveOwl/TravelTracker/issues/9). Add a read-only name filter to the public Milestone 1 Attractions catalogue so a traveller can find an attraction without scanning the whole list. Copilot's original draft was reviewed and revised; see [draft review](copilot-review.md).

## API behavior

`GET /api/v1/attractions?name={query}` returns a JSON array of attraction representations.

- Trim leading and trailing whitespace. An absent, empty or whitespace-only `name` returns the full catalogue.
- Match a literal substring of `Name` using `StringComparison.OrdinalIgnoreCase` for the seeded in-memory catalogue. Do not search address, interpret wildcard/regex syntax, or concatenate SQL.
- Validate after trimming: more than 100 UTF-16 code units returns HTTP `400` with `application/problem+json` and a validation error for `name`. Exactly 100 is valid. This is a proposed feature limit, not a course requirement.
- Return HTTP `200` and `[]` when no attraction matches. Sort every successful result by name using `StringComparer.OrdinalIgnoreCase`, then by its GUID identifier as a deterministic tie breaker.
- Use the framework's normal query decoding. Do not URL-decode a second time. Accept at most one `name` value; repeated `name` parameters return `400` with a `name` validation error.
- Read only the public M1 seed catalogue. Search neither changes stored records nor contacts another service or external data provider.

The response uses the M1 wire fields `attractionId` (GUID string), `name`, `address`, `lat`, `lon`, and `estimatedVisitMinutes`. Existing shared model properties `Latitude`/`Longitude` are implementation details; map them to the agreed `lat`/`lon` response. This feature does not migrate other services to `Id64`.

## Acceptance criteria

| Case | Expected result |
| --- | --- |
| Fixed seed contains Montreal Museum, Science Centre and Old Port; `name=mUsEuM` | `200`, only Montreal Museum |
| `name=%20port%20` | `200`, Old Port; surrounding whitespace ignored |
| Missing `name`, `name=` or whitespace only | `200`, full catalogue in the same deterministic order |
| Valid substring matches several names | Every matching name, ordered by name then GUID |
| Names differ only in case or are equal | GUID tie breaker gives repeatable order |
| No match, including ordinary literal `%`, `_`, `*` or regex-looking input without a matching name | `200 []`; no wildcard or regex expansion |
| Trimmed length 100 / 101 UTF-16 code units | Valid search / `400` problem response with `name` error |
| `name=port&name=museum` | `400` problem response with `name` error |
| Encoded query | Decoded once by framework; matching still literal |
| Search followed by unfiltered list | Seed data remains unchanged |
| Successful response | Exact agreed field names and GUID IDs; no internal or private fields |
| Baseline `GET /api/v1/attractions/{id}` | Known GUID `200`, unknown GUID `404`, malformed GUID `400` problem response |
| Baseline `GET /health` on port 5104 | `200 {"status":"ok","service":"Attractions"}` while other TravelTracker services are stopped |

Add unit tests for search validation and repository filtering/ordering, plus HTTP tests for binding, status, problem responses and JSON compatibility. Test data and IDs must be fixed. Unit tests alone do not prove the HTTP contract.

## Runnable prerequisite

The checkout currently has no attraction endpoint, service implementation, repository implementation or health route. Before search, establish a minimal independently runnable `TravelTracker.Attractions.Api` on port 5104 with its own public seed store, list, GUID detail and health routes. Keep this prerequisite in a separate implementation commit and prove it before the search commit. It is not completion of all M1 CRUD requirements.

This follows the supplied M1 Attractions assignment and the six-service ownership in `docs/design.md`. It is a documented, limited change from the current shared API/Data/Domain scaffold and its generic skill default. Do not restructure the other five services, delete shared contracts, or make the new service depend on their model/data assemblies. If the team already supplies a compliant Attractions baseline before implementation, reuse it and omit the scaffold step.

The existing solution fails in `Id64.cs` and contains interfaces referencing absent trip/settings types. The independent service may be validated in isolation, but the full-solution failure must remain visible and must be resolved or explicitly deferred with justification during #11 review; isolated success must never be reported as a successful solution build.

## Scope and handoff

No UI, authentication redesign, private catalogue, write routes, pagination, geospatial/fuzzy search, EF migration, event bus, transport feature or unrelated baseline repair is included. Private/shared catalogue access must be designed before private records are introduced; do not expose them through this seed-only endpoint.

Attractions ownership remains with Artiom. Implementation proceeds through [#10](https://github.com/NewWaveOwl/TravelTracker/issues/10); validation, evidence and the implementation PR through [#11](https://github.com/NewWaveOwl/TravelTracker/issues/11). The [implementation plan](implementation-plan.md) and [triage](triage-notes.md) define the handoff. The [priming interview](priming-interview.md) records proposed decisions and unanswered human preferences explicitly.
