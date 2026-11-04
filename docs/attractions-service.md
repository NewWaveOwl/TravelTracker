# Independent Attractions service (#35)

Attractions owns its Domain/Data/Api projects and its PostgreSQL database. It runs on port **5104**, and the remaining Transport/Route Points host (`TravelTracker.Api`) runs on **5105**. Neither host references the other's API or reads the other's store. Both use the reviewed `TravelTracker.Security` infrastructure library for bearer validation and OpenAPI security descriptions. The original attraction response fields and resource routes are preserved.

## Start independently

Install .NET 10 and run a dedicated PostgreSQL database. PostgreSQL 17 is used for the local verification and CI baseline; the server must be available independently of the other TravelTracker hosts. Configure its connection through environment variables or local secret storage; never commit a real password:

```powershell
$env:ConnectionStrings__Attractions = 'Host=localhost;Database=traveltracker_attractions;Username=YOUR_LOCAL_ROLE;Password=YOUR_LOCAL_PASSWORD'
$env:Database__ApplyMigrations = 'true'
$env:Database__SeedExamples = 'false'
dotnet run --project src/TravelTracker.Attractions.Api --launch-profile http
```

`GET http://localhost:5104/health` returns exactly `{"status":"ok","service":"Attractions"}` anonymously. Swagger is at `/swagger` in Development. Other service hosts can be stopped: health/startup has no Users dependency. Health is a liveness/service-identity check, not a claim that PostgreSQL or Users is reachable. PostgreSQL is used when operations access records; Users is checked on protected operations as described below.

Database initialization uses **committed EF Core PostgreSQL migrations**, never EnsureCreated. `Database:ApplyMigrations` is an explicit local/startup opt-in; in deployment apply the same migrations with a suitably privileged deployment role before starting the runtime service. The runtime role needs only its own schema's required permissions. `Database:SeedExamples=true` optionally inserts the three shared examples atomically only when the database is empty; a nonempty database is left untouched; default false. Restarting does not reset or overwrite stored records. Do not enable example seeding on production data.

For migration tooling, install a local `dotnet-ef` version compatible with EF Core 10 and use `--project src/TravelTracker.Attractions.Data`. The design-time factory reads `ConnectionStrings__Attractions`; it does not start the API or contact Users. The initial migration maps internal app-issued bigint keys, unique external UUIDs, exact numeric coordinates/rating, ownership and validation constraints. Migration history belongs to this service; the historical `src/Database/schema.sql` is not applied automatically.

The internal key allocator reads the persisted maximum when it first issues an ID after startup, then increments in process. This deliberately retains the documented **single-instance** allocation model: do not run two Attractions writers against one database until a separately reviewed distributed allocator is implemented. Keys are never database identity/sequence-generated and never appear in HTTP DTOs. Public IDs remain UUIDv7. Coordinates/rating retain decimal precision with PostgreSQL `numeric`.

Configure bearer authority/issuer/audience as described in [authentication](authentication.md); use this service's audience. Development `.invalid` placeholders permit health and Swagger exploration, not successful token discovery. PostgreSQL credentials and Users service credentials are separate from bearer settings.

## Private/shared access contract

Creation adds one optional writable field: `visibility`, exactly `private` or `shared`. Omitted visibility defaults to **private**. Explicit null and unknown values are rejected. Existing request bodies otherwise remain valid and response JSON retains exactly `attractionId`, `name`, `description`, `rating`, `address`, `lat`, `lon`, `estimatedVisitMinutes`. Ownership/internal IDs are never exposed. Visibility is immutable through PATCH; a later promotion/archive workflow needs its own contract.

- A private attraction belongs to the token's validated `(iss, sub)`. Clients cannot submit owner fields. Only that identity can read/search/edit it; another traveller or administrator receives 404, including on PATCH.
- Active callers see shared attractions plus their own private attractions. Administrators can create/edit shared attractions; ordinary travellers can view shared attractions but receive 403 when attempting to modify them.
- Both token scope and business access must permit an operation. `catalogue.read` is required for reads; `catalogue.write` for POST/PATCH. Write permission alone does not imply administrator status or permission to read a Location.
- Banned callers may read their own private records, not shared/other private records, and cannot write. Deactivated/deleted callers are denied with 403. The selected response here is the issue #35 API behavior; other service contracts must agree their own standing behavior.
- Users is checked on every protected attraction request, without caching standing. Missing/misconfigured/unavailable Users returns 503 and does not read or mutate attraction data. No client-supplied status, role or identity header bypass exists.

The owner approved the creator-private/admin-shared model in chat after selecting PostgreSQL on 2026-10-28. This resolves catalogue ownership for Attractions; it does not silently approve Accommodations implementation or all outstanding multi-service decisions.

## Users consumer integration boundary

The Users service is not implemented by this issue. This service provides the following explicit **consumer contract**, which the Users owner must adopt/coordinate in its own issue; no working upstream integration is claimed:

Configure an absolute HTTPS `Users:AccessEndpoint` and a server-to-server access token via `Users:ServiceAccessToken` (environment: `Users__AccessEndpoint`, `Users__ServiceAccessToken`). The endpoint address is configurable rather than assumed to exist. The client POSTs JSON `{"issuer":"validated issuer","subject":"validated subject"}` with the configured service Bearer credential. The caller's access token is not reused for another audience. Configure the service credential with only the permission to resolve account access. Redirects are disabled; timeout is five seconds; no account-standing result is cached.

