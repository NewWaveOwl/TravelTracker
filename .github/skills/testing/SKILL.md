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

## Project layout

- Each service and the frontend own a test project under `tests/`, referencing only the project it tests (plus shared contracts). Today that is `tests/TravelTracker.Api.Tests`. Feature tests live with the behavior they prove, in the file matching the resource or capability (for example `AttractionTests.cs`); extend the matching file instead of creating a new file per feature. `Support/` holds shared helpers such as `ApiFactory.cs` and `HttpTestSupport.cs`.
- Each test creates and disposes its own factory, client and stores. No shared mutable state, no ordering dependence.
- There is no fixed cap on test count. Coverage must be meaningful: every public behavior, validation boundary and failure path in the change gets a test. Never delete useful coverage to reduce a count, and never pad with duplicates. Do not hide a matrix in a loop; each `[InlineData]` row counts as a discovered case.
- Services are tested in isolation. A service test host replaces other services with fakes or stubbed `HttpClient` handlers, and the frontend is tested against the services' HTTP contract only, never their internals or databases.
- Only create a new test project when a new service or client needs one: .NET 10 xUnit under `tests/`, added to the solution, with only the references it needs. Add `Microsoft.AspNetCore.Mvc.Testing` for HTTP tests; check which package versions exist instead of guessing. If `Program` must be visible to `WebApplicationFactory`, use a dedicated `public partial class Program` file.
- Replace external services and configuration in the test host. No credentials, cloud services or production databases in routine tests.

## Responsibilities by level

- **Unit:** business rules and decisions with constructor-injected fakes.
- **HTTP:** routing, binding, validation, status, body and headers through `WebApplicationFactory<Program>`.
- **Relational persistence:** queries, constraints, migrations and atomic writes against a real relational provider.
- **Bearer validation:** see the next section.
- **Client end-to-end:** a few selected frontend journeys over HTTP against locally running services with seeded data. Not a substitute for lower levels.

## Authorization versus bearer validation

These prove different things; label each test with which one it is.

- **Fake-identity authorization tests** use a test authentication scheme that supplies a claims principal. They prove authorization rules (ownership, roles, banned or deactivated users, 401 versus 403 versus absent). The authorization policies stay on; only authentication is replaced. They say nothing about token validity.
- **Real bearer-validation tests** send actual signed JWTs through the production `JwtBearer` configuration (test signing key or local issuer). They must prove rejection of a bad signature, wrong issuer, wrong audience and expired token, and acceptance of a valid token. A fake scheme can never satisfy this.

## Writing tests

- Name by behavior and outcome: `CreateAttraction_InvalidCoordinates_DoesNotPersist`. Arrange minimal data, act once, assert the observable result and relevant side effects.
- `[Fact]` for one scenario, `[Theory]` for meaningful input variation. A regression test should fail against the old behavior when that can be shown safely.
- Be deterministic: fixed ids, times and seed data; inject `TimeProvider` where time matters; no live network, no sleeps; await everything.
- Fakes over mocks. Verify a dependency call only when it is the behavior (invalid input never writes; an accepted operation commits once).
- Relational guarantees need a real relational provider. SQLite in memory covers many queries; provider-specific SQL, migrations, constraints and concurrency need the production-compatible one. EF's InMemory provider proves nothing about transactions.
- HTTP tests assert status, body schema and relevant headers, including creation `Location` and problem details. For auth, a test scheme may supply identities, but the authorization rules stay on.
- Use `/** ... */` with `@param` and `@return` on test helpers that need documentation (see `code`). Comments explain intent, not assertions.

## Cases worth covering

Take exact expectations from the current contract, and do not implement planned features just to have something to test.

- **Attractions / route points:** validation boundaries (required fields, coordinate ranges, rating, visit duration, supported types), unknown JSON properties rejected, not found, persisted values round-trip.
- **Transport:** supported codes (car, bus, train, pedestrian, bike, plane), unknown code rejected, nothing persisted by a rejected request.
- **Concurrency:** lost-update protection where updates exist, shown to fail without the fix.
- **Trips, once implemented:** one-way versus round-trip legs, connected ordered legs without gaps, arrival after departure, no overlapping legs, visits within trip dates, failed validation leaves the aggregate unchanged.
- **Authorization (fake identities):** unauthenticated calls, ownership, another user's private record appearing absent, banned users read-only, deactivated users denied, administrators not seeing other travellers' private trips.
- **Failure:** absent resources, dependency timeout, cancellation, stale edits, rollback with no partial writes.
- **Events, when implemented:** duplicate delivery gives one effect, retries stop at the limit, permanent failures are not retried, poison messages are dead-lettered.
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

1. Before any behavioral change, record the baseline (discovered, passed, failed, skipped) so later counts can be compared. Run the changed tests first, then the wider suite, and rerun after any further edit or failure fix.
2. Collect TRX or `XPlat Code Coverage` only if the runner supports it; keep results out of commits. Coverage points at gaps; it is not a quality score and has no invented target.
3. CI work follows restore, build, test, publishes results even on failure, and gates deployment on passing tests. Match this repo's actual setup (`.github/workflows/ci.yml`) and do not create a pipeline or deploy unasked.
4. Report the commands, discovered and executed counts, pass/fail/skip, baseline versus new failures, behavior covered and boundaries left unverified. If the build fails, say tests were blocked.

Lecture and video provenance: [skills-sources.md](https://github.com/NewWaveOwl/TravelTracker/blob/milestone_2_AI_and_development_resource/AI_evdence/atiom_evdence/skills-sources.md).
