# TravelTracker

Two independently runnable .NET 10 services now demonstrate Artiom's Attractions and Transport responsibilities. Both use fixed sample data and process-lifetime memory; restarting a service discards its created/updated records.

## Run

From the repository root, use separate terminals:

```powershell
dotnet run --project src/TravelTracker.Attractions.Api --launch-profile http
```

```powershell
dotnet run --project src/TravelTracker.Transport.Api --launch-profile http
```

Attractions health: [localhost:5104/health](http://localhost:5104/health). Transport health: [localhost:5103/health](http://localhost:5103/health). Each works with the other services stopped.

Attractions supports health, list/name search, details, POST creation and PATCH updates. Transport supports named entries with modes/descriptions, typed accommodation/attraction/transport-stop points, and scheduled routes with departure/arrival, preparation and ordered stop timings. A normal transport POST creates a catalogue entry; a route POST creates its schedule.

Read the [API contract](docs/api.md) and copy requests from the [Attractions examples](src/TravelTracker.Attractions.Api/requests.http) and [Transport examples](src/TravelTracker.Transport.Api/requests.http). POST returns a details Location; GET that URL to retrieve the new record. Times require explicit UTC offsets. Route-point sourceId is optional metadata, and potential stop windows are not attraction suggestions.

## Verify

Each standalone API has an xUnit project with its tests and internal test-host/helpers in one file, following the requested LAB_07-style layout:

- [AttractionsTests.cs](tests/TravelTracker.Attractions.Api.Tests/AttractionsTests.cs)
- [TransportTests.cs](tests/TravelTracker.Transport.Api.Tests/TransportTests.cs)

```powershell
dotnet test tests/TravelTracker.Attractions.Api.Tests --configuration Release --logger "trx;LogFileName=attractions.trx"
dotnet test tests/TravelTracker.Transport.Api.Tests --configuration Release --logger "trx;LogFileName=transport.trx"
```

Current suite: **15 Attractions cases + 15 Transport cases = 30 total**, all passing. Each theory input counts toward the human-requested maximum of 30. [Test overlap review and reductions](docs/milestone-2/implementation/test-review.md) explains what was kept and omitted. Earlier verification also passed 18 real-process smoke assertions on ports 5104/5103, including actual restarts; those checks were not rerun for the test reduction. [Evidence and commands](docs/milestone-2/implementation/validation.md).

The original shared `TravelTracker.Api`, `TravelTracker.Data` and `TravelTracker.Domain` remain an unfinished scaffold. Full-solution restore succeeds, but its build still fails at the pre-existing `Id64.cs:14` CS1503. Shared Trip/settings/read-model gaps are documented separately. The independent APIs/tests do not reference those projects; passing service checks do not mean the full solution builds.

## Milestone 2 records

[Implementation package](docs/milestone-2/implementation/README.md): approved specification, fresh Copilot plan and review adjustments, source/test evidence, review record, reflection and explicit tool provenance.

[Issue #9 planning package](docs/milestone-2/issue-9/README.md): GitHub MCP setup, reviewed [Attractions specification #15](https://github.com/NewWaveOwl/TravelTracker/issues/15), triage and priming interview. The workspace MCP configuration contains no credentials; each teammate signs in separately.

[Coding skill](.github/skills/traveltracker-code/SKILL.md) and [testing skill](.github/skills/traveltracker-testing/SKILL.md) apply the course patterns, one feature per production file, one test file per API, and `/** @param @return */` documentation. Controllers/services are split into partial feature files to preserve LAB_07's controller -> service -> repository flow.
