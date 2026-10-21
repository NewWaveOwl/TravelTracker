---
name: testing
description: Add, update, run or investigate TravelTracker .NET tests (unit, HTTP integration, persistence, contract, selected end-to-end). Establish a baseline, cover public behavior and failure paths, and report real evidence. Not for comment-only edits or unrelated feature work; use validation for the pre-commit gate.
---

# TravelTracker testing

## Baseline first

1. Look at the current diff, `TravelTracker.slnx`, the project files, the relevant `docs/` contracts and the existing tests. Decide what behavior needs proving before choosing a test level.
2. Run restore, build and test as they apply. Record pre-existing failures separately from regressions. Do not widen a test task into product fixes.
3. Confirm tests were actually discovered and run. Exit code 0 with no tests is not a pass. Recheck the repo rather than trusting an old baseline.

## Pick the level

| Level | Use for | Dependencies |
| --- | --- | --- |
| Unit | A business rule, value type, strategy or service decision | Small constructor-injected fakes; no database or network |
| HTTP integration | Routing, binding, validation, DI, auth, status, body, headers | `WebApplicationFactory<Program>` with controlled services |
| Persistence integration | EF queries, constraints, migrations, concurrency, atomic writes | Isolated database with the needed relational behavior |
| Contract | DTO and status compatibility between service owners | Assert the agreed public schema |
| End-to-end | A few critical user journeys | Local services with seeded data |

Use the lowest level that proves the behavior. A direct service call does not prove routing, authorization middleware or transactions.

## Course requirements versus demo conventions

The existing one-project/resource-file layout and former 30-case limit are historical user conventions, not lecture quality gates. The current milestone_3 suite already has 41 cases (verified 2026-10-21). This lecture-alignment revision retires the obsolete fixed cap: keep meaningful existing tests and add cases according to risk and acceptance criteria. Do not remove coverage or hide input matrices in loops to meet an old number. Preserve useful existing grouping, while allowing requested independent-service/provider suites their own organization. Reinspect the actual solution and runner before relying on old paths.

## Historical combined-demo layout (verify applicability)

- One xUnit project, `tests/TravelTracker.Api.Tests`, referencing `TravelTracker.Api`. Tests are grouped by resource: `HealthTests.cs`, `AttractionTests.cs`, `TransportModeTests.cs`, `RoutePointTests.cs`. Shared helpers: `Support/ApiFactory.cs` and `Support/HttpTestSupport.cs`. Reuse matching files where cohesive; use additional purpose-based files when new service or boundary coverage warrants them.
- Each test creates and disposes its own factory, client and in-memory stores. No shared mutable state, no ordering dependence.
- Reuse existing projects for the combined demo. Requested independent-service or provider integration work may need its own focused test project; if none exists and tests are requested, create .NET 10 xUnit under `tests/`, added to the solution, with only the references it needs. Add `Microsoft.AspNetCore.Mvc.Testing` for HTTP tests; check which package versions exist instead of guessing. If `Program` must be visible to `WebApplicationFactory`, use a dedicated `public partial class Program` file.
Responsibilities for the milestone:

