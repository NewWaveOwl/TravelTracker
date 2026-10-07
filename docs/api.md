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

## Implemented Attractions API (5104)

| Method and route | Behavior |
| --- | --- |
| GET /health | 200 with status ok and service Attractions |
| GET /api/v1/attractions | Ordered current catalogue |
| GET /api/v1/attractions?name=museum | Trimmed ordinal-ignore-case literal name substring; empty returns all; no match returns [] |
| GET /api/v1/attractions/{id} | Details |
| POST /api/v1/attractions | Create; 201, saved details and Location |
| PATCH /api/v1/attractions/{id} | Partial update; 200 with saved details |

Responses use attractionId, name, description, rating, address, lat, lon and estimatedVisitMinutes. Create requires name/lat/lon/estimatedVisitMinutes; omitted address becomes empty and omitted description/rating become null. Names are trimmed/nonblank, rating is null or 0-5, coordinates obey geographic ranges and minutes are nonnegative. PATCH omission preserves fields, null clears only description/rating, and the complete candidate is validated before atomic replacement. Repeated name queries, unknown/client-ID fields and empty PATCH are invalid. [Full contract](milestone-2/issue-9/feature-spec.md), [HTTP examples](../src/TravelTracker.Attractions.Api/requests.http).

## Implemented Transport API (5103)

| Method and route | Behavior |
| --- | --- |
| GET /health | 200 with status ok and service Transport |
| GET/POST /api/v1/transport-modes | List/create named transport entries, independent of schedules |
| GET /api/v1/transport-modes/{id} | Transport entry details |
| GET/POST /api/v1/route-points | List/create typed location snapshots |
| GET /api/v1/route-points/{id} | Point details |
| GET/POST /api/v1/transport-routes | List/create individual scheduled routes |
| GET /api/v1/transport-routes/{id} | Schedule, saved transport/point snapshots and timing calculations |

Transport creation takes code, name and optional description. Supported codes are car, bus, train, pedestrian, bike and plane; multiple entries can share a code. Point creation takes type (accommodation/attraction/transport_stop), name, lat/lon, optional address and optional sourceId. The source GUID is metadata rather than an independently verified reference.

Route creation takes name, transportModeId, startPointId, destinationPointId, departureAt, arrivalAt, boardingMinutes and optional stops. Each stop has pointId, arrivalAt, departureAt, boardingMinutes and canLeaveVehicle. All references must exist in Transport's own store. Instants require explicit offsets and are returned in UTC. Stops must remain chronological within the journey and preparation must fit stop dwell.

Route responses contain transportRouteId, name, transport/start/destination snapshots, departureAt, arrivalAt, boardingMinutes, boardingStartsAt, elapsedMinutes, totalMinutes and stops. Stop responses add boardingStartsAt, downtimeMinutes and potentialFreeMinutes; the last is zero unless leaving the vehicle is allowed. Elapsed includes stop dwell and total also includes origin preparation. These are supplied-plan calculations, not live transit estimates or attraction suggestions.

All new resources use generated GUIDs. POST returns 201 and Location. Invalid input/malformed IDs return 400 validation problem details; absent resources/references return 404. Lists are ordered by name then GUID. Both services use process-lifetime memory and reset to seeds on restart. The original shared service interfaces remain scaffold contracts; the independently owned interfaces implement this documented surface without importing shared build failures. [Transport specification](milestone-2/implementation/specification.md), [HTTP examples](../src/TravelTracker.Transport.Api/requests.http).
