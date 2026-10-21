# Milestone 3 architecture and contracts

Contract version: 0.1 (draft for approval). Issue #27 (M3-01). Checked against master `16ffb8a` and the open issues on 2026-10-19.

Docs describe intent and code describes reality. Where they disagree, the disagreement is listed in section 2 and is not resolved silently.

## 1. Services and ownership

Six logical services, each owning its own data, plus one independent frontend. Each member owns two primary services.

| Service | Primary owner | Responsibility | Current state on master |
| --- | --- | --- | --- |
| Users | Christel | Sign-in, profiles, roles, account status, admin audit | Domain model and internal repository only; no endpoints |
| Accommodations | Christel | Lodging places | Domain model and internal repository only; no endpoints |
| Transport | Artiom | Transport entries, typed route points, later scheduled routes | Implemented: POST and GET by ID for transport entries and route points |
| Attractions | Artiom | Attraction places | Implemented: GET list with name filter, GET by ID, POST, PATCH |
| Trips | James | Itineraries, history, map data, time rules | Empty `Trip` and `TripLeg` declarations; interfaces only |
| Settings | James | User preferences | Empty `UserSettings` declaration; interface only |
| Frontend | Open decision | Traveller-facing client using public HTTP DTOs only | Not started |

Current runnable state: one shared host (`TravelTracker.Api`, port 5104) hosts Attractions, Transport and Route Points with in-memory seeded data. The six-service split is the target, not the current runtime.

Issue #15 (attraction management and name search, open) has residual scope only: its list and PATCH behaviour is on master through #53 and #54. #56 (repeated `name` query parameters, open) remains.

## 2. Reconciled inventory

| Topic | Source A | Source B | Resolution in this draft |
| --- | --- | --- | --- |
| Attraction list and PATCH | `docs/api.md` route table and `AttractionsController` list PATCH | `docs/api.md` and README prose say collection GET/search and PATCH are future work; `copilot-instructions.md` says search/PATCH need a separate request | Code and route table agree these exist. Prose corrected in `api.md`, README and `copilot-instructions.md`. Recorded as implemented. |
| Test count | README says 20 test cases; testing skill caps the API suite at 30 | `dotnet test` on master discovers and passes 41 cases | Cap replaced in the testing and validation skills by a one-concern-per-test rule. README updated to 41 at master `16ffb8a`. |
| Route points for trips | `docs/trip-points-and-routes.md` says route points are the trip location unit | `docs/data.md` Trips row uses `DeparturePlaceId` and `DestinationPlaceId` foreign keys to `Accommodations` and `Attractions` | Conflict. See Open decision 4. |
| Trip legs | `ITripRepository.ReplaceLegsAsync` and `docs/data.md` note | `Trips` table stores one leg on the row | Resolved by James: a `TripLegs` table with ordered rows. A trip stores one departure and one destination. Each leg stores only a `destination`; its departure is implied (the trip's departure for the first leg, the previous leg's destination for the rest). The last leg must end at the trip's destination. A broken connection is an impossible invariant: with implied departures no stored state can describe a gap, so reordering reshapes the chain and only time clashes are highlighted (`design.md` updated to match). Schema change is future work. |
| Trip user identity | `docs/api.md` `GET /trips?userId=` | Design section 3 privacy and ownership rules | Conflict. Resolved by James: `GET /trips` lists the caller's own trips; the owner comes from the bearer subject, not a query parameter. |
| Trip and settings wire IDs | `docs/api.md` says the Trips/Settings wire format still needs agreement | `docs/domain.md` uses `Guid` public IDs with internal `Id64` keys | Open decision 5. Repositories currently take public `Guid`. |
| SQL schema | `src/Database/schema.sql` is PostgreSQL-style DDL (`text`, `uuid`, enums) | No EF Core provider is chosen | Not evidence of a provider decision. Open decision 3. |
| Frontend stack | Issue #27 offers React Native or an ASP.NET Core client | Design section 7 describes screens only | Open decision 1. |

## 3. Project boundaries

Current (implemented):

- `TravelTracker.Domain`: models and identifier types. No EF Core, no web references. Verified in `TravelTracker.Domain.csproj`.
- `TravelTracker.Data`: repositories, seed data and `EntityIdGenerator`. References Domain only. `InternalsVisibleTo` exposes `Id64` to Data.
- `TravelTracker.Api`: controllers, request/response contracts, services and composition in `Program.cs`. References Data and Domain. Services coordinate workflows; controllers map to public DTOs.
- `TravelTracker.Api.Tests`: HTTP and identity tests.

