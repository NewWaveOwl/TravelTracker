# API Layer

HTTP contracts for the shared scaffold and the implemented combined API.

## Current state

The original [TravelTracker.Api/Program.cs](../src/TravelTracker.Api/Program.cs) remains a bare scaffold bootstrap. Its collaborators' service interfaces are preserved unchanged from `master`. The active [TravelTracker.WebApi](../src/TravelTracker.WebApi) hosts all three implemented resources on port 5104, with one Swagger page at /swagger.

## Shared scaffold service interfaces

| Preserved interface | Wraps | Declared methods |
|---|---|---|
| [IAttractionService](../src/TravelTracker.Api/Services/IAttractionService.cs) | `Attraction` | `GetAllAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` |
| [ITransportModeService](../src/TravelTracker.Api/Services/ITransportModeService.cs) | `TransportMode` | `GetAllAsync`, `GetByIdAsync`, `GetByCodeAsync`, `CreateAsync`, `UpdateAsync` |
| [ISettingsService](../src/TravelTracker.Api/Services/ISettingsService.cs) | `UserSettings` (model missing) | `GetOrCreateAsync`, `UpdateAsync` |
| [ITripService](../src/TravelTracker.Api/Services/ITripService.cs) | `Trip`, `TripLeg` (models missing) | `GetHistoryAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `ReplaceLegsAsync` |
| [ITripMapService](../src/TravelTracker.Api/Services/ITripMapService.cs) | `TripMapPoint` (model missing) | `GetMapPointsAsync` |

These public interfaces declare asynchronous contracts; they have no concrete service implementations or mapped controllers on `master`. Attraction/transport IDs are GUIDs; settings/trip IDs use `Id64`. `ITripService.ReplaceLegsAsync` returns the saved legs, while its repository counterpart returns only task completion.

No `IUserService` or `IAccommodationService` exists yet. Their models and internal repositories are retained (see [Data](data.md)), but there are no user/accommodation endpoints to display in Swagger.

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

The planned Trips service will validate referenced user/place/transport IDs via HTTP calls to the owning service at write time; each service will own its own store. This integration is not implemented by the shared scaffold or the current catalogue demo.

## Ownership

Users/Accommodations: Christel. Transport/Attractions: Artiom. Trips/Settings: James.


## Implemented combined API

The human selected one host like LAB_07. Attractions, Transport entries and Route Points share http://localhost:5104 and one Swagger document. GET /health returns status ok and service TravelTracker. Swagger is enabled in Development.

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

The active [WebApi ITransportModeService](../src/TravelTracker.WebApi/Services/ITransportModeService.cs) declares synchronous `GetById` and `Create` for `TransportModeRecord`. It is separate from the preserved asynchronous interface above. The same separation applies to [WebApi IAttractionService](../src/TravelTracker.WebApi/Services/IAttractionService.cs); [IRoutePointService](../src/TravelTracker.WebApi/Services/IRoutePointService.cs) handles the new location catalogue.

### Route point fields

Response: routePointId, type, name, address, lat, lon, sourceId. POST requires an exact lowercase type (accommodation, attraction or transport_stop), nonblank name and valid coordinates. Address defaults to empty and cannot be explicitly null. Optional sourceId is unverified GUID metadata; the API does not contact another service. A point is a reusable location, not a scheduled journey.

The shared host owns seeded process-lifetime memory. Its singleton repositories share records between requests; restarting restores all three catalogues and discards created data. Scoped services contain creation/lookup logic. Each resource has one controller, one service and one repository, with related GET/POST methods together.

Collection GET/search, PATCH and scheduled transport routes/stops/timing calculations are deferred. The earlier [issue #15 contract](milestone-2/issue-9/feature-spec.md) remains historical and is not fully implemented by this reduced scope. [Current specification](milestone-2/implementation/specification.md), [combined HTTP examples](../src/TravelTracker.WebApi/requests.http).

For future Trips integration, a trip point is an ordered occurrence of a reusable route point. A scheduled route connects route points using a transport entry; a trip leg selects that journey. Dates and boarding/stop timings belong to the scheduled route and occurrences, rather than the transport catalogue POST. See [trip points, transport and routes](trip-points-and-routes.md).
