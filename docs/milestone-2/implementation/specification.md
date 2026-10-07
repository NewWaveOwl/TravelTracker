# Simple Attractions, Transport and Route Points

## Accepted scope

On 2026-10-06 the human requested a simple POST and GET implementation. In the follow-up scope question they answered "route points ,TRanport ansd atractioins". The current scope therefore retains those three resources and removes scheduled journeys and their stop/time machinery. The earlier broader [Attractions issue #15](../issue-9/feature-spec.md) remains historical; collection search and PATCH are deferred.

Use C#/ASP.NET Core .NET 10, LAB_07's controller -> service -> repository flow, seeded process-lifetime memory, the repository skills and /** @param @return */ documentation. The human subsequently requested one application like LAB_07. TravelTracker.WebApi hosts all three resources on 5104, with a shared GET /health and Development Swagger page at /swagger. The service field in health is TravelTracker. The original two-host requirement is superseded for this demonstration.

## Resource contract

| Resource | Fields returned | Operations |
| --- | --- | --- |
| Attraction | attractionId, name, description, rating, address, lat, lon, estimatedVisitMinutes | POST /api/v1/attractions; GET /api/v1/attractions/{id} |
| Transport entry | transportModeId, code, name, description | POST /api/v1/transport-modes; GET /api/v1/transport-modes/{id} |
| Route point | routePointId, type, name, address, lat, lon, sourceId | POST /api/v1/route-points; GET /api/v1/route-points/{id} |

POST generates a GUID and returns 201, the saved record and its details Location. GET returns 200 with the record, or 404 for an unknown GUID. ASP.NET Core binding/DataAnnotations return 400 validation problem details for invalid input or malformed IDs. Unknown JSON properties follow standard framework behavior and are ignored. Request DTOs omit server-owned IDs.

Names are required, cannot be whitespace-only and are trimmed before saving. Coordinates are required and bounded by latitude -90..90 and longitude -180..180. Attraction visit minutes are a required nonnegative integer; description/rating are optional and rating must be null or 0..5 inclusive. Address defaults to empty; explicit null is invalid.

Transport code is an exact lowercase car, bus, train, pedestrian, bike or plane; walking uses pedestrian. Entries contain name and optional description, without journey dates. Route-point type is an exact lowercase accommodation, attraction or transport_stop. Optional sourceId is unverified GUID metadata; no cross-service call or database access is performed.

Each resource groups its two operations in one controller, one service and one repository. Immutable records and request/interface types have their own files. Scoped services use thread-safe singleton in-memory repositories. Program only composes the API. The original shared API/Data/Domain/SQL scaffold is unchanged.

## Acceptance and limits

Verify health, seeded lookup, POST -> Location GET, missing IDs and representative validation. Keep one xUnit project, all tests/helpers in TravelTrackerTests.cs, and at most 30 discovered cases including theory rows. Current selection is 19 cases after removing the duplicate health check. Verify the shared real process, Swagger document/models and all three resource workflows on the same port, and recheck the full build.

Collection GET, name search, PATCH, scheduled routes, departure/arrival/boarding/dwell calculations, authentication and Trips integration are future work. Stops are represented as route-point locations only. The pre-existing shared Id64.cs:14 CS1503 still prevents a successful full-solution build; standalone checks are reported separately.
