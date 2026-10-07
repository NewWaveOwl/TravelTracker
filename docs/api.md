# API Layer

HTTP contracts for the shared scaffold and the implemented combined API.

## Current state

The existing [TravelTracker.Api](../src/TravelTracker.Api) is the only web host. Its controllers/services use TravelTracker.Data repositories and TravelTracker.Domain entities. All three implemented resources share port 5104 and /swagger. Original service-interface signatures are unchanged.

## Existing service interfaces

| Preserved interface | Wraps | Declared methods |
|---|---|---|
| [IAttractionService](../src/TravelTracker.Api/Services/IAttractionService.cs) | `Attraction` | `GetAllAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` |
| [ITransportModeService](../src/TravelTracker.Api/Services/ITransportModeService.cs) | `TransportMode` | `GetAllAsync`, `GetByIdAsync`, `GetByCodeAsync`, `CreateAsync`, `UpdateAsync` |
| [ISettingsService](../src/TravelTracker.Api/Services/ISettingsService.cs) | `UserSettings` (empty type declaration) | `GetOrCreateAsync`, `UpdateAsync` |
| [ITripService](../src/TravelTracker.Api/Services/ITripService.cs) | `Trip`, `TripLeg` (empty type declarations) | `GetHistoryAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `ReplaceLegsAsync` |
| [ITripMapService](../src/TravelTracker.Api/Services/ITripMapService.cs) | `TripMapPoint` (empty type declaration) | `GetMapPointsAsync` |

The Attraction and TransportMode interfaces now have concrete implementations wired into Api controllers. Trips/Settings/map retain their existing contracts but have no runtime implementation or endpoint. Empty Domain declarations supply their referenced types for compilation. Every entity identity is an `EntityId<T>`: an internal `Id64` key plus a public uuidv7. The key is `internal` to Domain and Data, so the Api cannot name or expose it; edge-facing signatures take the public `Guid`. `ITripService.ReplaceLegsAsync` returns the saved legs, while its repository counterpart returns only task completion.

No `IUserService` or `IAccommodationService` exists yet. Their models and internal repositories are retained (see [Data](data.md)), but there are no user/accommodation endpoints to display in Swagger.

## Shared scaffold's planned REST contract

`/api/v1`, JSON camelCase, ISO 8601 UTC. Current User, Accomodation, Attraction, TransportMode and RoutePoint identifiers are GUIDs; the wire format is always the public uuidv7. The future Trips/Settings wire format still needs agreement.

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

The planned Trips service will validate referenced user/place/transport IDs via HTTP calls to the owning service at write time; each service will own its own store. This integration is not implemented by the current catalogue demo.

## Ownership

Users/Accommodations: Christel. Transport/Attractions: Artiom. Trips/Settings: James.


## Implemented combined API

Attractions, Transport entries and Route Points share http://localhost:5104 and one Swagger document. GET /health returns status ok and service TravelTracker. Swagger is enabled in Development.

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

The Api controllers use the original asynchronous ITransportModeService and IAttractionService with their Domain entities; their Data implementations use the original repository interfaces. The new [IRoutePointService](../src/TravelTracker.Api/Services/IRoutePointService.cs) follows the same layer boundary. Response DTOs map DisplayName to name and Latitude/Longitude to lat/lon. Existing interface methods for collection/update/delete remain available internally, but no additional HTTP operations are exposed.

### Route point fields

Response: routePointId, type, name, address, lat, lon, sourceId. POST requires an exact lowercase type (accommodation, attraction or transport_stop), nonblank name and valid coordinates. Address defaults to empty and cannot be explicitly null. Optional sourceId is unverified GUID metadata; the API does not contact another service. A point is a reusable location, not a scheduled journey.

The shared host owns seeded process-lifetime memory. Its singleton repositories share records between requests; restarting restores all three catalogues and discards created data. Scoped services contain creation/lookup logic. Each resource has one controller, one service and one repository, with related GET/POST methods together.

Collection GET/search, PATCH and scheduled transport routes/stops/timing calculations remain future work. [Current specification](https://github.com/NewWaveOwl/TravelTracker/blob/milestone_2_AI_and_development_resource/devlopmet_flow/atiom_developmet_flow/implementation/specification.md), [HTTP examples](../src/TravelTracker.Api/requests.http).

For future Trips integration, a trip point is an ordered occurrence of a reusable route point. A scheduled route connects route points using a transport entry; a trip leg selects that journey. Dates and boarding/stop timings belong to the scheduled route and occurrences, rather than the transport catalogue POST. See [trip points, transport and routes](trip-points-and-routes.md).