- **Unit:** domain rules and service decisions (trip rules, name trimming, code validation). Fakes for repositories. No HTTP.
- **HTTP integration:** routing, binding, DataAnnotations, status codes, problem details, `Location` headers, JSON field names. Runs through `WebApplicationFactory`.
- **Relational persistence:** EF Core queries, constraints, migrations, atomic trip saves and concurrency tokens. The stored data and relational rules matter, not a particular provider. EF InMemory is acceptable while no provider-specific behavior is depended on. Once the provider is chosen (open decision 3 in the contracts) and the code relies on provider-specific behavior, move those tests to that provider.
- **Selected client end-to-end:** a few journeys (sign in, create attraction, plan a trip, view it) against local services with seeded data. Required only after the frontend exists (issues #32 and #39).

**Fake identities versus real token validation.** Most HTTP tests may use a test authentication scheme that supplies a chosen identity (subject, role, status). That proves authorization rules: ownership, banned-user read-only, admin without private trip access. It does not prove that the Api validates tokens. A fake-identity test must not be cited as evidence that tokens are validated. Real token validation tests are added once the identity provider is chosen (open decision 2).

## Project layout

- Current project: one xUnit project, `tests/TravelTracker.Api.Tests`, referencing `TravelTracker.Api`. Tests are grouped by resource: `HealthTests.cs`, `AttractionTests.cs`, `TransportModeTests.cs`, `RoutePointTests.cs`, plus `EntityIdGeneratorTests.cs`. Shared helpers: `Support/ApiFactory.cs` and `Support/HttpTestSupport.cs`. Put new tests in the matching file; do not create a new test file per feature (a `ServiceTests.cs` was previously rejected for this reason).
- Target layout for the six-service milestone (see `docs/milestone-3-contracts.md`): each service gets its own test project alongside its own Api, Data and Domain projects, with HTTP tests against that service's host. A cross-service contract test lives in its own project and checks only public DTO shapes and status codes. Creating those projects needs an explicit request and an approved plan.
- Each test creates and disposes its own factory, client and in-memory stores. No shared mutable state, no ordering dependence.
- No discovered-count cap. The current master count is 41 discovered and passing (counting every `[InlineData]` row). Each test covers exactly one concern; several asserts on that same concern belong in one test, not in several tests. Add tests where behavior needs proof, not to hit a number. Do not hide a matrix in a loop to lower the count; use `[Theory]` rows so each case is visible in the report.
- Only create a new test project if none exists and one is requested: .NET 10 xUnit under `tests/`, added to the solution, with only the references it needs. Add `Microsoft.AspNetCore.Mvc.Testing` for HTTP tests; check which package versions exist instead of guessing. If `Program` must be visible to `WebApplicationFactory`, use a dedicated `public partial class Program` file.
- Replace external services and configuration in the test host. No credentials, cloud services or production databases in routine tests.

## Writing tests

- Name by behavior and outcome: `CreateAttraction_InvalidCoordinates_DoesNotPersist`. Arrange minimal data, act once, assert the observable result and relevant side effects.
- `[Fact]` for one scenario, `[Theory]` for meaningful input variation. A regression test should fail against the old behavior when that can be shown safely.
- Be deterministic: fixed ids, times and seed data; inject `TimeProvider` where time matters; no live network, no sleeps; await everything.
- Choose small fakes, stubs or mocks according to the behavior being proved; use Arrange-Act-Assert. Verify a dependency call only when it is the behavior (invalid input never writes; an accepted operation commits once).
- Relational guarantees need a real relational provider. SQLite in memory covers many queries; provider-specific SQL, migrations, constraints and concurrency need the production-compatible one. EF's InMemory provider proves nothing about transactions.
- HTTP tests assert status, body schema and relevant headers, including creation `Location` and problem details. For auth, a test scheme may supply identities, but the authorization rules stay on.
- Use `/** ... */` with `@param` and `@return` on test helpers that need documentation (see `code`). Comments explain intent, not assertions.

## Cases worth covering

Take exact expectations from the current contract, and do not implement planned features just to have something to test.

- **Attractions / route points:** validation boundaries (required fields, coordinate ranges, rating, visit duration, supported types), unknown JSON properties rejected, not found, persisted values round-trip.
- **Attraction list and PATCH:** name filter case-insensitive substring; PATCH preserves omitted fields, clears only description or rating on explicit null, rejects attempts to set `attractionId`, and leaves the stored record unchanged when validation fails.
- **Transport:** supported codes (car, bus, train, pedestrian, bike, plane), unknown code rejected, nothing persisted by a rejected request.
- **Concurrency:** lost-update protection where updates exist, shown to fail without the fix.
- **Trips, once implemented:** one-way versus round-trip legs, connected ordered legs without gaps, arrival after departure, no overlapping legs, visits within trip dates, failed validation leaves the aggregate unchanged.
- **Authorization, once implemented:** unauthenticated calls, ownership, another user's private record appearing absent, banned users read-only, deactivated users denied, administrators not seeing other travellers' private trips. Verify invalid issuer/audience/signature/expired tokens through the real bearer handler where token validation is in scope; fake identities alone do not prove JWT validation. Include authenticated users missing required permissions (403), and prove rejected input does not persist.
- **Failure:** absent resources, dependency timeout, cancellation, stale edits, rollback with no partial writes.
- **Events, when implemented:** duplicate delivery gives one effect, retries stop at the limit, permanent failures are not retried, poison messages are dead-lettered with reasons, out-of-order delivery follows the contract, outbox records survive a publish failure, and replay after publish-before-marking does not duplicate business effects. Exercise concurrency of duplicate deliveries where atomic deduplication is claimed.
- **Contracts:** field names, identifier serialization (`Guid` versus `Id64` as agreed), status codes, backward compatibility.

## Run and report

```powershell
dotnet test TravelTracker.Tests.slnx --configuration Release
```

Test hosts start automatically. When the full solution matters:

```powershell
dotnet restore TravelTracker.slnx
dotnet build TravelTracker.slnx --configuration Release --no-restore
dotnet test TravelTracker.slnx --configuration Release --no-build --no-restore
```

1. Run the changed tests first, then the wider suite, and rerun after any further edit or failure fix.
2. Collect TRX or `XPlat Code Coverage` only if the runner supports it; keep results out of commits. Coverage points at gaps; it is not a quality score and has no invented target.
3. CI work follows restore, build, test, publishes results even on failure, and gates application artifacts and deployment on passing agreed checks. Publish supported TRX and coverage reports, and enforce thresholds with a failing check rather than merely printing a percentage. Use the appropriate .NET SDK and actual repository provider; Azure Pipelines is a course target when requested. Match this repo's actual setup (`.github/workflows/ci.yml`) and do not create a pipeline or deploy unasked.
4. Report the commands, discovered and executed counts, pass/fail/skip, baseline versus new failures, behavior covered and boundaries left unverified. If the build fails, say tests were blocked.

Lecture alignment and historical provenance: [skills-sources.md](../../../docs/skills-sources.md).
