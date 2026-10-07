# Executed validation

Source commits: Attractions `274672424d0ea0502a8249a3e4e71f1ddc22e0dc`, Transport `65afc228751b848287d34cf33ceaf0327c73e0fe`. SDK: **10.0.401**; both APIs/tests target net10.0. Resolved test packages were verified through NuGet: Microsoft.AspNetCore.Mvc.Testing 10.0.12, Microsoft.NET.Test.Sdk 18.10.1, xunit 2.9.3 and xunit.runner.visualstudio 4.0.0. Exact counters and session IDs are in [verification.json](verification.json).

| Check | Result |
| --- | --- |
| Owned project restores | Passed |
| Attractions Release build and tests | **53 executed, 53 passed, 0 failed, 0 skipped** |
| Transport Release build and tests | **61 executed, 61 passed, 0 failed, 0 skipped** |
| Real-process verification on 5104/5103 | **18 assertions passed**, including repeated health after actual restart |
| Full-solution restore | Passed, all seven projects restored/up to date |
| Full-solution Release build | **Failed at pre-existing Id64.cs:14 CS1503**, zero warnings |
| Full-solution tests | Not run after the failed build; no stale --no-build success claimed |

Run the owned checks from the repository root:

```powershell
dotnet restore tests/TravelTracker.Attractions.Api.Tests
dotnet test tests/TravelTracker.Attractions.Api.Tests --configuration Release --no-restore --logger "trx;LogFileName=attractions.trx"
dotnet restore tests/TravelTracker.Transport.Api.Tests
dotnet test tests/TravelTracker.Transport.Api.Tests --configuration Release --no-restore --logger "trx;LogFileName=transport.trx"
```

The sandbox denied access to the user-wide NuGet.Config, so these executed restores used a temporary config containing only the official nuget.org feed, with CLI/package cache directories under the writable task-support folder. That local workaround is not committed and is not a teammate setup requirement. Generated TRX files remain ignored at each test project's TestResults/attractions.trx or TestResults/transport.trx; their counters were parsed into the committed JSON. The test command builds the API and test project, so these results do not come from unbuilt source.

Attractions covers health/seed schema/order, known/unknown/malformed IDs, POST and usable Location, nullable inclusive ratings, missing/invalid required fields and types, unknown/client-ID fields, literal symbols/single decoding/repeated search queries, GUID ties, presence-aware PATCH/null/zero/rename, atomic rejection, concurrent different-field changes, immutable earlier snapshots and fresh-host reset.

Transport covers six modes and multiple entries per mode, creation without a schedule, all three point types and unknown source metadata, coordinate boundaries, creation/detail Locations, missing/malformed resources/references, direct overnight travel for all modes, ordered multi-stop schedules, UTC normalization/offset requirements, origin preparation/range overflow, stop dwell/boarding boundaries, can-leave gating, invalid/no-partial writes and fresh-host reset.

The live check launched each compiled API independently with the other service stopped, verified POST -> GET, search/PATCH, schedule calculations and invalid writes, then terminated and restarted the actual processes. Created records disappeared and seeds returned. Transport point creation succeeded while Attractions was stopped, confirming sourceId is metadata. All launched processes were stopped after verification.

Delivery checks verified 39 local links across 15 documents, all seven solution project paths, valid MCP/evidence JSON, the requested documentation-block style and no changes to original API/Data/Domain/SQL source. Staged diffs passed whitespace checks before each source commit.

The first code build exposed an incorrect ControllerBase.ValidationProblem overload; it was corrected to ValidationProblemDetails before tests executed. The first Attractions suite exposed a wrong nine-field count in the test; the agreed eight-field schema is now asserted explicitly. All final owned checks above pass. No SQL transactions, real transit accuracy, private authorization, Trips integration, full solution success or VS Code GUI Agent-mode run is claimed.
