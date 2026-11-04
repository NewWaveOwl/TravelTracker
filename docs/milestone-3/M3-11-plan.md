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
- Map the validated issuer/subject pair to a persistent local account whose public UUID is separate from the provider subject. No password or sign-in endpoint is included; provider sign-in belongs to M3-07.
- Proposed editable owner fields are `Username`, `Email`, and `Address`. The public user ID, provider identity mapping, `Role`, `Status`, and `CreatedAt` are server-controlled; `UpdatedAt` is set by the server on successful edits. Profile responses omit provider identity and internal keys.
- Active users may read and update their own profile. Banned users may read their own data but cannot change it; writes are forbidden. Deactivated users may do nothing. The status model remains the existing `Active`, `Deactivated`, `Banned` enum unless the Users owner separately approves the proposed `SpecialStatus` change.
- A valid token denied by account standing is proposed to receive 403. The contract still leaves deactivated-user 401 versus 403 unresolved; confirm with the identity-provider behavior before finalizing the service contract. Invalid/missing bearer tokens return 401.
- Administrators do not gain access to another person's private profile through these self-service routes.

### Explicitly deferred

Admin account search, admin status-change routes, audit workflows, account deletion, sign-in/registration/password endpoints, and frontend integration are outside this profile slice. Status enforcement is in scope; changing a user's status is not.

The Attractions consumer contract describes a server-to-server Users access lookup, but M3-11 does not explicitly name that route or its permission. Keep it out of the committed endpoint surface pending confirmation. If approved, add a configurable internal lookup contract with a dedicated least-privilege `users.access` policy; do not reuse an overbroad administrator permission. The consumer's request/response semantics are recorded in [attractions-service.md](../attractions-service.md).

## Proposed HTTP Contract

Use the `/api/v1` prefix, JSON camelCase, and public UUIDs. The profile paths below are a proposal consistent with the existing scaffold's `/users/{id}` route and M3-11's ID-spoofing acceptance case. Resolve the caller from validated claims, then require the route ID to identify that caller's local account.

| Method and route | Policy | Success and failure outcomes |
|---|---|---|
| `GET /health` | Anonymous; explicitly allow anonymous access | `200` service health. No bearer token required. |
| `GET /api/v1/users/{id}` | `users.read` | `200` own profile; `400` malformed UUID; `401` absent/invalid token or missing validated subject; `403` missing scope or standing disallows the read; `404` unknown or non-self ID; `503` required storage unavailable. Active and banned accounts may read their own profile. |
| `PATCH /api/v1/users/{id}` | `users.write` | `200` updated profile; `400` malformed UUID, invalid fields, unknown properties, or attempts to change server-owned fields; `401` absent/invalid token or missing validated subject; `403` missing scope or account standing disallows the write; `404` unknown or non-self ID; `409` unique-field conflict; `503` required storage unavailable. Active accounts only may update. |

The proposed response includes profile fields, public user ID, role, status and timestamps, but not `(iss, sub)` or internal numeric keys. Update requests whitelist only owner-editable fields. A mismatch between the validated caller and route ID returns 404 to preserve the privacy rule that another person's private resource looks absent. Read and write scopes remain independent.

No Users admin or provider-login route is added. If the internal access lookup is approved, document its path, service-token validation, `users.access` policy, unknown-account semantics, and HTTP behavior before implementation; it must not be silently inferred from this plan.

## File-by-File Plan

### Domain

- `src/TravelTracker.Users.Domain/TravelTracker.Users.Domain.csproj` — new service-owned Domain project with no EF Core or ASP.NET Core dependencies.
- `src/TravelTracker.Users.Domain/Models/UserAccount.cs` — profile, role/status, public identity, creation/update timestamps, and pure profile invariants.
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
- `src/TravelTracker.Users.Api/Contracts/UserProfileResponse.cs` and `UpdateUserProfileRequest.cs` — public DTOs with a strict writable-field allowlist.
- `src/TravelTracker.Users.Api/Controllers/UsersController.cs` — health and own-profile GET/PATCH routes only.
- `src/TravelTracker.Users.Api/Services/IUserProfileService.cs` and `UserProfileService.cs` — validated-claim mapping, ownership, account-standing checks, and update orchestration.
- Configure named `users.read` and `users.write` policies and an explicitly anonymous health endpoint. Add `users.access` only if the internal consumer lookup receives separate approval.

### Tests and solution wiring

- `tests/TravelTracker.Users.Api.Tests/TravelTracker.Users.Api.Tests.csproj` — isolated Users API host and test dependencies; use EF InMemory for ordinary HTTP/application behavior and PostgreSQL for relational evidence.
- `tests/TravelTracker.Users.Api.Tests/UserProfileTests.cs` — active/banned/deactivated read/write matrix, field validation, persistence across host restart, ID/header/body spoofing, privacy, and 400/403/404/409 outcomes.
- `tests/TravelTracker.Users.Api.Tests/AuthenticationTests.cs` — real bearer validation for signature, issuer, audience, lifetime, subject, exact scope parsing and policy outcomes; health remains anonymous.
- `tests/TravelTracker.Users.Api.Tests/UsersPostgreSqlTests.cs` — migration, unique issuer/subject, constraints, and durable restart behavior, marked `Category=PostgreSQL`.
- `TravelTracker.slnx` and `TravelTracker.Tests.slnx` — include the new production and test projects.
- Existing layer agents restrict test edits to current files. Before implementation, explicitly assign these new test paths to the testing agent or update the relevant test-file permissions; do not silently cross those boundaries. Any shared test helper under `Support/` must be named and authorized explicitly.

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
- The validated `(iss, sub)` resolves the intended persistent account; changing route ID or supplying identity/role/status in headers or JSON cannot access or mutate another account.
- Only whitelisted profile fields change. Invalid or prohibited updates leave stored data unchanged.
- Active, banned and deactivated behavior, missing/wrong scopes, malformed/unknown IDs, uniqueness conflicts, storage outages, and public health are covered with documented HTTP results.
- PostgreSQL migration and restart tests prove durable profile and mapping persistence. Ordinary tests run without PostgreSQL; relational tests use an isolated PostgreSQL instance.
- Resolve before implementation: Users port, profile route shape, deactivated response (401/403), account provisioning handoff with M3-07, whether to include the Attractions access lookup and `users.access`, and test-agent ownership of new test paths.