# API Layer

HTTP surface for TravelTracker (`TravelTracker.Api`).

## Current state

`Program.cs` is a bare bootstrap, no controllers, no endpoints, no `GET /health` yet.

## Service interfaces

| Interface | Wraps | Key methods |
|---|---|---|
| `IAttractionService` | `Attraction` | get all, get by ID, create, update, delete |
| `ITransportModeService` | `TransportMode` | get all, get by ID, get by code, create, update |
| `ISettingsService` | `UserSettings` (not implemented) | get or create, update |
| `ITripService` | `Trip`, `TripLeg` (not implemented) | history, get by ID, create, update, delete, replace legs |
| `ITripMapService` | `TripMapPoint` (read-model) | get map points |

No `IUserService` or `IAccommodationService` yet; their repositories are `internal` (see [[data]]), so nothing reaches them from the API layer.

## Planned REST contract

`/api/v1`, JSON camelCase, ISO 8601 UTC. Brief specifies UUID IDs, but the domain has since moved to `Id64`; format on the wire is still unsettled.

| Service | Routes |
|---|---|
| Users | `POST /auth/login`, `GET/POST/PATCH/DELETE /users/{id}`, `PATCH /users/{id}/status` |
| Accommodations | `GET /accommodations`, `GET/POST/PATCH /accommodations/{id}` |
| Transport | `GET /transport-modes`, `GET /transport-modes/{id}` |
| Attractions | `GET /attractions`, `GET/POST/PATCH /attractions/{id}` |
| Trips | `GET /trips?userId=`, `POST /trips`, `GET/PATCH/DELETE /trips/{id}`, `PUT /trips/{id}/legs`, `GET /trips/{id}/map-points` |
| Settings | `GET/PUT /settings/{userId}` |
| Ticket import (future) | `POST /ticket-imports`, `GET /ticket-imports/{id}`, `POST /ticket-imports/{id}/confirm` |
| All | `GET /health` |

Response codes: `201` create, `404` not found, `400` invalid trip direction/times, `403` forbidden (e.g. banned user write), `503` dependency unavailable.

Trips validates referenced user/place/transport IDs via HTTP calls to the owning service at write time; each service owns its own store, none read another's tables.

## Ownership

Users/Accommodations: Christel. Transport/Attractions: Artiom. Trips/Settings: James.
