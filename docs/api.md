# API Layer

HTTP contracts for the shared scaffold and the implemented independent services.

## Current state

The original `TravelTracker.Api/Program.cs` remains a bare bootstrap. The standalone Attractions and Transport APIs below are implemented and runnable independently.

## Shared scaffold service interfaces

| Interface | Wraps | Key methods |
|---|---|---|
| `IAttractionService` | `Attraction` | get all, get by ID, create, update, delete |
| `ITransportModeService` | `TransportMode` | get all, get by ID, get by code, create, update |
| `ISettingsService` | `UserSettings` (not implemented) | get or create, update |
| `ITripService` | `Trip`, `TripLeg` (not implemented) | history, get by ID, create, update, delete, replace legs |
| `ITripMapService` | `TripMapPoint` (read-model) | get map points |

No `IUserService` or `IAccommodationService` yet; their repositories are `internal` (see [[data]]), so nothing reaches them from the API layer.

## Shared scaffold's planned REST contract

`/api/v1`, JSON camelCase, ISO 8601 UTC. Brief specifies UUID IDs, but the domain has since moved to `Id64`; format on the wire is still unsettled.

| Service | Routes |
|---|---|
| Users | `POST /auth/login`, `GET/POST/PATCH/DELETE /users/{id}`, `PATCH /users/{id}/status` |
| Accommodations | `GET /accommodations`, `GET/POST/PATCH /accommodations/{id}` |
| Transport | `GET /transport-modes`, `GET /transport-modes/{id}` |
| Attractions | `GET/POST /attractions`, `GET/PATCH /attractions/{id}` |
| Trips | `GET /trips?userId=`, `POST /trips`, `GET/PATCH/DELETE /trips/{id}`, `PUT /trips/{id}/legs`, `GET /trips/{id}/map-points` |
| Settings | `GET/PUT /settings/{userId}` |
| Ticket import (future) | `POST /ticket-imports`, `GET /ticket-imports/{id}`, `POST /ticket-imports/{id}/confirm` |
| All | `GET /health` |

Response codes: `201` create, `404` not found, `400` invalid trip direction/times, `403` forbidden (e.g. banned user write), `503` dependency unavailable.

Trips validates referenced user/place/transport IDs via HTTP calls to the owning service at write time; each service owns its own store, none read another's tables.

## Ownership

Users/Accommodations: Christel. Transport/Attractions: Artiom. Trips/Settings: James.


## Implemented standalone APIs

The human reduced the current demonstration to Attractions (5104), Transport entries and Route Points (5103). Both APIs also expose GET /health with status ok and their service name.

| Method and route | Service | Result |
| --- | --- | --- |
| GET /api/v1/attractions/{id} | Attractions | Saved attraction, or 404 |
| POST /api/v1/attractions | Attractions | 201, saved attraction and details Location |
| GET /api/v1/transport-modes/{id} | Transport | Saved transport entry, or 404 |
| POST /api/v1/transport-modes | Transport | 201, saved entry and details Location |
| GET /api/v1/route-points/{id} | Transport | Saved route point, or 404 |
| POST /api/v1/route-points | Transport | 201, saved point and details Location |

All IDs are generated GUIDs; JSON fields use camelCase. Standard ASP.NET Core model binding and DataAnnotations produce 400 validation problem details for invalid input or malformed IDs. Unknown JSON fields follow the framework default and are ignored; request models do not expose server-owned IDs. GET never creates a missing record.

### Attraction fields

Response: attractionId, name, description, rating, address, lat, lon, estimatedVisitMinutes. POST requires a nonblank name, latitude (-90 to 90), longitude (-180 to 180), and nonnegative integer estimatedVisitMinutes. Names are trimmed. Description and rating are optional; null rating means no score, otherwise 0-5 inclusive. Omitted address becomes an empty string; explicit null address is invalid.

### Transport entry fields

Response: transportModeId, code, name, description. POST requires a nonblank name and one exact lowercase code: car, bus, train, pedestrian, bike or plane. Description is optional. Names are trimmed; multiple entries may share a code. ITransportModeService manages entries without journey dates.

### Route point fields

Response: routePointId, type, name, address, lat, lon, sourceId. POST requires an exact lowercase type (accommodation, attraction or transport_stop), nonblank name and valid coordinates. Address defaults to empty and cannot be explicitly null. Optional sourceId is unverified GUID metadata; the API does not contact another service. A point is a reusable location, not a scheduled journey.

Each API owns seeded process-lifetime memory. Its singleton repositories share records between requests; restarting restores seeds and discards created data. Scoped services contain creation/lookup logic. Each resource has one controller, one service and one repository, with related GET/POST methods together.

Collection GET/search, PATCH and scheduled transport routes/stops/timing calculations are deferred. The earlier [issue #15 contract](milestone-2/issue-9/feature-spec.md) remains historical and is not fully implemented by this reduced scope. [Current specification](milestone-2/implementation/specification.md), [Attractions examples](../src/TravelTracker.Attractions.Api/requests.http), [Transport examples](../src/TravelTracker.Transport.Api/requests.http).
