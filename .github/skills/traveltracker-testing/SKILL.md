---
name: traveltracker-testing
description: Add, update, run, or investigate TravelTracker .NET unit, HTTP integration, persistence, contract, and selected end-to-end tests. Establish a baseline, cover public behavior and failure paths, and report test evidence; do not activate for comment-only edits or unrelated feature implementation.
---

# TravelTracker test authoring and verification

## Establish a truthful baseline

1. Inspect the current diff, `TravelTracker.slnx`, project files, relevant `docs/` contracts, test projects, and existing runner/package choices. Identify the behavior to prove before choosing a test level.
2. Run the existing restore/build/test checks applicable to the request. Record pre-existing errors separately from regressions. Do not broaden a test task into unrelated product fixes.
3. Confirm that tests were discovered and executed. An exit code of zero with no test projects or no discovered tests is not a passing test suite. Recheck repository state rather than assuming a previously documented baseline still applies.

## Choose the boundary to test

| Level | Use in TravelTracker | Dependencies |
| --- | --- | --- |
| Unit | A business rule, value type, strategy, or service decision | Constructor-injected small fakes; no database/network |
| HTTP integration | Routing, binding, validation, DI, auth, status/body/headers | `WebApplicationFactory<Program>` with controlled test services |
| Persistence integration | EF queries, constraints, migrations, concurrency, atomic writes | An isolated database with the required relational/provider behavior |
| Contract | DTO/status compatibility between service owners | Assert the agreed public schema and semantics |
| End-to-end | A small number of critical user journeys | Controlled locally runnable services and seeded test data |

Use the lowest level that proves the behavior. A direct service call does not prove routing, authorization middleware, or database transactions.

## Set up tests only when the task needs them

- Reuse an existing test project/framework. If none exists and tests are requested, create focused .NET 10 xUnit projects under `tests/`, for example `TravelTracker.UnitTests` and `TravelTracker.Api.IntegrationTests`; reference only the projects each suite needs and add them to `TravelTracker.slnx`.
- Use `dotnet new xunit --framework net10.0 --name TravelTracker.UnitTests --output tests/TravelTracker.UnitTests` as a starting point, then add the actual project references and solution entry. Delete the generated empty/example test when adding meaningful tests.
- Keep packages compatible with .NET 10 and the chosen runner. Add `Microsoft.AspNetCore.Mvc.Testing` for HTTP tests and a supported coverage collector only when required; inspect available package versions instead of guessing them.
- For a top-level API entry point, expose `Program` to `WebApplicationFactory` with a separate `public partial class Program` file if needed. Keep that file limited to the test-host entry point.
- Replace external services/configuration in the test host, seed only test data, and isolate/clean up database resources. Do not require credentials, cloud services, or a production database for routine tests.

## Write focused and deterministic tests

- Follow the human-approved LAB_07-style layout for the standalone services: one xUnit project per API, with all its tests and internal host/helpers in `AttractionsTests.cs` or `TransportTests.cs`. Keep the API project references separate so their `Program` entry points do not collide. Group related cases within that file; production code still follows one feature per file.
- For the current milestone demo, keep at most 30 discovered test cases across both APIs, including every `[InlineData]` row. Prefer core workflows and representative boundaries; document the coverage removed to meet this human-requested limit. Do not hide the old input matrix inside loops just to change the reported count.
- Name tests by behavior and expected outcome, such as `CreateAttraction_InvalidCoordinates_DoesNotPersist`. Arrange minimal data, act on one behavior, then assert its observable result and relevant side effects.
- Use `[Fact]` for one scenario and `[Theory]` for meaningful input variations. A regression test should fail on the old behavior when it can be demonstrated safely.
- Fix identifiers/times/seed data; inject a clock or `TimeProvider` where time is a dependency. Avoid live network calls, arbitrary sleeps, test-order dependence, and uncontrolled shared state. Await asynchronous operations.
- Use small constructor-injected fakes for unit tests. Verify a dependency call only when it is part of the behavior, such as proving invalid input never writes or an accepted UoW operation commits once.
- Use a real relational test provider for relational guarantees. SQLite in memory can exercise many queries, but provider-specific SQL, migrations, constraints, or concurrency need the production-compatible provider. EF's non-relational InMemory provider does not prove transaction correctness.
- For HTTP tests, assert status, response schema/body, and relevant headers, including creation location and problem details. Test auth policies through the host; a test authentication scheme may supply identities but must leave authorization rules active.
- Use the project's `/** ... */`, `@param`, and `@return` documentation for new test/helper members where documentation is needed. Keep simple test bodies readable; comments should explain intent rather than restate assertions.

## Select relevant TravelTracker cases

Derive exact expectations from the current contract and requested milestone. Do not implement planned features solely to populate this list.

- **Attractions/accommodations:** validation boundaries, private/shared visibility, permitted writes, not found, archive behavior, and stable saved trip snapshots.
- **Transport/strategies:** supported modes, unknown mode/code rejection, deterministic algorithm results, and no persistence when merely requesting a quote/route preview.
- **Trips:** accommodation-to-attraction one-way travel; explicit return to the starting place for round trips; connected ordered legs with no sequence gaps; arrival after departure; no overlapping legs/visits; visits at reached places within trip dates; stays may overlap sightseeing; failed validation leaves the aggregate unchanged.
- **Time and history:** deterministic ordering/paging, derived upcoming/current/past status, trip time zones, and ambiguous/impossible local times when the relevant feature exists.
- **Authorization:** unauthenticated protected calls, permitted ownership, another user's private record appearing absent, banned users reading their own data but unable to write, and deactivated users denied access. Administrator status alone does not expose other travellers' private trips.
- **Failure/concurrency:** absent resources, dependency timeout/unavailability, cancellation, stale-edit rejection, and transaction rollback without partial writes. Protected actions must not trust stale permissions when Users is unavailable.
- **Events, when implemented:** duplicate delivery causes one side effect, transient retries stop at their limit, permanent invalid messages are not retried, poison messages reach dead-letter handling, and documented out-of-order behavior is preserved.
- **Contracts:** request/response fields, identifier serialization (`Guid` versus `Id64` as actually agreed), status codes, and compatibility with an older consumer.

## Execute and report evidence

Run from the repository root, adapting targeted commands to the discovered test projects and runner:

```powershell
dotnet restore TravelTracker.slnx
dotnet build TravelTracker.slnx --configuration Release --no-restore
dotnet test TravelTracker.slnx --configuration Release --no-build --no-restore
```

1. Run the changed test project/feature first, then the relevant suite. Complete required checks; repeat them after further edits or failures that justify another run.
2. Request TRX results and `XPlat Code Coverage` only when the configured runner/collector supports them. Keep generated results out of commits and report where evidence was saved.
3. Treat coverage as a gap-finding signal. Do not invent a required percentage or declare quality from coverage alone; use agreed project gates.
4. When CI work is requested, use the lectures' restore -> build -> test flow, publish results even on failure, and require passing agreed checks before application artifacts/deployment. Match this repository's actual branch/provider; lecture examples do not authorize creating a pipeline or deploying.
5. Report commands, executed test counts, pass/fail/skip results, baseline failures versus new failures, covered behaviors, and unverified boundaries. If the build fails, say tests are blocked rather than claiming they passed.

Lecture and video provenance: [skills-sources.md](../../../docs/skills-sources.md).
