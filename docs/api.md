# API Layer

HTTP contracts for the independent service APIs and the historical shared scaffold.

## Current state

Attractions and Accommodations each have dedicated Api/Data/Domain projects and independent PostgreSQL databases; they run on ports 5104 and 5106. Transport/Route Points run independently on 5105, and Users runs on HTTPS 5101. Catalogue APIs use shared bearer infrastructure, explicit scopes and live Users checks for account standing; creator-private/shared visibility is enforced by the owning service. See the [Attractions](attractions-service.md) and [Accommodations](accommodations-service.md) startup guides.

## Existing service interfaces

| Preserved interface | Wraps | Declared methods |
|---|---|---|
| [IAttractionService](../src/TravelTracker.Attractions.Api/Services/IAttractionService.cs) | `Attraction` | `GetAllAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` |
| [ITransportModeService](../src/TravelTracker.Transport.Api/Services/ITransportModeService.cs) | `TransportMode` | `GetAllAsync`, `GetByIdAsync`, `GetByCodeAsync`, `CreateAsync`, `UpdateAsync` |
| [ITripService](../src/TravelTracker.Api/Services/ITripService.cs) | `Trip`, `TripLeg` (empty type declarations) | `GetHistoryAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `ReplaceLegsAsync` |
| [ITripMapService](../src/TravelTracker.Api/Services/ITripMapService.cs) | `TripMapPoint` (empty type declaration) | `GetMapPointsAsync` |

The Attraction, Accommodation, TransportMode and Users profile interfaces have concrete implementations in their owning service APIs. Settings is implemented by the independent Settings service ([settings-service.md](settings-service.md)); the shared settings placeholder and its legacy interfaces were removed. Trips/map retain their existing contracts but have no runtime implementation or endpoint. The historical shared entity identity is an `EntityId<T>` with an internal `Id64` key and public uuidv7; the independent Accommodation service uses a public UUIDv7 as its database primary key. APIs expose only public GUIDs and DTOs, never persistence keys or owner identity.

The historical `Accomodation` model and `IAccomadationRepository` remain unchanged for compatibility; the Accommodations API owns a separate `Accommodation` model, repository and HTTP contract.

## Shared scaffold's planned REST contract

`/api/v1`, JSON camelCase, ISO 8601 UTC. Current User, Accomodation, Attraction, TransportMode and RoutePoint identifiers are GUIDs; the wire format is always the public uuidv7. The future Trips/Settings wire format still needs agreement.

| Service | Routes |
|---|---|
| Users | `POST /auth/login`, `GET/POST/PATCH/DELETE /users/{id}`, `PATCH /users/{id}/status` |
| Accommodations | `GET /accommodations`, `GET/POST/PATCH /accommodations/{id}` |
| Transport | `GET /transport-modes`, `GET /transport-modes/{id}` |
| Attractions | `GET/POST /attractions`, `GET/PATCH /attractions/{id}` |
| Trips | `GET /trips` (the caller's own trips), `POST /trips`, `GET/PATCH/DELETE /trips/{id}`, `PUT /trips/{id}/legs`, `GET /trips/{id}/map-points` |
| Settings | `GET/PUT /settings` (the caller's own settings; `settings.read` / `settings.write`; `ETag` and `If-Match` required on PUT; see [settings-service.md](settings-service.md)) |
| Ticket import (future) | `POST /ticket-imports`, `GET /ticket-imports/{id}`, `POST /ticket-imports/{id}/confirm` |
| All | `GET /health` |

Response codes: `201` create, `404` not found, `400` invalid trip direction/times, `403` forbidden (e.g. banned user write), `503` dependency unavailable.

The planned Trips service will validate referenced user/place/transport IDs via HTTP calls to the owning service at write time; each service will own its own store. This integration is not implemented by the current catalogue demo.

## Ownership

Users/Accommodations: Christel. Transport/Attractions: Artiom. Trips/Settings: James.


## Implemented APIs

Catalogue resource operations require a validated bearer access token and the exact scope: GET uses `catalogue.read`; POST and PATCH use `catalogue.write`. `/health` is explicitly anonymous. Authentication failures return `401` with a Bearer challenge; missing permissions return `403` before input validation. Read and write permissions are independent. Both fallback authorization (no attribute) and bare `[Authorize]` default to `catalogue.read`; mutations must still declare their write policy. Attractions and Accommodations also check account standing and ownership through the live Users HTTP boundary. See [authentication configuration](authentication.md) and the [Accommodations contract](accommodations-service.md).

Attractions runs at http://localhost:5104; Accommodations runs at http://localhost:5106; Transport/Route Points run at http://localhost:5105. Each host has a separate Development Swagger page and public anonymous `/health`. Users runs on HTTPS 5101. Attractions and Accommodations require explicit catalogue scopes and live Users standing checks. For Accommodations, omitted `visibility` defaults to private; shared creation requires a live active administrator. See the [Attractions](attractions-service.md) and [Accommodations](accommodations-service.md) service contracts.

| Method and route | Service | Result |
| --- | --- | --- |
| GET /api/v1/attractions | Attractions | 200 ordered array of attractions; optional `name` query filters case-insensitive substring matches |
| GET /api/v1/attractions/{id} | Attractions | 200 attraction details; malformed GUID -> 400; unknown valid GUID -> 404 |
| POST /api/v1/attractions | Attractions | 201, saved attraction and details Location; unknown JSON properties rejected (400) |
| PATCH /api/v1/attractions/{id} | Attractions | 200 updated attraction; malformed GUID/invalid JSON/invalid fields -> 400; unknown valid GUID -> 404 |
| GET /api/v1/transport-modes | Transport | Saved selection, optional code/name filters |
| GET /api/v1/transport-modes/{id} | Transport | Saved transport entry, or 404 |
| POST /api/v1/transport-modes | Transport | 201, saved entry and details Location |
| GET /api/v1/route-points | Transport | Saved selection, optional type/name filters |
| GET /api/v1/route-points/{id} | Transport | Saved route point, or 404 |
| POST /api/v1/route-points | Transport | 201, saved point and details Location |
| GET /api/v1/accommodations | Accommodations | 200 ordered visible records; optional literal case-insensitive `name` substring filter |
| GET /api/v1/accommodations/{id} | Accommodations | 200 visible record; malformed GUID -> 400; missing or another user's private record -> 404 |
| POST /api/v1/accommodations | Accommodations | 201, saved record and details Location; invalid or unknown input fields -> 400 |

Public IDs are GUIDs; JSON fields use camelCase. Standard ASP.NET Core model binding and DataAnnotations produce 400 validation problem details for invalid input or malformed IDs. Existing Attraction and Transport request behavior for unknown JSON fields is unchanged; the Accommodations create DTO rejects unknown fields. No request DTO exposes server-owned IDs. GET never creates a missing record.

### Attraction fields

Response: attractionId, name, description, rating, address, lat, lon, estimatedVisitMinutes. POST requires a nonblank name, latitude (-90 to 90), longitude (-180 to 180), and nonnegative integer estimatedVisitMinutes. Names are trimmed. Description and rating are optional; null rating means no score, otherwise 0-5 inclusive. Omitted address becomes an empty string; explicit null address is invalid. PATCH accepts partial JSON objects; omitted fields are preserved, explicit null clears description or rating only. Unknown fields, duplicate case-insensitive property names, or attempts to set attractionId are rejected with 400.

Collection GET trims a single `name` query value and matches a literal, ordinal case-insensitive substring of the name. Missing, empty or whitespace-only input returns the full catalogue; no match returns `200 []`. Results are ordered by name using ordinal case-insensitive comparison, then public GUID. Query keys and values are decoded once by ASP.NET Core. Repeated `name` parameters return `400 application/problem+json` with an `errors.name` entry before search runs, including equal/blank values and equivalent encoded or case-varied keys (for example `name=museum&%6Eame=port`). Rejected queries leave the catalogue unchanged.

### Transport entry fields

Response: transportModeId, code, name, description. POST requires a nonblank name and one exact lowercase code: car, bus, train, pedestrian, bike or plane. Description is optional. Names are trimmed; multiple entries may share a code. ITransportModeService manages entries without journey dates.

The Api controllers use the original asynchronous ITransportModeService and IAttractionService with their Domain entities; their Data implementations use the original repository interfaces. The new [IRoutePointService](../src/TravelTracker.Transport.Api/Services/IRoutePointService.cs) follows the same layer boundary. Response DTOs map DisplayName to name and Latitude/Longitude to lat/lon. Existing interface methods for collection/update/delete remain available internally, and Transport exposes collection selection in addition to detail/create.

### Route point fields

Response: routePointId, type, name, address, lat, lon, sourceId. POST requires an exact lowercase type (accommodation, attraction or transport_stop), nonblank name and valid coordinates. Address defaults to empty and cannot be explicitly null. Optional sourceId is unverified GUID metadata; the API does not contact another service. A point is a reusable location, not a scheduled journey.

Attractions owns its durable PostgreSQL store, scoped context/repository and application rules. Optional examples seed an empty store only; restarting retains records. Transport/Route Points use their own scoped PostgreSQL context and repositories. Each resource keeps cohesive controller/service/repository responsibilities. PostgreSQL row-locking transactions protect attraction PATCH validation and atomic updates.

Attraction list/name search and PATCH plus Accommodation create/search/detail are implemented (see the route table). Transport and route-point PATCH/DELETE, accommodation update/archive, scheduled transport routes, stops and timing calculations remain future work. The contract for the six-service target is in [milestone 3 contracts](milestone-3-contracts.md). [Current specification](https://github.com/NewWaveOwl/TravelTracker/blob/milestone_2_AI_and_development_resource/devlopmet_flow/atiom_developmet_flow/implementation/specification.md), [Attractions HTTP examples](../src/TravelTracker.Attractions.Api/requests.http), [Accommodations HTTP examples](../src/TravelTracker.Accommodations.Api/requests.http).

### Accommodation fields and access

The response is `accommodationId`, `name`, `address`, `lat`, `lon`, and `visibility` (`private` or `shared`). `POST /api/v1/accommodations` accepts only `name`, `address`, `lat`, `lon`, and optional `visibility`. The server generates the UUIDv7 and assigns the owner from the validated bearer `(iss, sub)`; request identity, owner, role, status, or ID fields are rejected. Names are trimmed and must not be blank. Latitude must be between -90 and 90; longitude between -180 and 180. Omitted address becomes an empty string; explicit null is invalid. Omitted visibility is private; explicit null or unsupported values are invalid. Invalid fields and unknown JSON fields return `400` validation problem details and persist nothing.

Collection search trims a single `name` query value and matches a literal, ordinal case-insensitive substring; missing or blank input returns all records visible to that caller. Results are ordered by name case-insensitively, then by public GUID. Repeated `name` query parameters return a field validation error. Another user's private record is excluded from collection results and appears as `404` on detail, even to administrators. Active callers can read shared records plus their own private records; banned callers can read only their own private records and cannot create; deactivated or missing accounts are denied with `403`. Creating a shared record requires `catalogue.write` and a live active Users administrator result; an ordinary traveller can create private records. Users lookup outages fail closed with `503`.

Every resource route declares `catalogue.read` or `catalogue.write` explicitly. Missing/invalid bearer credentials return `401`; a valid token without the required scope returns `403` before request validation. `GET /health` is anonymous. The service has no PATCH, archive, or delete endpoint in M3-12. Configure its PostgreSQL store and Users service credential using [Accommodations startup instructions](accommodations-service.md).

For future Trips integration, a trip point is an ordered occurrence of a reusable route point. A scheduled route connects route points using a transport entry; a trip leg selects that journey. Dates and boarding/stop timings belong to the scheduled route and occurrences, rather than the transport catalogue POST. See [trip points, transport and routes](trip-points-and-routes.md).

Attractions failure handling: missing validated identity returns 401; forbidden operations 403; application validation and PostgreSQL integrity constraints 400; unique, serialization, deadlock and optimistic concurrency conflicts 409; transient storage outages 503. Unexpected storage/programming failures remain 500. Cancellation without client disconnection returns 408; disconnected requests receive no problem body. All failure details are safe and omit provider/identity/credential data. See [Attractions operations](attractions-service.md).

### Frontend attraction editing (#43)

The Blazor `/attractions` view consumes these public HTTP DTOs and sends the current user's bearer token to the configured Attractions service only. Search uses one percent-encoded literal `name` parameter. Create sends only writable fields and uses the service's private visibility default. Edit sends only changed fields, with explicit null clearing rating or description. Saved details come from a confirmed response; result-refresh failures are reported separately. Unconfirmed writes are never automatically retried.

The frontend's optional duration ± field is a local preview and has no HTTP or persistence field. The map is the user-supplied static placeholder; choosing a location currently means confirming manually entered coordinates. No endpoint or database change is included in #43.

## Users contracts

The independent Users service runs on HTTPS port 5101 locally and owns its PostgreSQL database. The following profile routes identify the caller from the validated token issuer and subject.

| Route | Permission | Behavior |
| --- | --- | --- |
| `GET /api/v1/users/me` | `users.read` | Returns the caller's profile. The first valid request creates an active traveller with blank editable fields. Banned accounts can read their profile. Deactivated accounts receive 403. |
| `PATCH /api/v1/users/me` | `users.write` | Active accounts can change `username`, `email` and `address`. Omitted fields keep their values. A null address clears it. Invalid or unknown fields return 400. A duplicate username or stale write returns 409. |
| `GET /internal/users/{userId}/access` | `users.access` and an allowlisted service subject | Returns `userId`, `status` and `role`, or 404 for an unknown ID. |
| `POST /internal/users/identity` | `users.access` and an allowlisted service subject | Resolves a backend-validated issuer and subject to an existing public ID, or returns 404. |

The self-profile response includes `publicId`, `username`, `email`, `address`, `role`, `status`, `createdAt` and `updatedAt`. Editable email is profile contact information. Internal lookups never create accounts. Protected routes return 401 for invalid or missing bearer credentials and 403 for missing permission. Users returns 503 when required storage is unavailable.

`POST /api/v1/users/bootstrap-first-administrator` requires `catalogue.read`, the setup credential, an allowed verified provider email bound to the validated subject, and a never-consumed bootstrap record. It accepts no client-selected identity or role fields. A replay for the same identity may return the current role without re-elevating the account.

`POST /api/v1/users/access` uses a separate high-entropy service Bearer credential over HTTPS, rather than the caller's access token, to return live status/role for the legacy Attractions consumer. Unknown identities return deactivated/traveller. The endpoint never creates an account. `/health` is anonymous liveness only. See [first-admin setup](first-administrator-setup.md). The self-profile GET/PATCH routes above are implemented; general cross-account profile CRUD, account status changes, and account deletion are not. Administrator listing and role changes are documented below.

### Administrator account listing and role changes

`GET /api/v1/users` and `PATCH /api/v1/users/{publicId}/role` require HTTPS, a validated read-session bearer token and a live active-administrator account in Users. A token role claim alone never grants access. Listing returns public profile DTOs only. Role PATCH accepts only `role`, `expectedRole`, and `reason`; unknown fields are rejected. Valid roles are `traveller` and `administrator`; the trimmed reason must contain1–500 characters. The service returns200 only for a committed change,400 for invalid input/inactive promotion,403 for a nonactive/nonadministrator actor,404 for a missing target,409 for stale/no-op roles or last-active-admin demotion, and safe503 for unavailable storage. Rejected changes do not write an audit entry or mutate the role.

Every role mutation shares the one-time-bootstrap transaction lock, so two concurrent demotions cannot remove the last active administrator. Successful changes atomically persist the actor, target, previous role, new role, reason and time. Self-demotion immediately removes server authority for further administration. First verified profile access can provision only a traveller; it preserves existing roles and standing. See [setup and administrator operations](first-administrator-setup.md).

Transport now runs independently with PostgreSQL on 5105. See [selection filters, startup, policies and persistence](transport-service.md).
