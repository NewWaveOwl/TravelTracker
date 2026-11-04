# M3-11 Users Service Plan

Issue: [#37, M3-11](https://github.com/NewWaveOwl/TravelTracker/issues/37)
Dependencies: [#27, M3-01](https://github.com/NewWaveOwl/TravelTracker/issues/27), [#34, M3-08](https://github.com/NewWaveOwl/TravelTracker/issues/34)
Status: approved for planning; implementation requires its own approval.

## Ground Truth

This plan follows [design.md](../design.md), [domain.md](../domain.md), [api.md](../api.md), [data.md](../data.md), [milestone-3-contracts.md](../milestone-3-contracts.md), [authentication.md](../authentication.md), and the current [layer-agent ownership rules](../../.github/agents/).

The current `User` domain model and internal repository interface exist, but there is no Users API or durable Users service. The six-service architecture proposes an independent Domain/Data/Api set per service, with API-to-Data-to-Domain dependency direction and no cross-service database reads.

### Service decisions

- Proposed projects: `TravelTracker.Users.Domain`, `TravelTracker.Users.Data`, and `TravelTracker.Users.Api` under `src/TravelTracker.Users.Domain/`, `src/TravelTracker.Users.Data/`, and `src/TravelTracker.Users.Api/`.
- M3-01 does not assign a Users port. Propose **5101** for local development; current independent Attractions and Transport hosts use 5104 and 5105.
- PostgreSQL with EF Core/Npgsql is selected. Each service owns its database. M3-01 does not set a Users database name; `traveltracker_users` is a proposed local name, with the connection supplied through `ConnectionStrings__Users` or equivalent secret-free environment configuration.
- Preserve app-issued public UUIDv7 identifiers and keep internal numeric keys out of Domain/API contracts.

### Authentication and account rules

- Follow the real JWT bearer validation pattern from #34: validate signature, exact issuer and audience, token lifetime, and a nonblank validated subject. Use validated `(iss, sub)`; never trust a separate client identity value. The provider remains deployment-configured and is not selected by this plan.
- Parse the configured scope claim (default `scope`) using the #34 rules: space-separated values, repeated claims, or JSON string arrays; exact, ordinal, case-sensitive matches. Roles or partial matches grant no permission.
- Resolve the account only from the validated `(iss, sub)` pair. On the first authorized self-profile request, create a local account if that pair has no row; assign a server-generated `PublicId`, `UserRole.User`, `UserStatus.Active`, and UTC creation time. Do not copy identity, role, or status from request data or unverified token claims. The database enforces a unique composite index on issuer and subject; handle a concurrent first-request conflict by reloading the row for that same validated pair. No password or sign-in endpoint is included; provider sign-in belongs to M3-07.
- Proposed editable owner fields are `Username`, `Email`, and `Address`. The public `PublicId`, provider identity mapping, `Role`, `Status`, and `CreatedAt` are server-controlled; `UpdatedAt` is set by the server on successful edits. Profile responses expose `PublicId` only, never provider identity or internal keys such as `Id64`.
- Active users may read and update their own profile. Banned users may read their own data but cannot change it; writes are forbidden. Deactivated users may do nothing. The status model remains the existing `Active`, `Deactivated`, `Banned` enum unless the Users owner separately approves the proposed `SpecialStatus` change.
- A valid token denied by local account standing receives 403, including all profile operations for a deactivated account and profile writes for a banned account. Invalid/missing/expired bearer tokens return 401 during JWT authentication, before account lookup. A provider-revoked token that no longer validates also returns 401.
- Administrators do not gain access to another person's private profile through these self-service routes.

### Explicitly deferred

Admin account search, admin status-change routes, audit workflows, account deletion, sign-in/registration/password endpoints, and frontend integration are outside this profile slice. Status enforcement is in scope; changing a user's status is not.

The Attractions consumer contract describes a server-to-server Users access lookup, but M3-11 does not explicitly name that route or its permission. Keep it out of the committed endpoint surface pending confirmation. If approved, add a configurable internal lookup contract with a dedicated least-privilege `users.access` policy; do not reuse an overbroad administrator permission. The consumer's request/response semantics are recorded in [attractions-service.md](../attractions-service.md).

## Proposed HTTP Contract

Use the `/api/v1` prefix and JSON camelCase. The self-profile routes contain no user ID in the route, query, or request body. Resolve the account only from the validated token's issuer and subject. A profile request cannot select, create, or mutate an account by supplying a client-controlled ID or identity value.

| Method and route | Policy | Success and failure outcomes |
|---|---|---|
| `GET /health` | `[AllowAnonymous]` | `200` service health. No bearer token required. |
| `GET /api/v1/users/me` | `[Authorize(Policy = "users.read")]` | `200` own profile; an authorized first request provisions the local account from validated `(iss, sub)` and returns its profile. `401` absent/invalid token or missing validated subject; `403` missing scope or deactivated account; `503` required storage unavailable. Active and banned accounts may read their own profile. |
| `PATCH /api/v1/users/me` | `[Authorize(Policy = "users.write")]` | `200` updated own profile; `400` invalid fields or unknown JSON properties; `401` absent/invalid token or missing validated subject; `403` missing scope, banned account, or deactivated account; `409` unique editable-field conflict; `503` required storage unavailable. Active accounts only may update. |

Every protected action has an explicit named policy attribute; do not rely on a fallback/default policy for Users operations. `users.read` and `users.write` are independent. Health is explicitly marked `[AllowAnonymous]`.

`UserProfileResponse` exposes `PublicId` and profile fields including server-controlled role, status, and timestamps. It contains no `Id64`, internal key, issuer, or subject. `UpdateUserProfileRequest` contains only `Username`, `Email`, and `Address`; it has no ID, role, status, issuer, or subject property. Configure JSON unmapped-member handling to reject unknown properties, and validate the update DTO before mutation. Rejected updates leave the persisted profile unchanged.

No Users admin or provider-login route is added. If the internal access lookup is approved, document its path, service-token validation, `users.access` policy, unknown-account semantics, and HTTP behavior before implementation; it must not be silently inferred from this plan.

## File-by-File Plan

### Domain

- `src/TravelTracker.Users.Domain/TravelTracker.Users.Domain.csproj` — new service-owned Domain project with no EF Core or ASP.NET Core dependencies.
- `src/TravelTracker.Users.Domain/Models/UserAccount.cs` — profile, role/status, `PublicId`, creation/update timestamps, and pure profile invariants.
- `src/TravelTracker.Users.Domain/Types/ProviderIdentity.cs` — validated issuer/subject value type; no provider SDK dependency.
- `src/TravelTracker.Users.Domain/Models/UserRole.cs` and `UserStatus.cs` — service-owned role/status types if the existing shared types are retired from Users ownership; preserve current enum values and defaults.

### Data

- `src/TravelTracker.Users.Data/TravelTracker.Users.Data.csproj` — new Data project referencing Users.Domain and EF Core/Npgsql only.
- `src/TravelTracker.Users.Data/UsersDbContext.cs` — service-owned EF model and DbSets.
- `src/TravelTracker.Users.Data/Entities/UserRecord.cs` — persistence representation, including provider issuer/subject and internal key.
- `src/TravelTracker.Users.Data/Configurations/UserRecordConfiguration.cs` — field constraints and unique public ID and issuer/subject indexes.
- `src/TravelTracker.Users.Data/Repositories/IUserRepository.cs` and `UserRepository.cs` — resolve by provider identity/public ID and persist allowed profile updates; no HTTP concerns.
- `src/TravelTracker.Users.Data/EntityIdGenerator.cs` — app-issued internal/public identity generation following the documented single-instance model.
- `src/TravelTracker.Users.Data/Migrations/` — committed PostgreSQL migration(s); do not use `EnsureCreated` or treat `schema.sql` as applied migration history.

### API

- `src/TravelTracker.Users.Api/TravelTracker.Users.Api.csproj` — new independent .NET 10 host referencing Users.Data, Users.Domain, and shared `TravelTracker.Security`.
- `src/TravelTracker.Users.Api/Program.cs` and `Program.TestHost.cs` — production composition and isolated test-host composition; authentication before authorization.
- `src/TravelTracker.Users.Api/Properties/launchSettings.json`, `appsettings.json`, and `appsettings.Development.json` — proposed local port 5101, separate Users database configuration, and safe placeholder bearer settings; no credentials or real tokens.
- `src/TravelTracker.Users.Api/Contracts/UserProfileResponse.cs` and `UpdateUserProfileRequest.cs` — response exposes `PublicId` but no internal/provider identity; update DTO contains only `Username`, `Email`, and `Address`, with unmapped JSON properties rejected.
- `src/TravelTracker.Users.Api/Controllers/UsersController.cs` — `[AllowAnonymous]` health plus `[Authorize(Policy = "users.read")]` `GET /api/v1/users/me` and `[Authorize(Policy = "users.write")]` `PATCH /api/v1/users/me`; no client-supplied user ID.
- `src/TravelTracker.Users.Api/Services/IUserProfileService.cs` and `UserProfileService.cs` — validated-claim mapping, ownership, account-standing checks, and update orchestration.
- Configure named `users.read` and `users.write` policies and explicit endpoint attributes; identity resolution and first-use account creation use only the authenticated principal's validated issuer/subject. Add `users.access` only if the internal consumer lookup receives separate approval.

### Tests and solution wiring

- `tests/TravelTracker.Users.Domain.Tests/TravelTracker.Users.Domain.Tests.csproj` and `UserAccountTests.cs` — test pure profile invariants and account defaults alongside the Domain behavior they prove.
- `tests/TravelTracker.Users.Data.Tests/TravelTracker.Users.Data.Tests.csproj` and `UserRepositoryPostgreSqlTests.cs` — test EF mapping, the unique issuer/subject index, migrations, constraints, and durable restart behavior against isolated PostgreSQL; mark relational tests `Category=PostgreSQL`.
- `tests/TravelTracker.Users.Api.Tests/TravelTracker.Users.Api.Tests.csproj` — isolated Users API host and HTTP test dependencies; use the test-only EF InMemory adapter only for non-relational HTTP behavior, never as the production database or evidence of relational durability.
- `tests/TravelTracker.Users.Api.Tests/UserProfileTests.cs` — test `/me` GET/PATCH, validated-claim-only resolution/provisioning, rejected identity/role/status/ID injection, unknown JSON fields, profile unchanged on rejected writes, and active/banned/deactivated outcomes.
- `tests/TravelTracker.Users.Api.Tests/AuthenticationTests.cs` — test no token, invalid signature, expired/wrong-issuer/wrong-audience token (401), wrong/missing scope (403), and correct-scope success using the real JWT bearer handler; verify health remains anonymous.
- `TravelTracker.slnx` and `TravelTracker.Tests.slnx` — include the new production and test projects.
- Per-account-state assertions: Active can GET/PATCH; Banned can GET but PATCH returns 403 and stored profile is unchanged; Deactivated GET/PATCH return 403 and stored profile is unchanged. Invalid, unknown-property, and uniqueness-conflict updates also leave the prior profile unchanged.
- Existing layer agents restrict test edits to current files. Before implementation, explicitly assign these new Domain, Data, and API test paths to the testing agent or update the relevant test-file permissions; do not silently cross those boundaries. Any shared test helper under `Support/` must be named and authorized explicitly.

### Documentation

- `docs/users-service.md` — new startup, database, bearer configuration, profile contract, account-state behavior and policy map.
- `docs/api.md` — add Users routes and their outcomes; distinguish the approved slice from old scaffold routes.
- `docs/domain.md` — describe Users.Domain ownership, provider mapping and owner/server-controlled fields.
- `docs/data.md` — document Users-owned PostgreSQL persistence, migration and provider identity uniqueness.
- `docs/authentication.md` — document Users policies and the unresolved/deployment-specific identity choices; add `users.access` only if approved.
- `docs/milestone-3-contracts.md` — record the Users contract, selected port/database name if confirmed, deferred admin work, deactivated response choice, and any approved internal lookup.
- `README.md` — update project inventory, running instructions and capability status without claiming provider sign-in or admin management is implemented.

## Acceptance Evidence

- Independent Users host starts with other service hosts stopped; anonymous health succeeds and protected profile operations require a valid Users-audience token.
- The validated `(iss, sub)` is the sole account-resolution key; first-use creation assigns server-controlled role/status/PublicId. No route, body, query, or header user ID or identity value can choose an account.
- Only whitelisted profile fields change. Unknown JSON properties and attempts to supply server-owned fields are rejected, and invalid or prohibited updates leave stored data unchanged.
- Active, banned, and deactivated behavior; no/invalid token; wrong and correct scopes; uniqueness conflicts; storage outages; and public health have explicit tests and documented HTTP results.
- PostgreSQL migration and restart tests prove durable profile and mapping persistence. Ordinary tests run without PostgreSQL; relational tests use an isolated PostgreSQL instance.
- Resolve before implementation: Users port, exact account-provisioning handoff with M3-07, whether to include the Attractions access lookup and `users.access`, and test-agent ownership of new test paths. The proposed profile route is `/me`; valid-token account-standing failures are documented as 403.