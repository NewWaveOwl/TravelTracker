# Current executed validation

The three resources now run in one .NET 10 API, TravelTracker.WebApi, with Swagger at http://localhost:5104/swagger. The human-requested LAB_07 deployment supersedes the prior two-host layout. Current structured results are under combinedHost in [verification.json](verification.json); older results remain historical.

| Check | Result |
| --- | --- |
| Combined API/test restore | Passed with Swashbuckle.AspNetCore 10.2.3 and the existing xUnit packages |
| Release test solution | 19 executed, 19 passed, 0 failed, 0 skipped |
| Real shared host on 5104 | Eight live checks passed |
| Full-solution Release build | Existing Id64.cs:14 CS1503 remains; zero warnings |
| Full-solution tests | Not run after the failed build |

From the repository root, run the API and all tests with:

```powershell
dotnet run --project src/TravelTracker.WebApi --launch-profile http
dotnet test TravelTracker.Tests.slnx --configuration Release
```

The exact executed test command was `dotnet test TravelTracker.Tests.slnx --configuration Release --no-restore --logger "trx;LogFileName=combined-api.trx"`. It built the API and test project in Release and wrote the ignored tests/TravelTracker.WebApi.Tests/TestResults/combined-api.trx. One project/file holds all 19 cases. No manually running API is needed for xUnit tests.

The live check verified /swagger/index.html, /swagger/v1/swagger.json and /health, then POST -> Location GET for Attractions, Transport entries and Route Points on the same port. The document contains all seven operations, the six resource/request schemas and creation's 201 responses. Missing attraction ID returned 404; rating 5.1 returned 400. The Development host was left running for the human to inspect Swagger.

The user's existing Swashbuckle 10.2.3 additions were carried into the combined project. Both old host projects and test projects were replaced. The original shared API/Data/Domain/SQL source remains unchanged, so the full build limitation is preserved and disclosed. No production auth, database behavior, search/PATCH, schedules or new Copilot GUI session is claimed.

Delivery checks verify current links/project paths, comments, skills, source hashes, duplicate cases and whitespace. Historical issue/Copilot records remain available. [Test selection](test-review.md), [review record](review-record.md).

## Main-code documentation follow-up, 2026-10-06

After the human clarified that collaborators' original models and interfaces must be kept, the latest fetched master commit was checked: `7c553673edb2713e2d24eadca101eba5fe652cb0`. It is already an ancestor of Artiom. All 26 tracked original API/Data/Domain/Database files are present and unchanged relative to that commit. The follow-up changes documentation only; production and test files are unchanged.

The model/service/repository inventories now link the preserved source. The [trip-point/transport/route design](../../trip-points-and-routes.md) labels scheduled routes, trip occurrences and timings as planned, alongside the current catalogue API. Local documentation links, solution paths, JSON and diff whitespace checks passed. Structured results are under `mainCodeDocumentation` in verification.json. No new test execution is claimed for this documentation-only change; the prior 19-case/eight-live-check results above remain the runtime evidence.
