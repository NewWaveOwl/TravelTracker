# Attraction management and name search implementation plan

Reviewed handoff for [feature #15](https://github.com/NewWaveOwl/TravelTracker/issues/15) from planning [#9](https://github.com/NewWaveOwl/TravelTracker/issues/9) to implementation [#10](https://github.com/NewWaveOwl/TravelTracker/issues/10) and validation [#11](https://github.com/NewWaveOwl/TravelTracker/issues/11). The human confirmed the [specification](feature-spec.md) through the [completed interview](priming-interview.md). A revised plan was generated in fresh Copilot session `92e3a511-b590-4152-bb4b-afef271f5f07`, distinct from revised issue-draft session `e7f8b346-b11a-4909-9c48-eb4366616e6d`. Original responses and adjustments are in [Copilot review](copilot-review.md).

## 1 Establish the implementation baseline

Start a fresh, scoped #10 Copilot session in VS Code using C#, ASP.NET Core and .NET 10. Attach the specification, this plan, triage and completed interview; read `AGENTS.md`, repository instructions and both skills. Refresh `origin/master`, inspect local changes and the team's issue/PR state. Follow `CONTRIBUTING.md` for implementation branch work; the existing `Artiom` branch holds planning and skills.

Recheck baseline restore/build errors and test discovery. The current shared API has no routes, service/repository implementations, description/rating fields or test projects. Existing Id64/missing-type failures belong to separate shared-solution work. Reuse any compatible standalone Attractions baseline supplied by the team; otherwise add the scoped service below. The human's explicit standalone-service requirement overrides the generic shared-scaffold default for this feature only.

## 2 Create the independent service and read foundation

Create `src/TravelTracker.Attractions.Api/TravelTracker.Attractions.Api.csproj`, targeting net10.0 with nullable enabled and no references to shared API/Data/Domain projects. Use port 5104. These are **proposed future files**, not existing implementations:

| File under the API project | Responsibility |
| --- | --- |
| `Program.cs` | Configuration, DI, middleware and route-map calls |
| `Properties/launchSettings.json` | Development port 5104 |
| `Models/AttractionRecord.cs` | Immutable service-owned record with all specified wire fields represented |
| `Contracts/AttractionResponse.cs` | GUID/camelCase response, nullable description/rating and lat/lon |
| `Contracts/AttractionResponseMapper.cs` | Explicit representation mapping |
| `Contracts/CreateAttractionRequest.cs` | Writable create fields only; detect missing required values |
| `Contracts/PatchAttractionRequest.cs` | Presence-aware writable update fields |
| `Contracts/PatchAttractionRequestConverter.cs` | Detect omitted/null/present values and reject unknown keys |
| `Data/AttractionSeedData.cs` | Fixed example records and GUIDs |
| `Data/InMemoryAttractionStore.cs` | Thread-safe process-lifetime state and atomic operations |
| `Data/IAttractionRepository.cs` | Feature-needed reads/create/atomic-update contract |
| `Data/InMemoryAttractionRepository.cs` | Store-backed operations and filtering/order |
| `Validation/AttractionValidator.cs` | Complete record field rules owned by the service |
| `Services/IAttractionIdGenerator.cs` and `Services/AttractionIdGenerator.cs` | Testable server GUID generation, separate files |

Use a singleton thread-safe store seeded once per process; scoped repositories and use cases share it across requests. A singleton must not capture scoped services. Store immutable records and return snapshots. Protect compound read/validate/replace operations, not merely individual dictionary calls. The store and its contents reset only when a new service process starts.

Create one endpoint/use-case implementation per file:

| Endpoint file | Use case file | HTTP test file |
| --- | --- | --- |
| `Endpoints/HealthEndpoint.cs` | Health response requires no repository/dependency | `HealthHttpTests.cs` |
| `Endpoints/ListAttractionsEndpoint.cs` | `Services/GetAttractions.cs`; add `Services/SearchAttractions.cs` for name query | `ListAttractionsHttpTests.cs` and `SearchAttractionsHttpTests.cs` |
| `Endpoints/GetAttractionEndpoint.cs` | `Services/GetAttractionById.cs` | `GetAttractionHttpTests.cs` |
| `Endpoints/CreateAttractionEndpoint.cs` | `Services/CreateAttraction.cs` | `CreateAttractionHttpTests.cs` |
| `Endpoints/PatchAttractionEndpoint.cs` | `Services/PatchAttraction.cs` | `PatchAttractionHttpTests.cs` |

List and name search share **one** GET route; do not register competing handlers for the same path. Keep endpoints thin and Program as composition. Explicitly parse detail/update IDs so malformed GUIDs are `400`; a valid but unknown ID is `404`. A `{id:guid}` constraint alone would change malformed-ID behavior to `404`.

Add the project to `TravelTracker.slnx`. Establish health/list/details first with all other services stopped. Map the complete response fields including `description` and `rating`; do not mutate shared domain types for this independent contract.

## 3 Establish focused tests and review the read foundation

Coordinate source/test work and evidence with #11. Reuse existing test projects if suitable, or add `tests/TravelTracker.Attractions.UnitTests` and `tests/TravelTracker.Attractions.Api.IntegrationTests`, targeting net10.0 and referencing only the independent API. Verify current xUnit/test SDK and `Microsoft.AspNetCore.Mvc.Testing` compatibility before selecting versions. Add projects to the solution and remove example tests.

Use a separate API `Program.TestHost.cs` exposing `public partial class Program` if required by `WebApplicationFactory<Program>`. Isolate the singleton store per test host: it must be shared by requests within that host, never by unrelated tests. Fix seed values and replace the GUID generator with deterministic test values for create assertions.

Prove independent health, ordered seed list and detail status/representation with HTTP tests. Add meaningful service/repository tests, not tests mirroring implementation statements. Validate focused projects before **reviewed commit 1: standalone read foundation and passing tests**. If reused baseline already covers these cases, omit redundant scaffolding.

## 4 Implement creation with reviewed tests

Write a failing create behavior check, then implement it in the focused endpoint/service/repository files. Create DTOs exclude server-owned ID; reject unknown properties/client ID. Detect missing name/coordinates/duration rather than defaulting missing numeric input to zero. Trim and validate name, nullable manual rating 0-5, coordinates and nonnegative integer duration. Omitted description/rating become null; omitted address becomes an empty string per specification.

Generate a GUID in the create use case, validate before saving, and atomically add the saved record to the shared process store. Return `201`, saved representation and a detail Location. Test the exact returned Location with a subsequent GET, plus list visibility across requests. Test rating boundaries 0/5, null/omitted optionals, invalid names/rating/coordinates/duration/types/JSON and client IDs. Failed create must not change record count or contents.

Use `CreateAttractionTests.cs` for unit behavior and `CreateAttractionHttpTests.cs` for HTTP behavior; keep fixtures/fakes in their own files. Run focused checks before **reviewed commit 2: creation and passing tests**.

## 5 Implement partial updates without data loss

Use a presence-aware PATCH DTO/converter. A nullable property alone cannot distinguish missing from explicit null; numeric zero must not mean omitted. Reject empty objects, unknown fields and attempts to update the ID. Omitting any field preserves it; null clears only optional description/rating. The human's PATCH route uses a partial JSON object rather than JSON Patch operations.

Have `PatchAttraction` provide the business validation/update operation to an atomic repository/store boundary. Under one synchronized operation, read the current immutable record, apply specified fields, validate the complete candidate and replace only on success. Keep validation rules in service/validator code, not hard-coded persistence decisions. Do not read a stale snapshot and later overwrite a concurrently changed omitted field. If using version compare-and-swap instead, handle conflicts deliberately without returning a fabricated success; the wire representation need not expose internal versions.

Test `200` saved result, unchanged GUID/omitted fields, explicit null clearing, zero rating/zero coordinates, unknown valid GUID `404`, malformed GUID `400`, blank names/out-of-range ratings and a mixed valid-plus-invalid patch that leaves the entire record unchanged. Add a deterministic concurrency test with controlled coordination, without sleeps, for patches to different fields retaining both changes. Use `PatchAttractionTests.cs` and `PatchAttractionHttpTests.cs` separately from create/search tests.

Run focused checks before **reviewed commit 3: atomic partial update and passing tests**.

## 6 Implement name search over the current catalogue

The existing list endpoint binds at most one `name` value and calls the search use case when present. Reject duplicates, let the framework decode once, trim the query and use ordinal case-insensitive literal name substring matching. Empty input returns all, no match returns `200 []`. Filter before materializing, and order with `StringComparer.OrdinalIgnoreCase` then consistent GUID comparison. No old 100-character query bound is included.

Write unit and HTTP checks covering mixed case/spaces, empty/missing/no-match queries, equal/case-equal names with fixed GUIDs, once-decoded input and literal `%/_/*` or regex-looking names. Include positive literal-symbol fixtures. Prove search sees newly created records and renamed records, and that searching itself leaves state unchanged. Keep search implementation/test files separate from create/update.

Run focused checks before **reviewed commit 4: name search and passing tests**. Update `README.md` and `docs/api.md` to state the actually implemented routes, fields and process-only lifetime; do not claim planned other services are implemented.

## 7 Save validation evidence and complete the implementation review

Once these proposed projects exist, run from the repository root, checking each result before the next dependent command:

```powershell
dotnet restore src/TravelTracker.Attractions.Api/TravelTracker.Attractions.Api.csproj
dotnet build src/TravelTracker.Attractions.Api/TravelTracker.Attractions.Api.csproj --configuration Release --no-restore
dotnet restore tests/TravelTracker.Attractions.UnitTests/TravelTracker.Attractions.UnitTests.csproj
dotnet test tests/TravelTracker.Attractions.UnitTests/TravelTracker.Attractions.UnitTests.csproj --configuration Release --no-restore --logger "trx;LogFileName=attractions-unit.trx"
dotnet restore tests/TravelTracker.Attractions.Api.IntegrationTests/TravelTracker.Attractions.Api.IntegrationTests.csproj
dotnet test tests/TravelTracker.Attractions.Api.IntegrationTests/TravelTracker.Attractions.Api.IntegrationTests.csproj --configuration Release --no-restore --logger "trx;LogFileName=attractions-http.trx"
```

Save commit, SDK/resolved package versions, commands, exit codes and discovered/pass/fail/skip counts in #11's evidence. Stop and fix targeted failures; an exit zero with no executed tests is not a passing suite. Generated TRX files remain ignored and can be attached as evidence.

Start the service independently:

```powershell
dotnet run --project src/TravelTracker.Attractions.Api/TravelTracker.Attractions.Api.csproj --no-launch-profile -- --urls http://localhost:5104
```

In a second terminal verify health, seed list, POST, GET at returned Location, PATCH and search. Stop/restart the actual process and verify original seeds are restored and created/updated data is discarded. Use a separate test host reset to prove the same lifecycle automatically; record process restart smoke evidence as well.

Then run solution restore and Release build. Run `dotnet test TravelTracker.slnx --configuration Release --no-build --no-restore` only if that solution build succeeds. Record unrelated Id64/missing-type errors separately; coordinate a separate fix or explicit justified deferral during #11 review. Service-only success is not a successful full-solution build.

Review every specification acceptance case, `git diff --check`, DTO/presence semantics, concurrency/lifetimes, one responsibility per file and `/** ... @param ... @return ... */` comments. #11 records fixed/deferred findings and opens the implementation PR; #12 receives the reflection. No DELETE, database, external ratings, private/auth redesign, events, other services or unrelated source repair is added. #9's completed planning does not claim any future tests or API code have already been implemented.
