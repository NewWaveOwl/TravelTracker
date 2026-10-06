# Attraction name search implementation plan

This is the reviewed handoff from [#9](https://github.com/NewWaveOwl/TravelTracker/issues/9) to implementation [#10](https://github.com/NewWaveOwl/TravelTracker/issues/10) and validation [#11](https://github.com/NewWaveOwl/TravelTracker/issues/11). GitHub Copilot generated the [initial plan](evidence/copilot-initial-plan.md) in fresh session `df53555f-cd5b-4f36-b2dd-83ab434ad806`, distinct from its issue/interview session. Codex then adjusted the file boundaries, validation sequence and build gating. The [specification](feature-spec.md) governs behavior; the [interview](priming-interview.md) distinguishes planning defaults from human answers.

## 1 Recheck the baseline and team work

Read `AGENTS.md`, `.github/copilot-instructions.md`, both repository skills, this specification and [triage](triage-notes.md). Repeat the duplicate/open-PR search and inspect current code before creating scaffolding. Start the #10 implementation session fresh with these files; do not rely on the planning conversation.

Refresh `origin/master`, inspect local changes and use the team's implementation branch workflow in `CONTRIBUTING.md`. `Artiom` is the user's requested planning/skills branch. Do not merge or overwrite it as part of implementation. Record baseline restore/build results and the existing absence of tests. Recheck the known Id64 error and missing types rather than assuming the old result covers every compiler failure.

Resolve the limited architecture decision recorded here before changing source: M1 requires an independently runnable Attractions service. Reuse a compliant teammate baseline if available. Otherwise add only the standalone Attractions prerequisite below. Leave all five other service responsibilities and the existing shared contracts intact. This assignment requirement explains the limited exception to the skill's generic instruction to preserve the shared scaffold.

## 2 Establish the Attractions prerequisite

Create a .NET 10 web project at `src/TravelTracker.Attractions.Api`, with nullable enabled and no references to the shared API/Data/Domain projects. Bind its development launch profile to port 5104. It owns its model and public seeded catalogue and starts with every other service stopped.

The following files are **proposed additions**, not existing implementations. Keep one use case or primary responsibility per file.

| Proposed file under `src/TravelTracker.Attractions.Api/` | Responsibility |
| --- | --- |
| `TravelTracker.Attractions.Api.csproj` | Standalone net10.0 web project |
| `Program.cs` | Startup, DI and calls to route mapping methods |
| `Properties/launchSettings.json` | Development port 5104 |
| `Models/AttractionRecord.cs` | Service-owned GUID attraction model |
| `Contracts/AttractionResponse.cs` | Exact M1 wire representation |
| `Contracts/AttractionResponseMapper.cs` | Explicit coordinate/field mapping |
| `Data/AttractionSeedData.cs` | Fixed public records and GUIDs |
| `Data/IAttractionRepository.cs` | Read operations needed by this service |
| `Data/InMemoryAttractionRepository.cs` | Seed-backed read implementation |
| `Services/GetAttractions.cs` | Full catalogue use case |
| `Services/GetAttractionById.cs` | Detail use case |
| `Endpoints/ListAttractionsEndpoint.cs` | Thin list HTTP boundary |
| `Endpoints/GetAttractionEndpoint.cs` | Thin GUID detail HTTP boundary |
| `Endpoints/HealthEndpoint.cs` | Independent health route |

Use immutable seed records, or return independent read-only snapshots, so a read caller cannot mutate the process-wide seed. Use scoped repository/use-case registrations; if a shared seed object is singleton, it must be immutable and must not capture scoped services. No DbContext or broker is necessary.

Expose `/api/v1/attractions`, `/api/v1/attractions/{id}` and `/health`. Explicitly parse detail IDs so malformed GUIDs give `400` problem responses and unknown valid GUIDs give `404`. A `{id:guid}` route constraint by itself would give `404` for malformed IDs and violate the chosen contract. Map coordinates to JSON `lat`/`lon`; keep all other specified fields and GUID strings.

Add the project to `TravelTracker.slnx` and update `README.md`/`docs/api.md` to distinguish implemented Attractions routes from planned services. Build this project and smoke-check list/detail/health before the next phase. This is a minimum read baseline, not completion of M1 CRUD.

**Reviewed commit 1:** independently runnable public Attractions read baseline. Reuse and skip these additions if the team has already delivered equivalent behavior.

## 3 Define failing search checks

Coordinate test ownership with #11. Add net10.0 xUnit projects only if equivalent projects do not already exist:

- `tests/TravelTracker.Attractions.UnitTests`, referencing only the independent Attractions project.
- `tests/TravelTracker.Attractions.Api.IntegrationTests`, referencing that project plus a compatible `Microsoft.AspNetCore.Mvc.Testing` package.

Inspect current supported package versions/runner compatibility before restoring. Add both projects to the solution and remove template sample tests. If needed, expose the API's top-level `Program` using a separate `src/TravelTracker.Attractions.Api/Program.TestHost.cs` with `public partial class Program`; it belongs in the API assembly.

Create `SearchAttractionsTests.cs` for service/validation cases and `SearchAttractionsHttpTests.cs` for query/status/JSON behavior. Give fixtures and fakes their own files. Baseline detail and health tests belong in `GetAttractionHttpTests.cs` and `HealthHttpTests.cs` rather than a file combining all endpoints. Verify that a filtered search assertion fails against the unfiltered prerequisite for the expected reason, then implement it. Do not commit a broken test suite as a completed checkpoint.

## 4 Implement and validate the name filter

Add a focused `Services/SearchAttractions.cs` use case and, if useful, a separate `Validation/AttractionNameQueryValidator.cs`. The service owns normalization and length rules so non-HTTP callers cannot bypass them. The list endpoint binds query values, rejects repeated `name` keys, calls the use case, and maps invalid input to `ValidationProblem` with the `name` key.

Extend the independent repository only with the read query needed by the search. It performs literal `OrdinalIgnoreCase` substring matching and orders by `Name` with `StringComparer.OrdinalIgnoreCase`, then `AttractionId` using consistent GUID comparison. Materialize after filtering/ordering and project through the response mapper. Empty input uses the same ordering. Do not expose a mutable seed list, search addresses, double-decode input, interpret regex/wildcards, or add database/external-service access.

Keep `Program.cs` for composition. Document new or substantially changed members with `/** ... */`, exact `@param` names and meaningful `@return` descriptions. Explain non-obvious matching/length decisions; do not add XML comments or blanket-comment simple statements.

Run unit and HTTP checks for the complete specification matrix. Include test-only fixed records with equal/case-equal names and distinct GUIDs to prove tie ordering. Include a record containing a literal metacharacter so wildcard tests prove literal matching positively as well as showing no expansion. Include a supplementary Unicode character in length-boundary tests to distinguish UTF-16 code units from Unicode characters. Prove that a search leaves the subsequent list unchanged and that invalid input does not reach persistence.

**Reviewed commit 2:** complete name-search behavior and passing focused tests, including binding, validation, matching and ordering. Combine these parts into one working change; do not commit an endpoint that accepts `name` while silently ignoring it as a completed search checkpoint.

## 5 Save focused validation evidence

Once the proposed projects exist, run from the repository root:

```powershell
dotnet restore src/TravelTracker.Attractions.Api/TravelTracker.Attractions.Api.csproj
dotnet build src/TravelTracker.Attractions.Api/TravelTracker.Attractions.Api.csproj --configuration Release --no-restore
dotnet restore tests/TravelTracker.Attractions.UnitTests/TravelTracker.Attractions.UnitTests.csproj
dotnet test tests/TravelTracker.Attractions.UnitTests/TravelTracker.Attractions.UnitTests.csproj --configuration Release --no-restore --logger "trx;LogFileName=attractions-unit.trx"
dotnet restore tests/TravelTracker.Attractions.Api.IntegrationTests/TravelTracker.Attractions.Api.IntegrationTests.csproj
dotnet test tests/TravelTracker.Attractions.Api.IntegrationTests/TravelTracker.Attractions.Api.IntegrationTests.csproj --configuration Release --no-restore --logger "trx;LogFileName=attractions-http.trx"
```

Inspect each exit code and discovered/passed/failed/skipped test count. Stop and fix failed focused checks; do not describe later commands as proof if an earlier prerequisite failed. #11 saves a readable summary with commit, SDK/package versions, commands, counts and relevant failure output; generated TRX files are already ignored and may be attached to the PR as evidence.

Start the service in one terminal:

```powershell
dotnet run --project src/TravelTracker.Attractions.Api/TravelTracker.Attractions.Api.csproj --no-launch-profile -- --urls http://localhost:5104
```

From a second terminal, with all other TravelTracker services stopped, verify:

```powershell
Invoke-RestMethod http://localhost:5104/health
Invoke-RestMethod 'http://localhost:5104/api/v1/attractions?name=mUsEuM'
```

Health must return the specified body; the filtered list must contain only Montreal Museum. HTTP integration tests cover invalid input, response shapes and the rest of the matrix.

## 6 Check the solution and complete the review handoff

Run `dotnet restore TravelTracker.slnx`, and continue only if restore succeeds. Run `dotnet build TravelTracker.slnx --configuration Release --no-restore`. Run `dotnet test TravelTracker.slnx --configuration Release --no-build --no-restore` only after that build succeeds; a failed build makes `--no-build` evidence potentially stale.

If unrelated shared projects still fail, save that output separately. Coordinate a separate repair or an explicit justified deferral in #11's review record. A focused Attractions pass cannot become a claim that the full solution passed. No discovered tests cannot become a passing suite.

Review the diff against every acceptance criterion, `git diff --check`, the one-feature/responsibility-per-file rule, comment style and service ownership. Update scoped API/run documentation to the final implementation. #11 opens the implementation PR, records fixed/deferred findings and links #10, the feature specification, this plan and test evidence. #12 receives the short reflection on useful Copilot output and the corrections that required judgment.

**Reviewed commit 3, if needed:** final documentation and fixes arising from validation/review. Source code, tests and evidence described here are future #10/#11 work; this planning task does not claim they already exist or pass.
