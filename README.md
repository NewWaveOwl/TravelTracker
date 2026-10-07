# TravelTracker

One .NET 10 API hosts Attractions, Transport entries and Route Points together, following LAB_07's controller -> service -> repository structure. All current endpoints and models appear on one Swagger page.

## Run and use Swagger

From the repository root:

```powershell
dotnet run --project src/TravelTracker.WebApi --launch-profile http
```

Open [localhost:5104/swagger](http://localhost:5104/swagger). Expand Attractions, TransportModes or RoutePoints, choose **Try it out**, enter an ID or JSON body, then **Execute**. POST returns 201 with the saved record and a details Location; use the returned GUID in GET by ID. Swagger's Schemas section shows the request and response models. Swagger is enabled in Development, which the http launch profile selects.

| Resource | Routes on http://localhost:5104 |
| --- | --- |
| Health | GET /health |
| Attractions | POST /api/v1/attractions; GET /api/v1/attractions/{id} |
| Transport entries | POST /api/v1/transport-modes; GET /api/v1/transport-modes/{id} |
| Route points | POST /api/v1/route-points; GET /api/v1/route-points/{id} |

Transport entries contain a mode code, name and optional description. Route points represent accommodation, attraction or transport_stop locations. See the [API contract](docs/api.md) and [HTTP examples](src/TravelTracker.WebApi/requests.http). Names, allowed types/modes, rating and coordinates are validated. Created records last until the shared API restarts. Listing/search, PATCH and scheduled journeys remain future work.

## Run all tests

One xUnit project references the combined API, with all tests/helpers in [TravelTrackerTests.cs](tests/TravelTracker.WebApi.Tests/TravelTrackerTests.cs), like LAB_07:

```powershell
dotnet test TravelTracker.Tests.slnx --configuration Release
```

The [test solution](TravelTracker.Tests.slnx) groups the API and test project and starts isolated test hosts automatically. You do not need to launch the API manually for tests. Use the explicit solution filename because both the full and test solutions exist.

Current verification: **19 xUnit cases passed**, plus **eight live checks** including the Swagger page/document and all three POST -> GET workflows on the same host. The former two-project suite had 20 cases; one duplicate health case was removed. [Validation](docs/milestone-2/implementation/validation.md) and [test selection](docs/milestone-2/implementation/test-review.md).

The original TravelTracker.Api/Data/Domain/SQL files remain an unfinished scaffold and are not referenced by the active WebApi. The full solution still fails at the existing shared Id64.cs:14 CS1503. The combined API and test solution build successfully. The old separate Attractions/Transport API projects have been replaced by WebApi.

## Preserved main code and trip design

All 26 tracked files in the original API/Data/Domain/Database directories match the checked `master` commit `7c55367`. This keeps Christel's User/Accomodation models and repositories, Artiom's original Attraction/TransportMode models and contracts, and James's Trips/Settings/map interfaces. See the linked inventories of [models](docs/domain.md), [service interfaces](docs/api.md) and [repositories/SQL](docs/data.md).

The [trip-point, transport and route design](docs/trip-points-and-routes.md) shows how reusable route points and transport entries can later support scheduled routes and ordered trip legs. Trips/scheduling are planned; their docs do not add endpoints or change the collaborators' source.

## Milestone 2 records

[Current implementation package](docs/milestone-2/implementation/README.md): accepted scope, reviewed plan, test evidence, review record and reflection.

[Historical issue #9 planning package](docs/milestone-2/issue-9/README.md): GitHub MCP setup, [original Attractions issue #15](https://github.com/NewWaveOwl/TravelTracker/issues/15), triage and priming. Issue #15 also requests search/PATCH, so the reduced change does not fully complete it.

[Coding skill](.github/skills/traveltracker-code/SKILL.md) and [testing skill](.github/skills/traveltracker-testing/SKILL.md) preserve separate resource/layer files, one API host, one test project/file, and /** @param @return */ documentation.
