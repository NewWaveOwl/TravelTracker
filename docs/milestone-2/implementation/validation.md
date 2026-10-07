# Executed validation

Production source commits: Attractions `274672424d0ea0502a8249a3e4e71f1ddc22e0dc`, Transport `65afc228751b848287d34cf33ceaf0327c73e0fe`; subsequent changes only reorganized/reduced tests and updated guidance/evidence. SDK: **10.0.401**; both APIs/tests target net10.0. Resolved test packages were verified through NuGet: Microsoft.AspNetCore.Mvc.Testing 10.0.12, Microsoft.NET.Test.Sdk 18.10.1, xunit 2.9.3 and xunit.runner.visualstudio 4.0.0. Current counters are under `testReduction` in [verification.json](verification.json); the initial counters and `testLayoutRefactor` remain historical evidence.

| Check | Result |
| --- | --- |
| Initial owned project restores | Passed; package/project references have not changed |
| Current Attractions Release build and tests | **15 executed, 15 passed, 0 failed, 0 skipped** |
| Current Transport Release build and tests | **15 executed, 15 passed, 0 failed, 0 skipped** |
| Earlier real-process verification on 5104/5103 | **18 assertions passed**, including repeated health after actual restart; not rerun for test reduction |
| Initial full-solution restore | Passed, all seven projects restored/up to date |
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

The original 53-case Attractions suite covered health/seed schema/order, known/unknown/malformed IDs, POST and usable Location, nullable inclusive ratings, missing/invalid required fields and types, unknown/client-ID fields, literal symbols/single decoding/repeated search queries, GUID ties, presence-aware PATCH/null/zero/rename, atomic rejection, concurrent different-field changes, immutable earlier snapshots and fresh-host reset. Current selection and omitted cases are listed in [the test review](test-review.md).

The original 61-case Transport suite covered six modes and multiple entries per mode, creation without a schedule, all three point types and unknown source metadata, coordinate boundaries, creation/detail Locations, missing/malformed resources/references, direct overnight travel for all modes, ordered multi-stop schedules, UTC normalization/offset requirements, origin preparation/range overflow, stop dwell/boarding boundaries, can-leave gating, invalid/no-partial writes and fresh-host reset. The current 15 cases retain representative workflows and failures, rather than every original variation.

The live check launched each compiled API independently with the other service stopped, verified POST -> GET, search/PATCH, schedule calculations and invalid writes, then terminated and restarted the actual processes. Created records disappeared and seeds returned. Transport point creation succeeded while Attractions was stopped, confirming sourceId is metadata. All launched processes were stopped after verification.

Delivery checks verified 39 local links across 15 documents, all seven solution project paths, valid MCP/evidence JSON, the requested documentation-block style and no changes to original API/Data/Domain/SQL source. Staged diffs passed whitespace checks before each source commit.

The first code build exposed an incorrect ControllerBase.ValidationProblem overload; it was corrected to ValidationProblemDetails before tests executed. The first Attractions suite exposed a wrong nine-field count in the test; the agreed eight-field schema is now asserted explicitly. All final owned checks above pass. No SQL transactions, real transit accuracy, private authorization, Trips integration, full solution success or VS Code GUI Agent-mode run is claimed.

## Human-requested single-file xUnit layout

After reviewing `LABS/LAB_07/Tests_Labs_ProductAPI/Tests_Labs_ProductAPI.csproj` and `Lab07_HttpWorkflowTests.cs`, the tests were reorganized into [AttractionsTests.cs](../../../tests/TravelTracker.Attractions.Api.Tests/AttractionsTests.cs) and [TransportTests.cs](../../../tests/TravelTracker.Transport.Api.Tests/TransportTests.cs). Each independent API retains its own xUnit project and has exactly one C# source file, containing one public test class and internal host/request/assertion helpers. Tests still use `[Fact]`, `[Theory]`, and `WebApplicationFactory<Program>`; no EF test provider is needed for these existing in-memory APIs.

Before reorganization, both Release suites passed: 53 Attractions cases and 61 Transport cases. After reorganization, both projects were rebuilt and the same 114 cases passed with zero failures or skips. Comparing baseline and updated TRX case names, including theory inputs, confirmed that no cases were removed or added; only their containing classes changed. All existing test bodies and assertions were preserved. The original 15 test classes and six separate helper files were consolidated into the two files above.

The full-solution Release build was rechecked and still failed at the existing `Id64.cs:14` CS1503. Production source and project/package references were unchanged. Earlier live-process evidence remains historical; it was not rerun for this test-only reorganization. The repository skills now preserve this explicit test layout while keeping one feature per file for production code. Structured follow-up evidence is under `testLayoutRefactor` in [verification.json](verification.json).

## Human-requested maximum of 30 cases

The 114-case baseline passed before this follow-up. The [overlap review](test-review.md) found no identical normalized test bodies or repeated theory rows within a method, but did find repeated coverage paths. It also identifies distinct edge cases intentionally omitted to meet the new scope; the smaller suite does not claim identical coverage.

Both reduced projects were rebuilt and tested in Release: **15 + 15 = 30 executed cases**, all passed, none skipped or filtered out. The suite has 23 test methods; all twelve theory rows count toward the total. The old validation matrices were removed rather than moved into large loops. The point-role workflow creates three related locations and uses them together in one journey. Production source, package references and one-file-per-API layout are unchanged. Current TRX counters, source hashes and audit results are recorded under `testReduction` in [verification.json](verification.json).
