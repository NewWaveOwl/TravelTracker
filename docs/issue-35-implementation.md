# Issue #35 implementation and verification

## Approval and scope

The owner selected PostgreSQL and requested the affected documentation/AI guidance updates on 2026-10-28, then explicitly approved the independent-host plan and creator-private/admin-shared implementation in chat. Work starts from main development branch `milestone_3` at `3fee8b3`, on `independent-attractions-35`. The [approved file plan](issue-35-plan.md) and [current service guide](attractions-service.md) record the decisions and boundaries.

Attractions now owns dedicated Domain/Data/Api projects, PostgreSQL context/configuration/migration and scoped repository. Its original HTTP DTO fields, routes, validation/search/repeated-query/PATCH behavior are retained. The additive creation field `visibility` defaults private; shared creation/editing requires live Users administrator standing. Ownership is the validated `(iss, sub)`; other people's private records appear absent, including to administrators. Numeric keys remain internal, UUIDv7 identifiers remain public, and decimal coordinates/rating map to exact PostgreSQL numeric values.

The remaining Transport/Route Points host moves to 5105; Attractions retains 5104 and returns anonymous health identity `Attractions`. Both reuse the reviewed bearer implementation through `TravelTracker.Security`, with no host-to-host project reference. Test probes and HTTP stubs exist only in test assemblies.

## Atomicity and durability

POST commits a validated row with a server-issued identity and ownership. PATCH locks the target PostgreSQL row inside an explicit transaction, checks visibility/permissions, validates an independent candidate and commits once. Invalid candidates roll back. Concurrent disjoint patches preserve their fields; there is no new ETag wire contract. The app allocator starts above the persisted maximum and retains its documented single-instance limit. It uses no database identity or sequence.

Committed migration SQL was generated and reviewed: bigint primary key with no generated value, unique UUID index, exact numeric columns, and name/coordinate/rating/duration/private-owner constraints. Tests apply the real migration, reject PostgreSQL constraint/uniqueness violations, and verify the snapshot has no pending model changes. Optional demo records seed an empty store atomically; enabling seeding later on a nonempty unseeded database neither overwrites records nor collides with their keys.

## Users boundary and honest limits

A configurable HTTPS Users consumer endpoint receives a service-authenticated request for the validated issuer/subject and returns current role/standing. No standing result is cached. Banned callers can read only their own private records and cannot write; deactivated/deleted callers are denied. Missing configuration, upstream failures, unknown/malformed responses, invalid service-header configuration and unsupported response charset fail closed with 503 before catalogue access. Cancellation propagates; public errors contain no credential/identity/exception details.

This issue implements the Attractions **consumer**, not the Users server. The exact proposed consumer contract is documented for the Users owner to integrate; a live upstream implementation, selected identity provider and frontend journey are not available and are not claimed verified. Independent deployment/startup does not mean dependency-free business operations: with Users down, public health works and protected operations intentionally fail closed. No production bypass was added for the demonstration.

## Validation evidence

- Baseline at merged mainline: **147 passed, 0 failed, 0 skipped**.
- Final full solution restore succeeded. Release build: **0 warnings, 0 errors**.
- Full regression run: **222 passed, 0 failed, 0 skipped**: 89 legacy Transport/shared-domain/authentication cases plus 133 independent Attractions cases. Existing attraction cases moved to their owning host; token/discovery policies are independently verified in each host. No original resource scenario was removed to lower counts.
- PostgreSQL verification used official EDB PostgreSQL 17 binaries in a temporary, loopback-only, password-protected local instance, not SQLite/EF InMemory. Every test owns a disposable database. Isolated test credentials, logs and TRX files are under ignored temporary/artifact paths, never source.
- Attraction tests cover the real signed-bearer missing/invalid/signature/issuer/audience/lifetime/scope/public-health matrix, discovery parsing/JWKS retrieval, private ownership/admin isolation, shared-admin writes, client owner/key rejection, current standing changes, Users failures, exact search semantics and atomic invalid writes.
- Two successive isolated hosts against one retained PostgreSQL database verify created/PATCH values, public IDs, exact decimal precision, repeated migration startup and subsequent ID allocation after restart. A separate regression enables example seeding after an unseeded database already contains an application-issued key.
- Actual standalone process on **5104**, with Transport on 5105 stopped: `/health` returned `200 {"status":"ok","service":"Attractions"}`; anonymous catalogue returned 401; Swagger contained only attraction and health routes. Startup applied the migration to a fresh local demo database and persisted three optional shared examples. The process was stopped after checking. Live successful provider-issued-token/Users requests were not available; those request paths are proven in isolated HTTP tests with real bearer validation and the controlled Users stub.
- CI now provisions its own loopback-only PostgreSQL 17 service and runs both test projects through the existing test solution. Remote CI must be checked on the PR; local success is not remote CI evidence.
- Changed-document relative links and diff whitespace checks passed; canonical coding/testing/planning/validation skills retain valid frontmatter. PostgreSQL decisions, current runtime ownership, ports and test prerequisites are reflected in docs/AI guidance.

Commands: `dotnet restore TravelTracker.slnx`; `dotnet build TravelTracker.slnx --configuration Release --no-restore`; `dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --logger "trx;LogFileName=issue35-final.trx"` with isolated `TRAVELTRACKER_TEST_POSTGRES`. Migration SQL: `dotnet-ef migrations script --project src/TravelTracker.Attractions.Data --configuration Release` using the compatible repository-local tooling.

## Independent read-only review

The repository-required code-review agent inspected the extraction, ownership/middleware, EF identity/transactions/migration and tests. It found two Medium issues: optional demo seeds could collide with existing app-issued keys, and malformed Users credentials/response charset could escape as 500. Both were fixed with regression tests and re-reviewed. Final result: **0 Critical, 0 High, 0 Medium, 0 Low actionable findings; Ready to merge subject to final build/test and CI checks**. This is agent review evidence, not another team member's approval. No merge is authorized or claimed.
