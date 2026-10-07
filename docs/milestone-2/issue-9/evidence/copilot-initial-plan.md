I’ll treat the reviewed issue specification as authoritative, distinguish existing files from proposed additions, and keep this to a plan with no edits or execution. I’m gathering the repository guidance, issue materials, and current solution/service contracts first.

# Attraction-name search implementation plan

## Scope and prerequisites

The reviewed `docs/milestone-2/issue-9/feature-spec.md` governs behavior. The current checkout has shared Attraction contracts, but no independent Attractions API, attraction endpoints, or test projects. The existing `Attraction` model uses GUID IDs and `Latitude`/`Longitude`; the existing repository and service interfaces are contracts, not implementations. Preserve them, and do **not** make the new service reference the shared API, Data, or Domain projects.

The M1 independent-service requirement is the narrow exception to the generic shared-scaffold guidance: create only an independently runnable Attractions API, leaving the other five services and their models, data, and contexts untouched. Before implementation, recheck whether a teammate has supplied a compliant baseline and reuse it if so. Confirm the current branch and team state under `CONTRIBUTING.md`; keep Attractions ownership with Artiom and implementation/validation responsibilities with #10/#11. The priming interview’s feature, standalone-baseline, and tooling choices are **defaults while human preferences remain unanswered**, not human approval.

The triage records unrelated baseline concerns in `Id64.cs` and references to missing `Trip`, `TripLeg`, `UserSettings`, and `TripMapPoint` types. Do not repair these as part of search. In #11, resolve them outside the search scope or explicitly defer them with justification; service-only evidence must not be presented as a passing full solution.

## Sequential steps and reviewed boundaries

1. **Establish the runnable M1 baseline first.** If no compliant teammate baseline exists, add only the independent service, deterministic public seed, list/details, and health routes. Review and prove this baseline before beginning search. Keep the existing shared scaffold intact. Document the service’s run command and implemented routes in `README.md` and `docs/api.md`.

   **Proposed files—not present in the checkout:**
   - `src/TravelTracker.Attractions.Api/TravelTracker.Attractions.Api.csproj` — .NET 10, nullable enabled, with no project references to the shared services.
   - `src/TravelTracker.Attractions.Api/Program.cs` — startup, DI, and route composition only.
   - `src/TravelTracker.Attractions.Api/Properties/launchSettings.json` — local port 5104.
   - `src/TravelTracker.Attractions.Api/Endpoints/AttractionEndpoints.cs` and `Endpoints/HealthEndpoint.cs` — separate route responsibilities.
   - `src/TravelTracker.Attractions.Api/Models/AttractionRecord.cs` — independent service model.
   - `src/TravelTracker.Attractions.Api/Contracts/AttractionResponse.cs` and `Contracts/AttractionResponseMapper.cs` — response type and explicit mapping.
   - `src/TravelTracker.Attractions.Api/Data/AttractionSeedData.cs`, `Data/IAttractionRepository.cs`, and `Data/InMemoryAttractionRepository.cs` — only public, fixed seed data and its in-memory read operations.
   - `src/TravelTracker.Attractions.Api/Services/GetAttractions.cs` and `Services/GetAttractionById.cs` — separate read use cases.
   - `TravelTracker.slnx` — add the independent service.
   - `README.md` and `docs/api.md` — document only the new service and its implemented contract.

   Seed the specified Montreal Museum, Science Centre, and Old Port records with fixed, deterministic GUIDs; do not generate random IDs. Map the response explicitly to exactly `attractionId`, `name`, `address`, `lat`, `lon`, and `estimatedVisitMinutes`. Keep health at `GET /health` with `{"status":"ok","service":"Attractions"}`. For details, preserve malformed GUID `400` versus valid-but-unknown GUID `404`; avoid a route constraint that would turn malformed IDs into `404`.

   **Commit boundary 1:** baseline only—seed, list, detail, health, and service registration. Review and smoke-check it before search work; no search behavior in this commit.

2. **Add query binding and validation at the HTTP boundary.** Extend `GET /api/v1/attractions` to accept at most one `name` parameter. Use framework query decoding once; explicitly detect repeated values rather than relying on scalar binding. Trim before validation; absent, empty, and whitespace-only values mean “no filter.” Reject a trimmed query longer than 100 UTF-16 code units with `400 application/problem+json` and a validation error for `name`; exactly 100 is valid. Do not decode again.

   **Proposed files—not present:** `src/TravelTracker.Attractions.Api/Validation/AttractionNameQueryValidator.cs`. Update the proposed `Endpoints/AttractionEndpoints.cs` to bind, check duplicate values, validate, and return the validation problem response. Keep filtering out of the endpoint.

   **Commit boundary 2:** binding and validation only, with no change yet to matching behavior.

