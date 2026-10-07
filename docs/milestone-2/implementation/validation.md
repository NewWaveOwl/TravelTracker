# Current executed validation

TravelTracker.Api is the only runnable web project. Controllers/services use the original Data/Domain layers and existing Attraction/TransportMode interfaces. The latest test split and documentation review were checked on 2026-10-07 and are recorded under testOrganization in [verification.json](verification.json). integratedLayers records the preceding integration; older combinedHost/mainCodeDocumentation results remain historical.

| Check | Executed result |
| --- | --- |
| Before-split baseline at 8ead33a | 19 passed using previously built assemblies |
| Full-solution Release build after split | All four projects build; 0 warnings, 0 errors |
| Test solution after split | 19 executed, 19 passed, 0 failed/skipped |
| Test preservation/duplicates | All 16 method blocks and theory inputs retained; unchanged helper bodies; 19 distinct discovered cases, no additions/removals |
| Live Swagger/API | Eight checks passed on the shared host at 5104 |
| Current code/docs agreement | Source, API/model/data/design contracts, launch settings and Swagger reviewed; implemented operations/fields/validation/lifetimes agree; scheduling remains explicitly planned |
| Production preservation | No src changes relative to 8ead33a; prior audit retains all 26 original master files, all 11 original interfaces and all original model properties |
| Project layout | Api is the only web SDK project; Data/Domain are class libraries; one Api.Tests project with four test files and two shared helper files |

Executed commands from the repository root:

```powershell
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --logger "trx;LogFileName=before-test-split.trx"
dotnet build TravelTracker.slnx --configuration Release --no-restore
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --logger "trx;LogFileName=test-split.trx"
dotnet run --project src/TravelTracker.Api --configuration Release --no-build --no-restore --launch-profile http
```

Existing restored dependencies were reused; no packages or production code changed. TRX results are saved in the ignored tests/TravelTracker.Api.Tests/TestResults directory. For a normal run that also builds/restores as needed, use `dotnet test TravelTracker.Tests.slnx --configuration Release`. Test hosts start automatically; a manually running server is unnecessary. The live demo host was stopped before rebuilding to avoid its executable lock, then restarted for Swagger inspection.

Live checks verify /swagger/index.html, /swagger/v1/swagger.json, health, three POST -> Location GET workflows, an unknown attraction returning 404 and an invalid decimal rating returning 400. The document contains exactly seven operations and the six agreed request/response schemas. The Development host is left running on port 5104.

Documentation review found stale one-file test guidance and corrected README, Copilot instructions, both skills and current milestone records. The planned API overview also now distinguishes GUID catalogue/User/Accomodation identifiers from Id64 in Trips/Settings interfaces. The runtime already agrees with the accepted simple HTTP contract; no production correction was needed. Tests remain HTTP integration cases grouped by resource, with fresh memory stores per case. Historical Copilot records are retained; no new Copilot session is claimed.

The earlier integration fixed Id64 and supplied empty partial Trip, TripLeg, UserSettings and TripMapPoint declarations for existing interfaces. Those declarations provide no fields or runtime endpoints. The [relationship design](../../trip-points-and-routes.md) distinguishes current reusable points/entries from future scheduled routes and trip occurrences. Listing/search, PATCH, scheduling, auth and SQL persistence remain outside scope. [Test selection](test-review.md), [review record](review-record.md).
