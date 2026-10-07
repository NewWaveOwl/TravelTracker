# Current executed validation

TravelTracker.Api is now the only runnable web project. Controllers/services use the original Data/Domain layers and existing Attraction/TransportMode interfaces. Current structured evidence is under integratedLayers in [verification.json](verification.json). Earlier combinedHost/mainCodeDocumentation results are historical.

| Check | Executed result |
| --- | --- |
| Pre-integration test baseline | 19 passed using previously built assemblies |
| Full-solution restore | All four projects restored |
| Full-solution Release build | Passed; 0 warnings, 0 errors |
| Test solution | 19 executed, 19 passed, 0 failed/skipped |
| Live Swagger/API | Eight checks passed on the integrated host at 5104 |
| Collaborator-source audit | 26 original files present, 20 unchanged, six intentional integration updates; all 11 interface files and all original model properties retained |
| Project layout | Api is the only web SDK project; Data/Domain are class libraries; one Api.Tests project/file |

Executed commands from the repository root:

```powershell
dotnet restore TravelTracker.slnx --configfile "K:\CLASSES 26 FALL\WEB_3\.task-support\issue9\implementation-nuget.config"
dotnet build TravelTracker.slnx --configuration Release --no-restore
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --logger "trx;LogFileName=integrated-api.trx"
dotnet run --project src/TravelTracker.Api --configuration Release --no-build --no-restore --launch-profile http
```

The task-local NuGet config selects nuget.org for this environment; collaborators can use their normal NuGet configuration. TRX is saved in the ignored tests/TravelTracker.Api.Tests/TestResults/integrated-api.trx. All 19 original test bodies are preserved. The first baseline build attempt encountered a file locked by the old running WebApi; that host was stopped, and the baseline ran with --no-build --no-restore before migration. This was a process-lock limitation rather than a failed test.

Live checks verified /swagger/index.html, /swagger/v1/swagger.json, health, three POST -> Location GET workflows, unknown attraction 404 and invalid decimal rating 400. The document contains exactly seven operations and the six existing request/response schemas. The integrated Development host is left running for inspection.

The Id64 constructor's missing explicit conversion is fixed. Trip, TripLeg, UserSettings and TripMapPoint now have empty partial declarations only, allowing their original interfaces to compile. No fields, runtime implementation or endpoint is provided for these types. Full-solution success does not imply those future features work.

The [relationship design](../../trip-points-and-routes.md) distinguishes reusable points/entries from future scheduled routes and trip occurrences. Listing/search, PATCH, scheduling, auth and SQL persistence remain outside scope. [Test selection](test-review.md), [review record](review-record.md).
