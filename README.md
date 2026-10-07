# TravelTracker

One .NET 10 application uses the existing project structure: **TravelTracker.Api** hosts controllers and services, **TravelTracker.Data** owns repositories, and **TravelTracker.Domain** owns models. Attractions, Transport entries and Route Points appear on one Swagger page, following LAB_07.

## Run and use Swagger

From the repository root:

```powershell
dotnet run --project src/TravelTracker.Api --launch-profile http
```

Open [localhost:5104/swagger](http://localhost:5104/swagger). Expand a resource, choose **Try it out**, enter an ID or request body, then **Execute**. POST returns 201 with a saved representation and details Location; use its GUID in GET by ID. The http profile enables Development Swagger.

| Resource | Routes on http://localhost:5104 |
| --- | --- |
| Health | GET /health |
| Attractions | POST /api/v1/attractions; GET /api/v1/attractions/{id} |
| Transport entries | POST /api/v1/transport-modes; GET /api/v1/transport-modes/{id} |
| Route points | POST /api/v1/route-points; GET /api/v1/route-points/{id} |

Names, supported modes/types, rating and coordinates are validated. Created records last until the API restarts. Transport entries hold mode/name/description; route points represent accommodation, attraction or transport-stop locations. [API contract](docs/api.md), [HTTP examples](src/TravelTracker.Api/requests.http).

## Run all tests

One xUnit project references the existing API. Tests are grouped by the resource or purpose they verify:

| File | Aim | Cases |
| --- | --- | --- |
| [HealthTests.cs](tests/TravelTracker.Api.Tests/HealthTests.cs) | Shared API health and service identity | 1 |
| [AttractionTests.cs](tests/TravelTracker.Api.Tests/AttractionTests.cs) | Attraction lookup, creation and validation | 6 |
| [TransportModeTests.cs](tests/TravelTracker.Api.Tests/TransportModeTests.cs) | Transport entry lookup, creation and validation | 5 |
| [RoutePointTests.cs](tests/TravelTracker.Api.Tests/RoutePointTests.cs) | Route-point lookup, creation and validation | 7 |

[ApiFactory.cs](tests/TravelTracker.Api.Tests/Support/ApiFactory.cs) creates isolated HTTP hosts; [HttpTestSupport.cs](tests/TravelTracker.Api.Tests/Support/HttpTestSupport.cs) shares creation/validation assertions. These helpers contain no test cases. Run all four files together:

```powershell
dotnet test TravelTracker.Tests.slnx --configuration Release
```

The [test solution](TravelTracker.Tests.slnx) includes Api, Data, Domain and the test project. Isolated test hosts start automatically; a manually running API is unnecessary. Use the explicit solution filename because both solution files exist.

[Validation and test coverage](https://github.com/NewWaveOwl/TravelTracker/blob/milestone_2_AI_and_development_resource/devlopmet_flow/atiom_developmet_flow/implementation/validation.md) are recorded on the resource branch.

## Models and design

The API uses the original Attraction/TransportMode entities and interfaces. HTTP DTOs map DisplayName to name and Latitude/Longitude to lat/lon. RoutePoint is a reusable location. Trip, TripLeg, UserSettings and TripMapPoint are compile-only declarations with no runtime endpoints.

[Models](docs/domain.md), [API contracts](docs/api.md), [repositories/SQL](docs/data.md) and [trip-point/transport/route design](docs/trip-points-and-routes.md) describe the current implementation and future relationships. Scheduling, Trips, collection search and PATCH remain future work.

## Milestone 2 development records

The [AI evidence](https://github.com/NewWaveOwl/TravelTracker/tree/milestone_2_AI_and_development_resource/AI_evdence/atiom_evdence) and [development flow](https://github.com/NewWaveOwl/TravelTracker/tree/milestone_2_AI_and_development_resource/devlopmet_flow/atiom_developmet_flow) live on `milestone_2_AI_and_development_resource`. They include issue #9 planning, original prompts/responses, interview answers, reviewed plans, validation, reflection and the conversation/decision record.

[Coding skill](.github/skills/traveltracker-code/SKILL.md) and [testing skill](.github/skills/traveltracker-testing/SKILL.md) remain available for everyday development.
