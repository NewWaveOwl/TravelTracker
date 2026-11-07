# Milestone 3 architecture and contracts

Contract version: 0.1 (draft for approval). Issue #27 (M3-01). Checked against master `16ffb8a` and the open issues on 2026-10-19.

Docs describe intent and code describes reality. Where they disagree, the disagreement is listed in section 2 and is not resolved silently.

## 1. Services and ownership

Six logical services, each owning its own data, plus one independent frontend. Each member owns two primary services.

| Service | Primary owner | Responsibility | Current state in this branch |
| --- | --- | --- | --- |
| Users | Christel | Sign-in, profiles, roles, account status, admin audit | Independent Domain/Data/Api projects with profile, live access, first-administrator and audited role-management endpoints |
| Accommodations | Christel | Lodging places | Independent Domain/Data/Api projects with PostgreSQL, create/search/detail, coordinate validation and private/shared visibility (#38) |
| Transport | Artiom | Transport entries, typed route points, later scheduled routes | Implemented: independent PostgreSQL host, POST, detail and filtered collection selection for transport entries and route points |
| Attractions | Artiom | Attraction places | Implemented: GET list with name filter, GET by ID, POST, PATCH |
| Trips | James | Itineraries, history, map data, time rules | Implemented: independent PostgreSQL host on 5108 with atomic create and own-trip detail (#41); history, edit, delete and map data remain future work |
| Settings | James | User preferences | Independent Domain/Data/Api projects with PostgreSQL, own-settings GET/PUT, `settings.read`/`settings.write` policies and live Users standing checks (#42); see [Settings](settings-service.md) |
| Frontend | Blazor Web App, interactive Server mode (owner decision, 2026-11-03, replacing the earlier same-day React choice) | Traveller-facing client using public HTTP DTOs only | Independent `src/TravelTracker.Frontend` shell, shared form/status displays and anonymous HTTP health checks; feature integration remains separate |

Current runnable state: `TravelTracker.Accommodations.Api` owns the lodging catalogue on port 5106 with PostgreSQL, scoped search/detail, create, coordinate validation, private/shared ownership and live Users standing checks. `TravelTracker.Attractions.Api` owns Attractions on 5104; `TravelTracker.Transport.Api` owns Transport/Route Points on 5105; and `TravelTracker.Users.Api` owns profiles, first-administrator setup, live standing and audited role changes over HTTPS on port 5101. Each service owns its Domain/Data projects and PostgreSQL store and uses `TravelTracker.Security` bearer infrastructure. The Blazor frontend consumes public HTTP DTOs, provides attraction search/editing and exposes Administrator Panel to active administrators. See [Accommodations](accommodations-service.md), [Users](first-administrator-setup.md), [Transport](transport-service.md) and [Attractions](attractions-service.md). `TravelTracker.Settings.Api` owns the caller's preferences on port 5107. The independent Trips service (`TravelTracker.Trips.Api`, port 5108) saves complete itineraries with snapshots in its own PostgreSQL store and returns own trips; see [Trips](trips-service.md). Trip history/edit/delete and accommodation update/archive remain future work.

Issue #15 (attraction management and name search, open) has residual scope only: its list and PATCH behaviour is on master through #53 and #54. #56 (repeated `name` query parameters, open) remains.

## 2. Reconciled inventory

| Topic | Source A | Source B | Resolution in this draft |
| --- | --- | --- | --- |
| Attraction list and PATCH | `docs/api.md` route table and `AttractionsController` list PATCH | `docs/api.md` and README prose say collection GET/search and PATCH are future work; `copilot-instructions.md` says search/PATCH need a separate request | Code and route table agree these exist. Prose corrected in `api.md`, README and `copilot-instructions.md`. Recorded as implemented. |
| Test count | README says 20 test cases; testing skill caps the API suite at 30 | `dotnet test` on master discovers and passes 41 cases | Cap replaced in the testing and validation skills by a one-concern-per-test rule. README updated to 41 at master `16ffb8a`. |
| Route points for trips | `docs/trip-points-and-routes.md` says route points are the trip location unit | `docs/data.md` Trips row uses `DeparturePlaceId` and `DestinationPlaceId` foreign keys to `Accommodations` and `Attractions` | Resolved 2026-11-06 (section 9, item 4): direct legs, with each place validated against the service that owns it. |
| Trip legs | `ITripRepository.ReplaceLegsAsync` and `docs/data.md` note | `Trips` table stores one leg on the row | Resolved by James: a `TripLegs` table with ordered rows. A trip stores one departure and one destination. Each leg stores only a `destination`; its departure is implied (the trip's departure for the first leg, the previous leg's destination for the rest). The last leg must end at the trip's destination. A broken connection is an impossible invariant: with implied departures no stored state can describe a gap, so reordering reshapes the chain and only time clashes are highlighted (`design.md` updated to match). Schema change is future work. |
| Trip user identity | `docs/api.md` `GET /trips?userId=` | Design section 3 privacy and ownership rules | Conflict. Resolved by James: `GET /trips` lists the caller's own trips; the owner comes from the bearer subject, not a query parameter. |
| Trip and settings wire IDs | `docs/api.md` says the Trips/Settings wire format still needs agreement | `docs/domain.md` uses `Guid` public IDs with internal `Id64` keys | Resolved for Trips 2026-11-06 (section 9, item 5): public `Guid` only. Settings remains open under #42. Repositories take public `Guid`. |
| SQL schema / database provider | `src/Database/schema.sql` is PostgreSQL-style DDL (`text`, `uuid`, enums) | Owner explicitly selected PostgreSQL on 2026-10-28 | Decision 3 resolved: PostgreSQL with EF Core's Npgsql provider. The old schema still does not prove implemented persistence or applied migrations. |
| Frontend stack | Issue #27 offers React Native or an ASP.NET Core client | Design section 7 describes screens only | Resolved 2026-11-03 (section 9, item 1): Blazor Web App in `src/TravelTracker.Frontend`. |

## 3. Project boundaries

Current (implemented):

- `TravelTracker.Domain`: models and identifier types. No EF Core, no web references. Verified in `TravelTracker.Domain.csproj`.
- `TravelTracker.Data`: repositories, seed data and `EntityIdGenerator`. References Domain only. `InternalsVisibleTo` exposes `Id64` to Data.
- `TravelTracker.Transport.Api/Data/Domain`: independent Transport/Route Points boundary with PostgreSQL persistence and shared bearer security. See [startup and selection](transport-service.md).
- `TravelTracker.Api`: library of remaining unimplemented service interfaces; no runnable catalogue endpoints.
- `TravelTracker.Transport.Api.Tests`: Transport HTTP, bearer, selection and PostgreSQL tests. `TravelTracker.Api.Tests` retains shared Trip/identity tests.

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
- Migration: for the selected PostgreSQL provider, seed the app-issued counter from the table maximum at startup, as the in-memory generator already does. Preserve the documented single-instance limitation; provider selection does not authorize replacing app-issued IDs with database identities or sequences.

## 5. HTTP contract conventions

- Base path `/api/v1`, JSON camelCase, timestamps ISO 8601 UTC.
- Creation returns `201 Created` with a `Location` header pointing at the GET-by-ID route.
- Validation failures return `400` with problem details. Field errors name the request field, per the design rule that every problem sits next to the field that caused it.
- Malformed IDs return `400`. Unknown but well-formed IDs return `404`.
- Unknown JSON properties are rejected with `400` on writes that enforce it (currently attraction create and PATCH).
- `401 Unauthorized`: no valid bearer token. Implemented for the combined catalogue API by #34.
- `403 Forbidden`: a valid token lacks the required permission. Implemented for catalogue scopes by #34; account-standing rules (for example a banned user's write) remain future work.
- `404 Not Found`: also used for another user's private item, so it looks absent. Design section 3 rule.
- `412 Precondition Failed`: a stale edit when two saves race (decided by James). The server sends an ETag with a record, the client returns it in `If-Match` on save, and a stale tag is refused with `412`. Implemented for settings (#42): `GET` and `PUT /api/v1/settings` send a strong ETag per saved revision, `PUT` requires `If-Match` (a missing header returns `428 Precondition Required` per RFC 6585, settled; a stale or malformed one returns `412`; see [settings-service.md](settings-service.md)). Trips are not implemented yet.
- `503 Service Unavailable`: a required dependency (for example Users during a protected action) is down. The design says protected actions wait rather than trust stale permissions. Not implemented yet.

## 6. Identity, ownership and scopes (proposed)

The representative combined API implements configurable JWT bearer validation and catalogue read/write policies for #34; see [authentication configuration, policy map and adoption recipe](authentication.md). The user approved its implementation plan in chat on 2026-10-24 and selected **Auth0** on 2026-11-03. The Blazor client signs in with the ASP.NET Core OpenID Connect handler, authorization-code flow and PKCE. Authority, exact issuer, audience and scope claim name remain deployment settings. Live Blazor sign-in, a protected Transport read and hosted logout across two open tabs were verified on 2026-11-04. The frontend enforces session revocation across circuits, protected calls and cookie restoration after logout or API `401`; `403` preserves the session. Its session registry supports one frontend process and fails closed across restarts. Automated tests cover these session boundaries; remaining live failure verification is separate. Users mapping and other service work remain separate. See [Auth0 setup and verification evidence](frontend-auth0.md).

- Authentication: the Api validates the bearer JWT signature, issuer, audience and expiry. Unit tests may fake identities, but a separate test set must prove real token validation (see the testing skill).
- Subject mapping (proposed): the Api must work with any OIDC provider, so it cannot assume `sub` is our UUID. It maps the token's issuer and `sub` pair to the Users record, which carries the public UUIDv7. The Users service remains the authority for account standing. The Api checks it live (no cached answer) before any action that a restricted account could not perform; reads that every standing allows need no check. If Users is unavailable during a required check, the action fails with `503`. James proposes replacing the `UserStatus` enum (active, deactivated, banned) with a nullable `SpecialStatus`: null for normal users, `Deactivated` or `Banned` for restricted ones. This changes the Users domain model, so it needs Christel's approval (open decision 8).
- Ownership: a trip's owner is the authenticated subject. `GET /trips?userId=` is replaced by `GET /trips`, which returns the caller's own trips, so a caller cannot list another user's trips by changing a parameter (decided by James). Settings work the same way: `GET/PUT /settings` has no user ID in the path, and the internal `userId` is taken from the authenticated caller (decided by James). Administrators do not get private trip access.
- Banned users: read own data, write nothing (`403` on writes). Deactivated users: `401` or `403`, nothing allowed (to be decided with the OIDC provider).

Endpoint-to-scope policy (catalogue policies implemented in the combined API; other service policies proposed):

| Endpoint group | Read scope | Write scope | Notes |
| --- | --- | --- | --- |
| Attractions and route points (shared catalogue) | `catalogue.read` | `catalogue.write` | Attractions are public reads for active users. Write rules follow the catalogue ownership decision. |
| Transport entries | `catalogue.read` | `catalogue.write` | Preloaded entries are read-only for travellers. |
| Users | `users.read` (self) | `users.write` (self); `users.admin` for status | Admin status changes need a written reason and an audit entry. |
| Accommodations | `catalogue.read` | `catalogue.write` | Private places belong to the creator. |
| Trips | `trips.read` | `trips.write` | Owner only. Implemented by the Trips service (#41): `POST /api/v1/trips` and `GET /api/v1/trips/{id}`. |
| Settings | `settings.read` | `settings.write` | Self only. |
| `GET /health` | none | none | Anonymous. |

## 7. Trip contract (minimum slice)

The trip contract must settle the conflicts below before production implementation starts. Items marked **Open decision** need the owner's choice.

Current facts:

- A trip is a named, dated, one-way or round-trip journey shown in a chosen time zone (design section 4).
- One-way trips go from accommodation to attraction; round trips return to the start and include at least one attraction (design section 5).
- Legs are ordered, connected, time-ordered, and never overlap. Visits and stays are separate concepts.
- Trip location snapshots are kept so catalogue edits never move an old trip's markers.

Decision 4 (resolved 2026-11-06 as Option A; see section 9): direct itinerary legs versus scheduled-route selection for the minimum trip slice.

- Option A, direct legs: a leg records a transport entry, a from and to route point, and the traveller's entered departure and arrival times. No scheduled routes, stops or boarding calculations. Smallest slice; keeps Trips independent of Transport scheduling.
- Option B, scheduled-route selection: a leg selects a scheduled transport route owned by Transport, with stops and preparation minutes. Requires Transport scheduling (currently out of scope) and a cross-service lookup.

Either option needs: the ID type used by trip legs (public route point GUID or a Trips-local reference), snapshot fields copied at save time, and the migration from the single-leg `Trips` row to a `TripLegs` table. Stays and visits are out of this slice in both options.

Ticket import, nearby suggestions and free-time summary (M3 features in design sections 5 and 8) stay deferred unless separately approved.

## 8. Failure handling

- Saving a trip is one atomic unit: check the user, confirm every place and transport entry (through owning service contracts), apply trip rules, store snapshots, commit. On any failure nothing is stored.
- Saved trips stay readable when a catalogue is unavailable, because snapshots are stored with the trip.
- Dependency calls use timeouts and return `503` with a clear message. The form keeps the user's input in the client; the server stores nothing partial.
- Admin status changes and trip deletes leave audit or cleanup records; cleanup progress is visible to administrators (design section 3).

## 9. Decisions and remaining open items

Unresolved items need an owner choice recorded here before the dependent work starts. Resolved entries remain numbered for existing references.

1. **Frontend stack resolved.** Blazor Web App in `src/TravelTracker.Frontend` (owner instruction, 2026-11-03; replaced the earlier same-day React choice). It calls the services over HTTP from the server, so there is no browser CORS or proxy. Identity integration remains #33.
2. **OIDC provider selected: Auth0 (owner decision, 2026-11-03).** The Blazor client uses the ASP.NET Core OpenID Connect handler. Tenant/application configuration is supplied locally. The owner verified hosted sign-in, a protected Transport read requiring the configured issuer/audience and `catalogue.read`, and logout on the earlier React client. Live hosted sign-in, a protected Transport read and logout across two open tabs were also verified on Blazor on 2026-11-04. The session invalidation follow-up addresses cross-tab logout and rejected-token reuse; remaining live failure checks are documented separately. Follow [Auth0 setup and verification limits](frontend-auth0.md). Production deployment remains separately configured. Users mapping in #37 remains separate.
3. **EF Core database provider — resolved 2026-10-28.** The repository owner explicitly selected **PostgreSQL**, using `Npgsql.EntityFrameworkCore.PostgreSQL` for EF Core. Database selection no longer blocks #35, #36 or the persistence parts of #41; their other prerequisites remain. Keep Domain/application rules independent of the provider and isolate PostgreSQL configuration/mappings in persistence. Preserve the earlier intent of minimizing provider coupling in Domain/application code; PostgreSQL is the concrete implementation and verification target, and support for other providers must not be claimed without implementation and tests. Review provider-specific model choices and migrations deliberately rather than treating the historical SQL schema as an applied migration. Relational persistence verification must run against PostgreSQL. Server version, deployment and connection secrets are separate configuration concerns, not decided here.
4. **Trip and scheduled-route boundary: resolved 2026-11-06 (owner decision, Option A).** Trip legs use direct journeys: each leg records a from and to place snapshot, a transport mode, and entered departure and arrival times. No scheduled routes or stops are selected. Places are validated against their owning services: accommodations against Accommodations, attractions against Attractions, and transport stops against Transport route points. Only transport stops are read from Transport route points. Trip legs reference places by public `Guid`, not a Trips-local key. Implemented for M3-14 in `TravelTracker.Api` (`Trips` and `References` folders) and `TravelTracker.Domain` (`TripPlace`, `TripTransport`, `TripLeg`). #41 owns persistence and endpoints.
5. **Trips and Settings wire IDs: resolved 2026-11-06 (owner decision).** Trips use public `Guid` identifiers only. Each leg stores its transport mode by public `Guid` (`TransportModeId`) and validates it with `GET /api/v1/transport-modes/{id}`, replacing the earlier transport-code list check. The transport mode's `code` and `name` are copied into the snapshot at save time. Settings still needs its own agreement under #42.
6. **Catalogue ownership — Attractions resolved 2026-10-28; Accommodations implemented in #38.** Both catalogues use creator-private/admin-shared visibility. Omitted create visibility is private; creating a shared item requires live active administrator status. Other people's private records appear absent, even to administrators. Active users read shared items and their own private items; banned users read only their own private items and cannot write; deactivated/missing users are denied. M3-12 implements accommodation create/search/detail only; update/archive remains out of scope. Both services check Users live and fail closed on dependency failure. See the [Users consumer integration boundary](attractions-service.md) and [Accommodations service contract](accommodations-service.md).
7. **Scope names and deactivated-user response.** Confirm the proposed names in section 6 and whether deactivated users get `401` or `403`.
8. **Account standing model.** Replace `UserStatus` with a nullable `SpecialStatus` (null for normal users, `Deactivated` or `Banned` otherwise), as proposed by James in section 6. Needs Christel's approval because she owns Users.
9. **Trip service-to-service token split: resolved 2026-11-06 (owner decision).** When saving a trip, the caller's access token is forwarded unchanged to the Accommodations, Attractions and Transport catalogue hosts, which share the `traveltracker-api` audience. Users is never sent the caller's token. Account standing is checked live with a backend credential holding `users.access`: `POST /internal/users/identity` resolves the validated issuer and subject, then `GET /internal/users/{userId}/access` reads standing. Banned and deactivated accounts cannot save trips. Missing accounts are rejected. Users failures and timeouts fail closed as dependency unavailable, and nothing is written.

## 10. Approval record

Database-specific owner decision: PostgreSQL, confirmed in chat on 2026-10-28. This records that decision only; it does not imply all-member approval of the entire draft contract or resolve its other open items.

| Member | Role | Approved version | Date | Notes |
| --- | --- | --- | --- | --- |
| James (@5unekku) | Trips and Settings owner | Pending | | |
| Artiom | Transport and Attractions owner | Pending | | |
| Christel | Users and Accommodations owner | Pending | | |

Scope kept out of this milestone contract: ticket import, nearby suggestions, free-time summary, time blocks, timeline editing beyond the editor requirements in design section 7, and any trip rule that needs scheduled transport routes.