Proposed for the six-service target (approval needed):

- One Domain, Data and Api project set per service, for example `TravelTracker.Transport.Api`, with a shared Domain only for types that cross service boundaries. Dependency direction stays Api to Data to Domain inside each service.
- Route Points and transport entries belong to Transport. Attractions keeps its own store.
- Each service runs as its own host with its own store. Services never read another service's tables; services talk to each other only through agreed, documented, authenticated contracts (internal-only endpoints are allowed).
- Client code uses HTTP DTOs only and never references a Domain or Data project.

Layer rules every reviewer checks:

1. Domain has no EF Core or ASP.NET Core dependency.
2. Data references only its own Domain and its persistence dependency.
3. Api coordinates workflows and owns HTTP concerns (routing, binding, status codes, auth policies).
4. The client uses public HTTP DTOs exclusively.
5. No service reads another service's store.

## 4. Identifiers and ID generation

- Wire format: public UUIDv7 as a `Guid`, JSON camelCase. Internal `Id64` never leaves Data.
- Storage: app-issued sequential `Id64` keys (`bigint` in `schema.sql`), with no database identity or sequence.
- Current strategy (`EntityIdGenerator`): one in-process counter shared across entity types. On the first call it reads the highest stored key, then increments in memory. No counter is persisted; it is rebuilt from data on restart. Gaps after a crash are harmless.
- Limit: this assumes one backend instance. Two instances would issue duplicate keys. Before scaling out, switch to a node-aware scheme (snowflake or similar).
- Migration: when a SQL provider is chosen, seed the counter from the table maximum at startup, as the in-memory generator already does. The provider choice is open decision 3.

## 5. HTTP contract conventions