3. **Implement the search use case and repository filtering.** Add `Services/SearchAttractions.cs` as the search use case and extend the independent repository contract/implementation with the corresponding read operation. Apply a literal name substring match using `StringComparison.OrdinalIgnoreCase`; do not search addresses or interpret wildcard or regex syntax. Sort all successful results by name using `StringComparer.OrdinalIgnoreCase`, then GUID as a stable tie-breaker. An empty query returns the full catalogue in that same order; no matches return `200 []`. Keep search read-only and limited to the public seed. Do not add persistence, cross-service calls, private-catalogue access, or `Id64` migration.

   **Commit boundary 3:** search use case and repository filtering/order, with the endpoint only passing validated input. Update the scoped API documentation to reflect the query contract. Use the required `/** ... */` comments with exact `@param` and meaningful `@return` documentation for new or substantially changed members.

4. **Add automated tests under #11.** There are no test projects in the current checkout. Add focused .NET 10 xUnit projects, then include them in `TravelTracker.slnx`:
   - **Proposed unit project:** `tests/TravelTracker.Attractions.UnitTests/TravelTracker.Attractions.UnitTests.csproj`; proposed files `AttractionNameQueryValidatorTests.cs` and `InMemoryAttractionRepositoryTests.cs`.
   - **Proposed HTTP project:** `tests/TravelTracker.Attractions.Api.IntegrationTests/TravelTracker.Attractions.Api.IntegrationTests.csproj`; proposed file `AttractionEndpointsTests.cs`, plus `Program.TestHost.cs` only if `WebApplicationFactory<Program>` needs a public test-host entry point.

   Unit tests should cover trimming/empty input, exact 100/101 UTF-16-unit boundaries (including a supplementary-character case), ordinal-ignore-case substring matching, literal `%`, `_`, `*` and regex-looking input, no matches, and deterministic name/GUID ordering. HTTP tests should verify once-decoded queries, duplicate `name` rejection, problem content type and `name` error, empty-query/full-list behavior, `200 []`, exact JSON field names and GUID representation, and that search does not mutate the subsequent unfiltered list. Also cover health, known and unknown GUID details, and malformed GUID `400`. Keep test IDs and records fixed.

   Before selecting versions, check that .NET 10 xUnit/test SDK packages and `Microsoft.AspNetCore.Mvc.Testing` are compatible with the target framework; do not guess versions. Inspect the resolved package graph and confirm restore succeeds.

   **#11 review boundary:** keep unit-test and HTTP-test evidence attributable to their respective projects, and report discovered/executed test counts. Unit tests do not establish the HTTP contract.

5. **Run project-targeted checks, then the full-solution checks.** From the repository root, the future service and test projects can be checked independently with:

   ```powershell
   dotnet restore src\TravelTracker.Attractions.Api\TravelTracker.Attractions.Api.csproj
   dotnet build src\TravelTracker.Attractions.Api\TravelTracker.Attractions.Api.csproj --configuration Release --no-restore

   dotnet restore tests\TravelTracker.Attractions.UnitTests\TravelTracker.Attractions.UnitTests.csproj
   dotnet list tests\TravelTracker.Attractions.UnitTests\TravelTracker.Attractions.UnitTests.csproj package --include-transitive
   dotnet test tests\TravelTracker.Attractions.UnitTests\TravelTracker.Attractions.UnitTests.csproj --configuration Release --no-restore

   dotnet restore tests\TravelTracker.Attractions.Api.IntegrationTests\TravelTracker.Attractions.Api.IntegrationTests.csproj
   dotnet list tests\TravelTracker.Attractions.Api.IntegrationTests\TravelTracker.Attractions.Api.IntegrationTests.csproj package --include-transitive
   dotnet test tests\TravelTracker.Attractions.Api.IntegrationTests\TravelTracker.Attractions.Api.IntegrationTests.csproj --configuration Release --no-restore
   ```

   For the baseline service smoke check, run it on port 5104 and check `/health` and `/api/v1/attractions`; the HTTP test suite should cover the remaining route cases:

   ```powershell
   dotnet run --project src\TravelTracker.Attractions.Api\TravelTracker.Attractions.Api.csproj --no-launch-profile -- --urls http://localhost:5104
   Invoke-RestMethod http://localhost:5104/health
   Invoke-RestMethod http://localhost:5104/api/v1/attractions
   ```

   After adding the service and test projects to the solution, separately run the repository’s full-solution commands:

   ```powershell
   dotnet restore TravelTracker.slnx
   dotnet build TravelTracker.slnx --configuration Release --no-restore
   dotnet test TravelTracker.slnx --configuration Release --no-build --no-restore
   ```

   In #11, distinguish service-only evidence from full-solution status. Do not treat a service-only build/test as a passing solution, or a zero test exit with no discovered tests as passing. Resolve the unrelated known blockers outside search or record an explicit, justified deferral in the #11 review.

## Main risks to track

The independent service intentionally duplicates only the small model/seed/read surface needed to honor M1 ownership; sharing the existing model or repository would violate that boundary. Scalar query binding may hide duplicate values unless explicitly checked. A second URL decode or culture-sensitive matching would change the specified behavior. The fixed public seed does not implement private/shared catalogue authorization, and this feature must not imply that it does. Full-solution failures remain a separate review concern rather than a reason to broaden search work.