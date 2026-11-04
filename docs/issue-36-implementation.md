# Issue #36 implementation and validation record

The owner approved the researched scope in chat on 2026-11-04 before production edits. New branch: `implement-transport-persistence-36`, from the repository default `origin/milestone_3` (50350da), rather than the outdated master referenced by historical contribution examples.

## Approved implementation plan mapped to files

- `src/TravelTracker.Transport.Domain/{Models,Types}`: own TransportMode, RoutePoint and public/internal identities without EF/web dependencies; preserve public data shapes and stable seed UUIDs.
- `src/TravelTracker.Transport.Data/{TransportDbContext,TransportDatabase,EntityIdGenerator}.cs`, `Entities/`, `Repositories/`, `Migrations/`: PostgreSQL tables, constraints, detached repositories, transactional first-run examples and a restart-safe single-process allocator seeded above both tables' maxima.
- `src/TravelTracker.Transport.Api/{TransportProgram.cs,Controllers,Contracts,Services,Errors,Properties}`: independently compose PostgreSQL and shared bearer validation on 5105; preserve existing create/detail operations and expose saved collection selection; keep health anonymous and reads/writes scoped.
- `src/TravelTracker.Api`: retire duplicate runnable host/controllers; retain remaining future service interfaces as a library. Existing shared Trip snapshots remain unchanged in historical Domain and have no dependency on the new service persistence.
- `tests/TravelTracker.Transport.Api.Tests/`: move existing HTTP and production bearer tests to their owning host, add selection/scope/input and PostgreSQL migration/restart/constraint tests. Shared trip/identity tests remain in the original test project.
- Solution files and `README.md`, API/data/domain/authentication/relationship/startup documents: register new projects, document selection and service startup, preserve the Blazor Transport base URL. Frontend selection journey remains #44.

Selection: optional exact mode `code` or point `type`, optional trimmed ordinal case-insensitive name substring; deterministic name/UUID ordering, empty array for no match, 400 for invalid nonempty categories/repeated filters. This fills the issue's saved-record selection requirement without adding scheduled journeys, stops or timing.

## Validation evidence

- Clean base `50350da`: 374 tests passed (89 shared/API, 184 Attractions including 7 PostgreSQL, 101 frontend), zero failures/skips.
- Final restore/build: successful, zero warnings/errors.
- Application filter: 380 passed (8 shared domain/identity, 94 Transport, 177 Attractions, 101 frontend), zero failures/skips.
- PostgreSQL filter: 12 passed (5 Transport, 7 Attractions), zero failures/skips. Total: 392, up 18 from baseline; no existing test removed.
- Real HTTP host against a dedicated local PostgreSQL database: anonymous health 200 with Transport service identity; missing-token transport request 401; invalid-token point request 401. Real signed-bearer success/failure operations and selection/create/detail cases are proven through isolated HTTP test hosts.
- Read-only agent review found two Medium issues (three broken links and category filtering after materialization). Both fixed and re-reviewed: no unresolved findings. CI and another member's approval remain PR checks; no merge requested.
- Frontend selection UI verification remains #44. Existing client Transport URL/detail contract is preserved; no completed selection screen is claimed.