Successful response: `200 {"status":"active","role":"traveller"}`. Allowed status values are `active`, `banned`, `deactivated`; allowed roles are `traveller`, `administrator`. Only an explicit successful `200` response with `status: "deactivated"` denotes a deactivated/deleted account. HTTP 404 is ambiguous (unknown account versus missing route), so it is logged and treated as unavailable (503), like other unsuccessful HTTP responses. Unknown/malformed responses, non-success statuses, connection failure and timeout fail closed with 503. Each failed lookup logs a safe reason distinguishing invalid endpoint/credential configuration, HTTP status, transport failure, timeout, invalid JSON/encoding and unsupported status/role. Logs omit caller identity, endpoint URLs, credentials, response bodies and raw exception text. Caller cancellation propagates without an outage diagnostic. Public failure responses contain no upstream URL, credential, identity or exception text. The Users endpoint must validate the service token and authorize this lookup; this consumer cannot implement that server-side responsibility for it.

**Independence boundary:** the Attractions process and database are independently deployable, but complete protected operations need the configured live Users endpoint. With Users stopped, 503 is intentional. Tests use a controlled HTTP stub to verify this boundary; they do not establish a deployed Users implementation, live identity-provider compatibility or a frontend journey.

## Search, writes and concurrency

Name search retains literal ordinal case-insensitive substring matching after trimming and deterministic name/public-UUID ordering. Records are projected/filtered in the service using those .NET semantics rather than changing behavior to PostgreSQL's collation or ILIKE rules. Repeated decoded/case-varied `name` keys still return the original validation problem before search.

Full replacement and PATCH share one lock/read/authorization/trim/invariant/commit workflow. Both hide absent/private records; visible but unwritable records are forbidden (full replacement throws UnauthorizedAccessException, mapped to 403; PATCH reports Forbidden). PATCH rejects duplicate property names case-insensitively, including identical duplicates. Its separate parser builds a detached candidate without storage or authorization decisions. PATCH keeps omission versus explicit null semantics; only description/rating may be cleared, while unknown/server-owned fields are rejected. It validates an independent candidate, then commits one database write. A PostgreSQL transaction locks the target row during the read/validate/write operation; unrelated concurrent partial patches therefore merge against the latest persisted row rather than losing fields. Rejected candidates roll back. This does not introduce an ETag/If-Match wire contract: explicit stale-client edit detection remains separate work. Request cancellation propagates to new persistence operations. Missing validated issuer/subject is rejected with 401 before Users lookup. Unhandled authorization/validation failures map to 403/400; PostgreSQL unique conflicts, serialization failures (40001), deadlocks (40P01) and EF optimistic concurrency conflicts map to 409; other integrity constraints map to 400, without provider details. Only transient storage failures map to 503; unexpected failures remain 500. An operation canceled without a disconnected caller returns 408; disconnected requests produce no problem body (499 when handled here; ASP.NET Core also handles request aborts). The exception pipeline logs a safe warning for cancellation/4xx and an error for 5xx. Aborts are logged inside the pipeline before ASP.NET Core handles them; duplicate raw framework diagnostics are suppressed for handled exceptions. Logs omit raw exceptions and identity data for handled failures before the response starts; failures after response start remain unhandled by the framework.

## Verification

Application/domain/HTTP/security tests use an isolated **EF Core InMemory** database by default and need no PostgreSQL installation or connection variable:

```powershell
dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"
```

The test-only adapter delegates EF queries/writes to the production repository while explicitly supplying a no-op write session. It provides no transaction, row lock or durable storage and is not used by production. Relational DELETE is unsupported by that adapter. Domain objects, scope checks, Users HTTP stubs and actual signed-token validation stay unchanged. Each host has its own unique EF InMemory store.

Tests marked `[Trait("Category", "PostgreSQL")]` cover constraints, migrations, first-run initialization/restart, row locking/concurrent PATCH, stale updates after deletion and rejected-write persistence. They explicitly select PostgreSQL and **fail** if the configured isolated instance is absent; no automatic fallback or silent skip:

```powershell
$env:TRAVELTRACKER_TEST_POSTGRES = 'Host=localhost;Database=postgres;Username=YOUR_TEST_ROLE;Password=YOUR_TEST_PASSWORD'
dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category=PostgreSQL"
# Complete suite, including both groups (the CI gate):
dotnet test TravelTracker.Tests.slnx --configuration Release
```

The test PostgreSQL role needs CREATEDB for unique disposable databases. Never point test configuration at production. CI starts its own loopback-only PostgreSQL 17 container and runs both groups explicitly. EF InMemory proves sequential application behavior, not PostgreSQL collation, constraints, migrations, locking, rollback or restart durability. SQLite is not the selected runtime or relational test provider.

Useful attraction validation/search/PATCH, privacy, account-standing and real bearer tests are retained. Transport/Route Points and trip-domain tests remain in their original suite. Users/discovery stubs, the InMemory provider/adapter/initializer and identity probes are test-only and never registered by production startup. This evidence does not establish a live Users implementation, live identity provider or frontend journey.
