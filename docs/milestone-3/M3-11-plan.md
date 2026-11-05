# M3-11 Users Service Plan

Issue: [#37, M3-11](https://github.com/NewWaveOwl/TravelTracker/issues/37)
Dependencies: [#27, M3-01](https://github.com/NewWaveOwl/TravelTracker/issues/27), [#34, M3-08](https://github.com/NewWaveOwl/TravelTracker/issues/34)
Status: implementation is present; acceptance verification is incomplete.

## Owner decisions recorded

The following five decisions were provided on 2026-11-04 and are no longer open:

1. **Users port:** use local port 5101.
2. **M3-07 provisioning handoff:** the frontend completes OIDC sign-in and obtains an API access token. It calls `GET /api/v1/users/me` with that bearer token. Users validates it and provisions a local profile on the first valid request using the validated `(iss, sub)` pair as the identity key. Later requests return the existing profile.
3. **Account standing:** a deactivated profile returns 403 and is never automatically reactivated.
4. **Attractions access lookup:** include `GET /internal/users/{userId}/access`, protected by a service access token with `users.access`. Restrict this permission to authorized backend services; frontend tokens must not receive it. Attractions denies access when the user is missing or deactivated, or when the lookup fails.
5. **Test ownership:** the testing agent may create and maintain Users tests. Each layer agent may edit tests for its own layer. The exact Users test directories are recorded in the applicable agent instructions before implementation.

The identity-to-public-ID integration is present in the current checkout: Attractions POSTs its validated `(iss, sub)` to the Users internal identity resolver, then uses the returned public ID for the access lookup. The Users guide and Attractions client describe this flow. Verify its authorization and fail-closed behavior in the applicable test suite before marking the acceptance evidence complete.

## Ground Truth

This plan follows [design.md](../design.md), [domain.md](../domain.md), [api.md](../api.md), [data.md](../data.md), [milestone-3-contracts.md](../milestone-3-contracts.md), [authentication.md](../authentication.md), and the current [layer-agent ownership rules](../../.github/agents/).

The independent Users Domain/Data/Api projects, PostgreSQL persistence, profile endpoints, internal access endpoints, and associated tests now exist. The six-service architecture uses API-to-Data-to-Domain dependency direction, and each service owns its database.

The existing shared `TravelTracker.Domain.Models.User` and internal `TravelTracker.Data.Repositories.IUserRepository` stay unchanged for the current scaffold. M3-11 does not reuse, move, or delete them. The Users service owns new `UserAccount` and `IUserRepository` types in its own projects. Retiring the shared model or repository requires a separate cleanup after confirming that no remaining consumers depend on them.

Baseline build verification on 2026-11-04: `dotnet build TravelTracker.slnx --configuration Release` succeeded with 0 warnings and 0 errors on the current checkout.

### Current implementation checkpoint (2026-11-05)

- **Domain — present:** `src/TravelTracker.Users.Domain/Models/UserAccount.cs`, `UserRole.cs`, `UserStatus.cs`, `Types/ProviderIdentity.cs`, and Users-owned identifier types. `TravelTracker.Users.Domain` has no EF Core or ASP.NET Core dependency. Domain tests are in `tests/TravelTracker.Users.Domain.Tests/`.
- **Data — present:** `src/TravelTracker.Users.Data/UsersDbContext.cs`, `Entities/UserRecord.cs`, `Configurations/UserRecordConfiguration.cs`, profile repositories under `Repositories/Profile/`, the account/administrator repository, and committed PostgreSQL migrations. Data references Users.Domain and EF Core/Npgsql. Relational tests are in `tests/TravelTracker.Users.Data.Tests/`.
- **API — present:** independent host in `src/TravelTracker.Users.Api/`, with `GET`/`PATCH /api/v1/users/me`, `GET /internal/users/{userId}/access`, `POST /internal/users/identity`, health, and additional bootstrap/administrator operations. `Program.cs` configures bearer validation, named Users policies, PostgreSQL, and host composition. Self-profile orchestration and validation live in `Services/UserProfileService.cs`; `UsersController` maps service outcomes to the existing HTTP responses.
- **API tests — present:** HTTP and signed-bearer Users tests currently live in `tests/TravelTracker.Users.Tests/`, not the originally proposed `tests/TravelTracker.Users.Api.Tests/` directory. The test-project path should be treated as an implementation difference, not as a missing API project.
- **Attractions contract — reconciled in code/docs:** `src/TravelTracker.Attractions.Api/Access/UsersAccessClient.cs` resolves the validated identity, then looks up standing by the returned public user ID. `docs/attractions-service.md` describes the same two-request flow.
- **Verification evidence:** `dotnet build src/TravelTracker.Users.Api/TravelTracker.Users.Api.csproj --configuration Release --no-restore --verbosity minimal --maxcpucount:1` succeeded with 0 warnings and 0 errors after the service extraction. `dotnet test TravelTracker.Tests.slnx --configuration Release --no-restore --filter "Category!=PostgreSQL" --verbosity minimal --maxcpucount:1` passed 547 cases with 0 failures and 0 skipped. The Data test project has only PostgreSQL cases, so that filter matched none there. The first restricted-run test attempt could not connect to testhost; rerunning outside that restriction completed successfully. A PostgreSQL test connection was not configured, so relational tests remain unverified. `git diff --check` passed.
- **Changes made in this review:** extracted self-profile orchestration/validation from the controller into the API service, registered it in DI, corrected the stale profile-status text in `docs/api.md`, and updated this plan to show current implementation and remaining verification.

### Service decisions

- Proposed service-owned projects: `TravelTracker.Users.Domain`, `TravelTracker.Users.Data`, and `TravelTracker.Users.Api` under `src/TravelTracker.Users.Domain/`, `src/TravelTracker.Users.Data/`, and `src/TravelTracker.Users.Api/`.
- The approved Users local port is **5101**; current independent Attractions and Transport hosts use 5104 and 5105.
- PostgreSQL with EF Core/Npgsql is selected. Each service owns its database. M3-01 does not set a Users database name; `traveltracker_users` is a proposed local name, with the connection supplied through `ConnectionStrings__Users` or equivalent secret-free environment configuration.
- Preserve app-issued public UUIDv7 identifiers and keep internal numeric keys out of Domain/API contracts.

### Authentication and account rules

- Follow the real JWT bearer validation pattern from #34: validate signature, exact issuer and audience, token lifetime, and a nonblank validated subject. Use validated `(iss, sub)`; never trust a separate client identity value. The provider remains deployment-configured and is not selected by this plan.
- Reference the shared `TravelTracker.Security` project, which implements the #34 bearer validation pattern. Its representative catalogue policies are `catalogue.read` and `catalogue.write`; Users defines distinct named policies `users.read` and `users.write` using the shared exact, ordinal, case-sensitive scope matcher. The current matcher handles space-separated values and repeated claims. Do not claim JSON-array scope support unless the shared implementation is extended and tested. A read-only scope must not authorize PATCH, and a write-only scope must not authorize GET.
- The frontend completes OIDC sign-in and obtains an API access token; Users does not implement sign-in. `GET /api/v1/users/me` validates the bearer token (signature, issuer, audience, lifetime, and nonblank validated subject), and resolves identity only from validated `(iss, sub)`. On the first valid request, create a profile with server-generated `PublicId`, Users.Domain-owned `UserRole.User`, Users.Domain-owned `UserStatus.Active`, and UTC creation time. Subsequent requests return that same profile for the identity pair. Never create an account from an invalid token or copy identity, role, or status from request data or unverified token claims. The database enforces a unique composite index on issuer and subject; handle a concurrent first-request conflict by reloading the row for that same validated pair.
- Before the first successful PATCH, `GET /api/v1/users/me` returns `200` with the newly provisioned `PublicId`, server-controlled role/status and timestamps, `username: null`, `email: null`, and `address: null`. The response DTO models `Username` and `Email` as nullable to represent this valid incomplete profile. GET does not invent or infer either value.
- Proposed editable owner fields are `Username`, `Email`, and `Address`. The public `PublicId`, provider identity mapping, `Role`, `Status`, and `CreatedAt` are server-controlled; `UpdatedAt` is set by the server on successful edits. Profile responses expose `PublicId` only, never provider identity or internal keys such as `Id64`.
- [Design.md](../design.md) says banned travellers can view their own data and change nothing. Therefore a banned user may GET their own profile and receives 403 for PATCH. Deactivated users can do nothing and receive 403 for either profile operation with a valid token. A deactivated profile is never automatically reactivated by GET, PATCH, or internal lookup. [Domain.md](../domain.md) confirms the existing `Active`, `Deactivated`, `Banned` values but defines no access rules; the endpoint behavior comes from the design contract, not the enum. Users.Domain owns the role and status types regardless of future changes to account-status policy; any change to the existing values is a separate owner decision.
- A valid token denied by local account standing receives 403, including all profile operations for a deactivated account and profile writes for a banned account. Invalid/missing/expired bearer tokens return 401 during JWT authentication, before account lookup. A provider-revoked token that no longer validates also returns 401.
- Administrators do not gain access to another person's private profile through these self-service routes.
- Logs must not contain profile PII or identity material: do not log request/response bodies, `Username`, `Email`, `Address`, `PublicId`, provider issuer/subject, bearer tokens, or authorization headers. Log only operational metadata such as operation, outcome category, status code, and correlation ID. Add an API test with captured logs that submits distinctive test PII and asserts none of those values appear in emitted log messages or structured properties.

### Explicitly deferred

Admin account search, admin status-change routes, audit workflows, account deletion, and password endpoints are outside this profile slice. Status enforcement is in scope; changing a user's status is not. Frontend OIDC sign-in remains in M3-07; this plan includes the `/me` profile handoff only.

The internal access lookup is in scope. `GET /internal/users/{userId}/access` accepts the public Users UUID, requires a valid service access token and the exact `users.access` permission, and is restricted to configured/authorized backend service clients. Never grant `users.access` to the frontend OIDC client or accept client-submitted owner identity as proof of authorization. Return only `userId` (the same public UUID), `status` (`active`, `banned`, or `deactivated`), and `role` (`traveller` or `administrator`), with no profile fields, provider identity, or internal key. Return `200` for an existing account, including deactivated accounts so the consumer can deny access based on standing; return `404` for an unknown public ID. Invalid/missing token returns `401`; an authenticated caller lacking `users.access` or not on the authorized service-client allowlist gets `403`; unavailable required storage returns `503`. No account is provisioned or reactivated by this endpoint.

## Approved HTTP Contract (implemented; verification pending)

Use the `/api/v1` prefix and JSON camelCase. The self-profile routes contain no user ID in the route, query, or request body. Resolve the account only from the validated token's issuer and subject. A profile request cannot select, create, or mutate an account by supplying a client-controlled ID or identity value.

| Method and route | Policy | Success and failure outcomes |
|---|---|---|
| `GET /health` | `[AllowAnonymous]` | `200` service health. No bearer token required. |
| `GET /api/v1/users/me` | `[Authorize(Policy = "users.read")]` | `200` own profile; an authorized first request provisions the local account from validated `(iss, sub)` and returns its incomplete profile with null `Username`, `Email`, and `Address`. `401` absent/invalid token or missing validated subject; `403` missing scope or deactivated account; `503` required storage unavailable. Active and banned accounts may read their own profile. |
| `PATCH /api/v1/users/me` | `[Authorize(Policy = "users.write")]` | `200` updated own profile; `400` invalid fields, empty patch, or unknown JSON properties; `401` absent/invalid token or missing validated subject; `403` missing scope, banned account, or deactivated account; `409` username uniqueness conflict or stale concurrent update; `503` required storage unavailable. Active accounts only may update. |
| `GET /internal/users/{userId}/access` | `[Authorize(Policy = "users.access")]` plus authorized backend-client restriction | `200` minimal access record for an existing public user ID; `401` invalid/missing service token; `403` missing `users.access` or unauthorized backend client; `404` unknown user ID; `503` required storage unavailable. This operation never provisions or reactivates an account. |

Every protected action has an explicit named policy attribute; do not rely on a fallback/default policy for Users operations. `users.read` and `users.write` are independent. Health is explicitly marked `[AllowAnonymous]`.

`UserProfileResponse` exposes `PublicId` and profile fields including server-controlled role, status, and timestamps. It contains no `Id64`, internal key, issuer, or subject. `UserAccessResponse` is internal-only and contains exactly `userId`, `status`, and `role`. `UpdateUserProfileRequest` contains only `Username`, `Email`, and `Address`; it has no ID, role, status, issuer, or subject property. Configure JSON unmapped-member handling to reject unknown properties, and validate the update DTO before mutation. Rejected updates leave the persisted profile unchanged.

PATCH semantics and validation:

- Omitted properties are unchanged. At least one of `Username`, `Email`, or `Address` must be present; an empty object is `400`.
- `Username` must be a non-null string, trimmed, 3 to 32 characters, and contain only ASCII letters, digits, `.`, `_`, or `-`. It must not be blank. The trimmed value is unique case-insensitively; persist/enforce a normalized nullable key with a PostgreSQL partial unique index whose predicate excludes null (`WHERE NormalizedUsername IS NOT NULL`). This allows all newly provisioned accounts to remain null until the owner chooses a username. A collision returns `409` with a `username_conflict` problem code. `Email` and `Address` are not unique fields.
- `Email` must be a non-null, nonblank string. Trim it, require a valid email address syntax, and limit it to 254 characters. Explicit JSON null is invalid.
- `Address` is optional and nullable. Explicit JSON null clears it. A non-null value must not be blank, is trimmed, and is limited to 500 characters.
- JSON values of the wrong type, unknown properties, and invalid supplied fields return `400`; no failed request changes any profile field or `UpdatedAt`.
- A persisted optimistic concurrency token belongs to each `UserRecord`. If simultaneous PATCH requests read the same version, only one may save; the stale request returns `409` with a `profile_concurrency_conflict` problem code, and the winner's complete profile remains stored. This status is separate from the `username_conflict` response. Do not retry-and-merge: a stale partial update can otherwise silently overwrite a field that the caller did not see, and the API contract already requires rejected writes to leave the submitted update unapplied.

M3-11 covers self-profile operations and the internal access lookup, not provider sign-in or the full administrator feature set. The checkout contains separate bootstrap and administrator operations; those are outside this issue's acceptance scope.

## File-by-File Implementation Review

The implementation below is already present. Do not recreate these projects. Remaining work is acceptance verification and any gap fixes that verification establishes.

The file and test paths listed in the following proposal describe the original intended layout. Where they differ from the checkout, use the current implementation checkpoint above as the source of truth; the differences are not, by themselves, missing work.

### Domain

- `src/TravelTracker.Users.Domain/TravelTracker.Users.Domain.csproj`: new service-owned Domain project with no EF Core or ASP.NET Core dependencies.
- `src/TravelTracker.Users.Domain/Models/UserAccount.cs`: profile, Users-owned role/status, `PublicId`, creation/update timestamps, and pure profile invariants. `Username` and `Email` are nullable until set by the owner through PATCH.
- `src/TravelTracker.Users.Domain/Types/ProviderIdentity.cs`: validated issuer/subject value type; no provider SDK dependency.
- `src/TravelTracker.Users.Domain/Models/UserRole.cs` and `UserStatus.cs`: Users.Domain-owned role/status types unconditionally; preserve the current `UserRole.User = 0`, `Administrator = 1`, `UserStatus.Active = 0`, `Deactivated = 1`, and `Banned = 2` values and defaults. Do not reference the shared `TravelTracker.Domain` enum types.
- `src/TravelTracker.Users.Domain/Types/EntityId.cs`: Users-owned `EntityId<T>` pairs an internal `Id64` key with a public UUIDv7. `Id64` remains internal and visible to Users.Data only; the Users API cannot name or expose it. Only Users.Data creates identifiers.

### Data

- `src/TravelTracker.Users.Data/TravelTracker.Users.Data.csproj`: new Data project referencing Users.Domain and EF Core/Npgsql only.
- `src/TravelTracker.Users.Data/UsersDbContext.cs`: service-owned EF model and DbSets.
- `src/TravelTracker.Users.Data/Entities/UserRecord.cs`: persistence representation, including provider issuer/subject, internal key, nullable username/email and normalized username, and a non-null optimistic concurrency token.
- `src/TravelTracker.Users.Data/Configurations/UserRecordConfiguration.cs`: field constraints, concurrency-token mapping, and unique public ID, normalized username, and issuer/subject indexes.
- `src/TravelTracker.Users.Data/Repositories/IUserRepository.cs` and `UserRepository.cs`: resolve by provider identity/public ID and persist allowed profile updates; no HTTP concerns.
- `src/TravelTracker.Users.Data/EntityIdGenerator.cs`: app-issued internal/public identity generation following the documented single-instance model.
- `src/TravelTracker.Users.Data/Migrations/`: committed PostgreSQL migration(s); do not use `EnsureCreated` or treat `schema.sql` as applied migration history.

### API

- `src/TravelTracker.Users.Api/TravelTracker.Users.Api.csproj`: new independent .NET 10 host referencing Users.Data, Users.Domain, and the existing shared `TravelTracker.Security` project.
- `src/TravelTracker.Users.Api/Program.cs` and `Program.TestHost.cs`: production composition and isolated test-host composition; authentication before authorization.
- `src/TravelTracker.Users.Api/Properties/launchSettings.json`, `appsettings.json`, and `appsettings.Development.json`: local port 5101, separate Users database configuration, and safe placeholder bearer settings; no credentials or real tokens.
- `src/TravelTracker.Users.Api/Contracts/UserProfileResponse.cs` and `UpdateUserProfileRequest.cs`: response exposes `PublicId` and nullable `Username`/`Email`/`Address` but no internal/provider identity; update DTO contains only `Username`, `Email`, and `Address`, with unmapped JSON properties rejected.
- `src/TravelTracker.Users.Api/Controllers/UsersController.cs`: `[AllowAnonymous]` health plus `[Authorize(Policy = "users.read")]` `GET /api/v1/users/me` and `[Authorize(Policy = "users.write")]` `PATCH /api/v1/users/me`; no client-supplied user ID.
- `src/TravelTracker.Users.Api/Services/IUserProfileService.cs` and `UserProfileService.cs`: validated-claim mapping, ownership, account-standing checks, and update orchestration.
- Configure named `users.read`, `users.write`, and `users.access` policies with the shared scope matcher, and explicit endpoint attributes. Restrict `users.access` to the authorized backend service clients as well as requiring the exact scope. Keep service-token configuration separate from frontend API tokens. Identity resolution and first-use account creation use only the authenticated principal's validated issuer/subject.
- `src/TravelTracker.Users.Api/Contracts/UserAccessResponse.cs`: internal-only minimal access response containing public `userId`, account `status`, and `role`; never expose profile PII, provider identity, or internal keys.
- `src/TravelTracker.Users.Api/Controllers/InternalUsersController.cs`: `GET /internal/users/{userId}/access`, protected by `users.access` and backend-client restriction; returns existing account standing only and never provisions/reactivates.

### Tests and solution wiring

- `tests/TravelTracker.Users.Domain.Tests/TravelTracker.Users.Domain.Tests.csproj` and `UserAccountTests.cs`: test pure profile invariants and account defaults alongside the Domain behavior they prove.
- `tests/TravelTracker.Users.Data.Tests/TravelTracker.Users.Data.Tests.csproj` and `UserRepositoryPostgreSqlTests.cs`: test EF mapping, the unique issuer/subject index, migrations, constraints, and durable restart behavior against isolated PostgreSQL; mark relational tests `Category=PostgreSQL`.
- `tests/TravelTracker.Users.Api.Tests/TravelTracker.Users.Api.Tests.csproj`: isolated Users API host and HTTP test dependencies; use the test-only EF InMemory adapter only for non-relational HTTP behavior, never as the production database or evidence of relational durability.
- `tests/TravelTracker.Users.Api.Tests/UserProfileTests.cs`: test first-request `/me` provisioning, repeated GET returning the same `PublicId`, invalid tokens not provisioning, PATCH, validated-claim-only resolution, field validation including explicit null, rejected identity/role/status/ID injection, unknown JSON fields, profile unchanged on rejected writes, and active/banned/deactivated outcomes.
- `tests/TravelTracker.Users.Api.Tests/AuthenticationTests.cs`: using the real JWT bearer handler, test no token, invalid signature, expired/wrong-issuer/wrong-audience token (401), missing validated subject (401), missing scope (403), read-only scope denied on PATCH (403), write-only scope denied on GET (403), correct independent scope on each operation, and anonymous health.
- `tests/TravelTracker.Users.Api.Tests/InternalUserAccessTests.cs`: test authorized service token with `users.access`, denied frontend token even if it has self-profile scopes, denied backend token without `users.access`, denied unapproved service client, minimal response fields, unknown user (404), deactivated account returned as deactivated without reactivation, and storage outage (503).
- `tests/TravelTracker.Users.Api.Tests/StorageFailureTests.cs`: use a test repository that reports storage unavailable during a protected profile request and assert `503`, without returning exception details or PII. Run as a non-PostgreSQL API test.
- `tests/TravelTracker.Users.Api.Tests/UserProfilePostgreSqlTests.cs`: deterministically exercise concurrent PATCH. A test-only EF Core `SaveChangesInterceptor` blocks both requests at save until both have loaded the same `UserRecord` and reached the save boundary; then release both. Use non-overlapping patch fields (for example one changes Email and one changes Address) so username uniqueness cannot decide the outcome. Assert exactly one `200`, one `409 profile_concurrency_conflict`, and that the stored record contains the winner's changes only. This synchronizes at the read/save boundary without adding a production test hook. Mark `Category=PostgreSQL`.
- Capture application logs in `UserProfileTests.cs`; verify submitted test values for username, email, address, provider identity, and token are absent from log messages and structured properties on success and rejected requests.
- Add each production project to `TravelTracker.slnx` in its own layer commit, and add each corresponding test project to `TravelTracker.Tests.slnx` in that same layer commit. Domain commit adds Users.Domain and Users.Domain.Tests; Data commit adds Users.Data and Users.Data.Tests; API commit adds Users.Api and Users.Api.Tests. Do not defer project inclusion to a later integration commit.
- Per-account-state assertions: Active can GET/PATCH; Banned can GET but PATCH returns 403 and stored profile is unchanged; Deactivated GET/PATCH return 403 and stored profile is unchanged. A deactivated record is not reactivated by repeated GET or internal access lookup. Invalid, unknown-property, username-conflict, and stale-concurrency updates leave persisted state consistent and do not change unrelated fields.
- The testing agent may create and maintain all Users tests. The Domain, Data, and API layer agents may edit tests only in their corresponding Users test directory. These exact directories are recorded in `.github/agents/testing.agent.md`, `.github/agents/domain-dev.agent.md`, `.github/agents/data-persistence.agent.md`, and `.github/agents/api-service.agent.md`. Shared helpers under each test project's `Support/` require explicit file-level authorization.

### Original commit-sized implementation proposal (historical; do not replay)

Keep the implementation in separate layer commits. Each commit adds its layer's production project to `TravelTracker.slnx` and test project to `TravelTracker.Tests.slnx`; each commit must pass its listed validation before starting the next layer.

1. **Domain commit:** add Users.Domain account, provider identity, `EntityId<T>`, Users-owned role/status types, pure field validation, and UserAccount tests. Add Users.Domain to `TravelTracker.slnx` and Users.Domain.Tests to `TravelTracker.Tests.slnx`. Validate with `dotnet build TravelTracker.slnx --configuration Release` and `dotnet test tests/TravelTracker.Users.Domain.Tests/TravelTracker.Users.Domain.Tests.csproj --configuration Release`.
2. **Data commit:** add UserRecord, PostgreSQL mapping/migration, repository, nullable normalized username uniqueness, and optimistic concurrency token. Add Users.Data to `TravelTracker.slnx` and Users.Data.Tests to `TravelTracker.Tests.slnx`. Validate the build and Data project against isolated PostgreSQL with `TRAVELTRACKER_TEST_POSTGRES` configured and `CREATEDB` permission: `dotnet build TravelTracker.slnx --configuration Release` and `dotnet test tests/TravelTracker.Users.Data.Tests/TravelTracker.Users.Data.Tests.csproj --configuration Release --filter "Category=PostgreSQL"`.
3. **API commit:** add the Users host, `users.read`/`users.write`/`users.access` policies, profile and internal lookup endpoints, validation/problem responses, account-standing behavior, PII-safe logging, and isolated HTTP tests. Add Users.Api to `TravelTracker.slnx` and Users.Api.Tests to `TravelTracker.Tests.slnx`. Validate the build and non-relational API behavior, including the storage-unavailable 503, first/repeated provisioning, invalid token, deactivated-account, and internal-lookup permission tests, with `dotnet build TravelTracker.slnx --configuration Release` and `dotnet test tests/TravelTracker.Users.Api.Tests/TravelTracker.Users.Api.Tests.csproj --configuration Release --filter "Category!=PostgreSQL"`. Validate the deterministic concurrent HTTP PATCH race with `dotnet test tests/TravelTracker.Users.Api.Tests/TravelTracker.Users.Api.Tests.csproj --configuration Release --filter "Category=PostgreSQL"` against isolated PostgreSQL.
4. **Contract/documentation commit:** update the Users/API/domain/data/authentication/milestone docs, README, and Attractions consumer contract to the approved internal route, trusted user-ID mapping, and response. Ensure CI includes all tests. No project wiring is deferred to this commit. Validate with `dotnet build TravelTracker.slnx --configuration Release`, `dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"`, and `dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category=PostgreSQL"` with isolated PostgreSQL configured.

The repository's CI workflow [ci.yml](../../.github/workflows/ci.yml) starts a disposable PostgreSQL 17 service for pull requests. It first runs `Category!=PostgreSQL` with `TRAVELTRACKER_TEST_POSTGRES` cleared, then runs `Category=PostgreSQL` against that service. Locally, run the ordinary filter without PostgreSQL; run the relational filter only with `TRAVELTRACKER_TEST_POSTGRES` pointing to an isolated PostgreSQL instance where the test user has `CREATEDB`. Never use production credentials or a production database for tests.

### Remaining verification plan

1. **Done:** profile request validation and application orchestration were extracted from `UsersController` into `UserProfileService`; the HTTP behavior tests pass after the refactor.
2. **Done:** the non-PostgreSQL suite passed 547 cases with 0 failures and 0 skipped.
3. **Pending PostgreSQL environment:** run the relational suite against a disposable database with `TRAVELTRACKER_TEST_POSTGRES` and CREATEDB permission. Confirm migrations/restart durability, constraints, unique mappings/usernames, and concurrent-write behavior.
4. **Partially verified:** the HTTP suites pass, including Users auth and Attractions backchannel coverage. Still verify independent Users startup/health on port 5101 and, where available, the full frontend-to-Users journey.
5. Compare the collected evidence with every issue acceptance criterion. Fix only confirmed gaps in the owning layer and its authorized tests, then rerun affected tests and the appropriate validation gate.

The original sequence below records how the implementation was expected to be built. The projects and documentation already exist; do not replay those implementation steps.

## Acceptance Evidence to Confirm

- Independent Users host starts on port 5101 with other service hosts stopped; anonymous health succeeds and protected profile operations require a valid Users-audience token.
- The frontend completes OIDC sign-in and calls `GET /api/v1/users/me` with its API access token. The validated `(iss, sub)` is the sole profile-provisioning key; the first valid request creates one profile and repeated requests return it. Invalid tokens never provision. A deactivated profile returns 403 and is never automatically reactivated.
- First-use creation assigns server-controlled role/status/PublicId while Username and Email remain null. A GET before the first PATCH returns 200 and explicitly serializes those profile fields as null. No route, body, query, or header user ID or identity value can choose an account.
- Only whitelisted profile fields change. Unknown JSON properties and attempts to supply server-owned fields are rejected, and invalid or prohibited updates leave stored data unchanged. The validation contract covers omitted properties, empty PATCH, explicit null for each field, normalization, format, length, and username uniqueness. The normalized username unique index excludes null rows.
- Active, banned, and deactivated behavior follows [design.md](../design.md): banned users can read their own profile but cannot change it; deactivated users cannot read or write. Tests document these results. [Domain.md](../domain.md) supplies the status enum but no separate access rule.
- A deterministic barrier in a test-only EF Core `SaveChangesInterceptor` proves both concurrent PATCH requests loaded the same version before either saves. Exactly one succeeds and the stale writer gets `409 profile_concurrency_conflict`; username collisions independently get `409 username_conflict`. The conflict is preferred to retry-and-merge because an automatic merge could apply a stale caller's edit without their seeing the intervening profile change.
- Authentication tests cover independent read/write scopes and a validly signed token with a missing subject, in addition to the other bearer cases.
- Internal lookup tests prove only an authorized backend client with `users.access` can call the route; frontend tokens cannot receive or use that permission. The response exposes only public user ID, role, and standing. Missing/deactivated users are denied by Attractions, and lookup failures fail closed.
- A non-PostgreSQL storage-unavailable test verifies `503` on a protected profile request without leaking exception details.
- Captured-log tests prove no profile PII, provider identity, or bearer token is emitted for successful or rejected requests.
- PostgreSQL migration and restart tests prove durable profile and mapping persistence. Ordinary tests run without PostgreSQL; relational tests use an isolated PostgreSQL instance.
- The owner decisions listed at the top are resolved. The code and current Attractions guide use trusted identity-to-public-ID resolution followed by the ID-keyed access lookup; test this flow before marking it verified. Valid-token account-standing failures are documented as 403.
