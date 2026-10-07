# Current executed validation

The reduced implementation was checked on 2026-10-06 after the human retained Attractions, Transport entries and Route Points with POST/GET-by-ID only. Current results are under simpleImplementation in [verification.json](verification.json). Earlier 114-/30-case runs and older live checks are historical and do not prove the current source.

| Check | Current result |
| --- | --- |
| Attractions Release build/tests | 7 executed, 7 passed, 0 failed, 0 skipped |
| Transport Release build/tests | 13 executed, 13 passed, 0 failed, 0 skipped |
| Independent real API processes on 5104/5103 | Eight checks passed; both health endpoints, three POST -> Location GET workflows and three missing-ID 404s |
| Full-solution Release build | Failed at the pre-existing shared Id64.cs:14 CS1503; zero warnings |
| Full-solution tests | Not run after failed build |

Executed targeted commands from the repository root:

```powershell
dotnet test tests/TravelTracker.Attractions.Api.Tests/TravelTracker.Attractions.Api.Tests.csproj --configuration Release --no-restore --logger "trx;LogFileName=attractions-simple.trx" --results-directory tests/TravelTracker.Attractions.Api.Tests/TestResults
dotnet test tests/TravelTracker.Transport.Api.Tests/TravelTracker.Transport.Api.Tests.csproj --configuration Release --no-restore --logger "trx;LogFileName=transport-simple.trx" --results-directory tests/TravelTracker.Transport.Api.Tests/TestResults
dotnet build TravelTracker.slnx --configuration Release --no-restore
```

The test commands built both APIs and test projects before execution. They reused previously restored packages and unchanged project/package references. Current TRX counters were parsed into the committed JSON; generated files remain ignored. The earlier sandbox-specific NuGet config workaround is historical and not a teammate setup requirement.

The live check launched one compiled API at a time, with the other stopped, on its normal port. POST returned 201 with a usable Location; following it returned 200 and identical saved JSON. Unknown resource GUIDs returned 404. All owned processes were stopped afterward. No real restart test or scheduled journey check was run against the reduced source.

Delivery verification checks local links, solution paths, valid MCP/evidence JSON, block comment style, skill format, whitespace and preservation of the original shared API/Data/Domain/SQL source. [Coverage selection and limits](test-review.md), [review record](review-record.md).

The owned APIs/tests pass. The full solution does not build; no full-solution success, production authorization, database behavior, live transit accuracy or new Copilot GUI session is claimed.