- Base path `/api/v1`, JSON camelCase, timestamps ISO 8601 UTC.
- Creation returns `201 Created` with a `Location` header pointing at the GET-by-ID route.
- Validation failures return `400` with problem details. Field errors name the request field, per the design rule that every problem sits next to the field that caused it.
- Malformed IDs return `400`. Unknown but well-formed IDs return `404`.
- Unknown JSON properties are rejected with `400` on writes that enforce it (currently attraction create and PATCH).
- `401 Unauthorized`: no valid bearer token. Not implemented yet.
- `403 Forbidden`: an authenticated user asks for something they can see but not change (for example a banned user's write). Not implemented yet.
- `404 Not Found`: also used for another user's private item, so it looks absent. Design section 3 rule.
- `412 Precondition Failed`: a stale edit when two saves race (decided by James). The server sends an ETag with a record, the client returns it in `If-Match` on save, and a stale tag is refused with `412`. Applies to trips and settings once they exist; the design requires the later save to be refused. Not implemented yet.
- `503 Service Unavailable`: a required dependency (for example Users during a protected action) is down. The design says protected actions wait rather than trust stale permissions. Not implemented yet.

## 6. Identity, ownership and scopes (proposed)

Blocked on open decision 2 (OIDC provider). The rules below hold for any provider.

- Authentication: the Api validates the bearer JWT signature, issuer, audience and expiry. Unit tests may fake identities, but a separate test set must prove real token validation (see the testing skill).
- Subject mapping (proposed): the Api must work with any OIDC provider, so it cannot assume `sub` is our UUID. It maps the token's issuer and `sub` pair to the Users record, which carries the public UUIDv7. The Users service remains the authority for account standing. The Api checks it live (no cached answer) before any action that a restricted account could not perform; reads that every standing allows need no check. If Users is unavailable during a required check, the action fails with `503`. James proposes replacing the `UserStatus` enum (active, deactivated, banned) with a nullable `SpecialStatus`: null for normal users, `Deactivated` or `Banned` for restricted ones. This changes the Users domain model, so it needs Christel's approval (open decision 8).
- Ownership: a trip's owner is the authenticated subject. `GET /trips?userId=` is replaced by `GET /trips`, which returns the caller's own trips, so a caller cannot list another user's trips by changing a parameter (decided by James). Settings work the same way: `GET/PUT /settings` has no user ID in the path, and the internal `userId` is taken from the authenticated caller (decided by James). Administrators do not get private trip access.
- Banned users: read own data, write nothing (`403` on writes). Deactivated users: `401` or `403`, nothing allowed (to be decided with the OIDC provider).

Endpoint-to-scope policy (proposed names, none implemented):

| Endpoint group | Read scope | Write scope | Notes |
| --- | --- | --- | --- |
| Attractions and route points (shared catalogue) | `catalogue.read` | `catalogue.write` | Attractions are public reads for active users. Write rules follow the catalogue ownership decision. |
| Transport entries | `catalogue.read` | `catalogue.write` | Preloaded entries are read-only for travellers. |
| Users | `users.read` (self) | `users.write` (self); `users.admin` for status | Admin status changes need a written reason and an audit entry. |
| Accommodations | `catalogue.read` | `catalogue.write` | Private places belong to the creator. |
| Trips | `trips.read` | `trips.write` | Owner only. |
| Settings | `settings.read` | `settings.write` | Self only. |
| `GET /health` | none | none | Anonymous. |

## 7. Trip contract (minimum slice)

The trip contract must settle the conflicts below before production implementation starts. Items marked **Open decision** need the owner's choice.

Current facts:

- A trip is a named, dated, one-way or round-trip journey shown in a chosen time zone (design section 4).
- One-way trips go from accommodation to attraction; round trips return to the start and include at least one attraction (design section 5).
- Legs are ordered, connected, time-ordered, and never overlap. Visits and stays are separate concepts.
- Trip location snapshots are kept so catalogue edits never move an old trip's markers.

Open decision 4: direct itinerary legs versus scheduled-route selection for the minimum trip slice.

- Option A, direct legs: a leg records a transport entry, a from and to route point, and the traveller's entered departure and arrival times. No scheduled routes, stops or boarding calculations. Smallest slice; keeps Trips independent of Transport scheduling.
- Option B, scheduled-route selection: a leg selects a scheduled transport route owned by Transport, with stops and preparation minutes. Requires Transport scheduling (currently out of scope) and a cross-service lookup.

Either option needs: the ID type used by trip legs (public route point GUID or a Trips-local reference), snapshot fields copied at save time, and the migration from the single-leg `Trips` row to a `TripLegs` table. Stays and visits are out of this slice in both options.

Ticket import, nearby suggestions and free-time summary (M3 features in design sections 5 and 8) stay deferred unless separately approved.

## 8. Failure handling

- Saving a trip is one atomic unit: check the user, confirm every place and transport entry (through owning service contracts), apply trip rules, store snapshots, commit. On any failure nothing is stored.
- Saved trips stay readable when a catalogue is unavailable, because snapshots are stored with the trip.
- Dependency calls use timeouts and return `503` with a clear message. The form keeps the user's input in the client; the server stores nothing partial.
- Admin status changes and trip deletes leave audit or cleanup records; cleanup progress is visible to administrators (design section 3).

## 9. Open decisions

Each needs an owner choice recorded here before the dependent issue starts.

1. **Frontend stack.** React Native or an ASP.NET Core client. Blocks #32 and #33.
2. **OIDC provider.** Which identity provider issues the tokens, and its issuer and audience values. Blocks #33, #34 and #37.
3. **EF Core database provider.** Blocks #35, #36 and the persistence parts of #41. `schema.sql` is PostgreSQL-style DDL; that is not a provider decision. Requirement (James): the code must not couple to one provider; it should work on effectively any provider EF Core supports. Provider-specific features (native enums, native uuid types) are avoided unless a later decision accepts them.
4. **Trip and scheduled-route boundary.** Option A or B above, and the trip leg ID type. Blocks #39, #40 and #41.
5. **Trips and Settings wire IDs.** Public `Guid` only, or a different identifier for these services. Blocks #40 and #42.
6. **Catalogue ownership.** Who can write shared attractions and accommodations, given the design's admin rules.
7. **Scope names and deactivated-user response.** Confirm the proposed names in section 6 and whether deactivated users get `401` or `403`.
8. **Account standing model.** Replace `UserStatus` with a nullable `SpecialStatus` (null for normal users, `Deactivated` or `Banned` otherwise), as proposed by James in section 6. Needs Christel's approval because she owns Users.

## 10. Approval record

| Member | Role | Approved version | Date | Notes |
| --- | --- | --- | --- | --- |
| James (@5unekku) | Trips and Settings owner | Pending | | |
| Artiom | Transport and Attractions owner | Pending | | |
| Christel | Users and Accommodations owner | Pending | | |

Scope kept out of this milestone contract: ticket import, nearby suggestions, free-time summary, time blocks, timeline editing beyond the editor requirements in design section 7, and any trip rule that needs scheduled transport routes.
