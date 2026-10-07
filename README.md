# TravelTracker

Two independently runnable .NET 10 APIs demonstrate Artiom's Attractions and Transport responsibilities with seeded in-memory data.

## Run

From the repository root, use separate terminals:

```powershell
dotnet run --project src/TravelTracker.Attractions.Api --launch-profile http
dotnet run --project src/TravelTracker.Transport.Api --launch-profile http
```

Attractions: [localhost:5104/health](http://localhost:5104/health). Transport: [localhost:5103/health](http://localhost:5103/health). Each works independently.

The current implementation contains three resources, each with **POST and GET by ID**:

| Resource | API | Routes |
| --- | --- | --- |
| Attractions | 5104 | POST /api/v1/attractions; GET /api/v1/attractions/{id} |
| Transport entries | 5103 | POST /api/v1/transport-modes; GET /api/v1/transport-modes/{id} |
| Route points | 5103 | POST /api/v1/route-points; GET /api/v1/route-points/{id} |

Transport entries have a mode code, name and optional description. Route points have a type (accommodation, attraction or transport_stop), name and location. POST returns 201, a generated GUID, the saved record and a details Location. GET returns 200 or 404. Basic invalid input returns 400. Created records last until that API restarts.

Read the [API contract](docs/api.md), [Attractions examples](src/TravelTracker.Attractions.Api/requests.http) and [Transport examples](src/TravelTracker.Transport.Api/requests.http). Collection listing, name search, PATCH, scheduled journeys and stop-time calculations are deferred following the human's request for a simple implementation.

## Verify

Each API has its own xUnit project and one test source file, following the requested LAB_07 layout:

- [AttractionsTests.cs](tests/TravelTracker.Attractions.Api.Tests/AttractionsTests.cs)
- [TransportTests.cs](tests/TravelTracker.Transport.Api.Tests/TransportTests.cs)

```powershell
dotnet test TravelTracker.Tests.slnx --configuration Release
```

The separate [test solution](TravelTracker.Tests.slnx) groups both xUnit projects and runs all tests with this one command. It builds their referenced APIs and starts isolated test hosts automatically; no manually running API is needed. Use the explicit solution filename because the repository contains both the full solution and this test solution.

Current results: **7 Attractions + 13 Transport/Route Point cases = 20, all passing**. Each theory row counts as a case. Eight additional live-process smoke checks passed on 5104/5103. [Validation evidence](docs/milestone-2/implementation/validation.md) and [test selection](docs/milestone-2/implementation/test-review.md) describe the checks and their limits.

The original shared API/Data/Domain projects remain an unfinished scaffold. The full-solution build still fails at the existing Id64.cs:14 CS1503. The standalone APIs/tests build successfully and have no references to those shared projects.

## Milestone 2 records

[Current implementation package](docs/milestone-2/implementation/README.md): revised scope, reviewed plan, source/tests, review record, reflection and tool provenance.

[Historical issue #9 planning package](docs/milestone-2/issue-9/README.md): GitHub MCP setup, [original Attractions specification #15](https://github.com/NewWaveOwl/TravelTracker/issues/15), triage and priming interview. Issue #15 also requests search/PATCH, so this reduced implementation does not complete that full issue.

[Coding skill](.github/skills/traveltracker-code/SKILL.md) and [testing skill](.github/skills/traveltracker-testing/SKILL.md) preserve one cohesive resource responsibility per production file, one test file per API, and /** @param @return */ comments. Controllers, services and repositories group GET-by-ID and POST together as in LAB_07.
