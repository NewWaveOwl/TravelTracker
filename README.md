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

Current verification: **the full solution builds with zero warnings/errors; 19 xUnit cases and eight live Swagger/API checks pass**. All test bodies and inputs were preserved when splitting the files; no cases were added. [Validation](docs/milestone-2/implementation/validation.md), [test selection](docs/milestone-2/implementation/test-review.md).

## Existing collaborator code

All 26 original files from master 7c55367 remain present. Twenty are unchanged, including all 11 original service/repository interfaces, User/Accomodation models, role/status enums and SQL. Six have reviewed integration changes: API bootstrap/project/launch configuration, optional attraction description/rating, optional transport description, and the Id64 constructor fix. Original model properties and interface signatures are preserved.

The API now uses the original Attraction/TransportMode entities and interfaces. HTTP response DTOs preserve name/lat/lon aliases while Domain keeps DisplayName/Latitude/Longitude. RoutePoint is a new Domain model. Empty partial Trip, TripLeg, UserSettings and TripMapPoint declarations allow the existing interfaces to compile; these have no fields, services or endpoints. Their feature contracts remain unfinished.

The added WebApi and old Attractions.Api/Transport.Api projects have been removed. Api is the only web host; Data and Domain are class libraries. Their obsolete generated local folders were also cleaned. [Models](docs/domain.md), [interfaces](docs/api.md), [repositories/SQL](docs/data.md).

The [trip-point, transport and route design](docs/trip-points-and-routes.md) explains future scheduled routes, ordered trip occurrences, stop times and boarding. Scheduling, Trips, collection search and PATCH remain future work.

## Milestone 2 records

[Current implementation package](docs/milestone-2/implementation/README.md): scope, plan, tests, review and reflection.

[Historical issue #9 package](docs/milestone-2/issue-9/README.md): MCP setup, [original issue #15](https://github.com/NewWaveOwl/TravelTracker/issues/15), triage and priming. Issue #15 requests search/PATCH as well, so this scoped change does not fully complete it.

[Coding skill](.github/skills/traveltracker-code/SKILL.md) and [testing skill](.github/skills/traveltracker-testing/SKILL.md) retain separate resource/layer files, one existing API host, one test project with files grouped by purpose, and /** @param @return */ documentation.
