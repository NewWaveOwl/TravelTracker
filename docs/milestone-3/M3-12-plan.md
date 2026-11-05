# M3-12 Accommodations Service Plan

Issue: [#38, M3-12](https://github.com/NewWaveOwl/TravelTracker/issues/38)
Dependencies: [#27, M3-01](https://github.com/NewWaveOwl/TravelTracker/issues/27), [#34, M3-08](https://github.com/NewWaveOwl/TravelTracker/issues/34)
Status: research complete; awaiting developer approval before production changes.

## Goal

Create an independently runnable Accommodations service with its own Domain, Data, and Api projects. Persist accommodation records in PostgreSQL and provide authenticated creation, collection search, and detail operations with location validation and private/shared visibility.

This plan is for a separate branch based on the latest `milestone_3` line, which contains the listed prerequisites. It must not depend on the unmerged M3-11 feature branch. Recheck the repository and dependencies when implementation starts.

## Ground truth reviewed

- [Design](../design.md): section 3 defines active, banned, deactivated, and administrator behavior; section 4 says places are private to their creator unless an administrator adds them to the shared catalogue; section 6 assigns Accommodations to Christel and lists add/search/view/edit/archive; section 7 describes place search and field-level validation.
- [Domain](../domain.md): the legacy shared `Accomodation` has only `Id`, `Name`, and `Address`; it is explicitly retained unchanged. RoutePoint has coordinates but is a distinct snapshot/location record.
- [Data](../data.md): the old `IAccomadationRepository` and `Accomodation` names/contracts are preserved as scaffold. They do not establish an independent service implementation.
- [API](../api.md): no accommodation service/API exists. Attractions is the closest implemented independent catalogue pattern, with separate projects, PostgreSQL, validated public DTOs, owner mapping, scope policies, and private/shared checks.
- [Milestone 3 contracts](../milestone-3-contracts.md): Accommodations use `catalogue.read` and `catalogue.write`; private places belong to their creator. The explicit 2026-10-28 admin-shared decision is written for Attractions and says Accommodations remain their owner's work, so this plan proposes applying the same semantics to Accommodations rather than claiming that decision already covered them.
- Current checkout: separate Attractions, Transport, and Users project families exist. Existing tests are grouped by service, including PostgreSQL-backed persistence and API/auth/privacy suites. No Accommodations projects or tests exist.

## Proposed behavior and boundaries

- Use canonical new names under `TravelTracker.Accommodations.*`, with `Accommodation` as the new service Domain model. Leave the existing shared `TravelTracker.Domain.Models.Accomodation` and `TravelTracker.Data.Repositories.IAccomadationRepository` unchanged for compatibility; document that they are historical scaffold and do not cross the new service boundary. No broad rename or migration of existing consumers is included.
- A record has a server-generated public GUID, validated owner `(issuer, subject)`, name, address, latitude, longitude, and visibility. Keep owner identity out of public request/response DTOs.
- Trim and require a nonblank name; require latitude in `[-90, 90]` and longitude in `[-180, 180]`. Address defaults to an empty string when omitted and rejects explicit null, following the current location DTO convention. Invalid values produce field-keyed validation errors and are never persisted.
- Omitted visibility defaults to private. The creator can find their own private records. Other users cannot find a private record in collection search or detail (return 404). Shared records are discoverable to authorized active catalogue readers. Only an administrator may create or change a record to shared; a traveller may create private records. Owners may edit their own private records. For shared-record edits, follow the Attractions implementation's explicit administrator/owner rule after confirming its current code during implementation.
- Active users may read; banned users may read their own private records and shared records but may not create or change records. Deactivated/missing users are denied. Use live Users access checks for standing and administrator role where the Attractions integration requires them; fail closed if that required dependency is unavailable.
- Every protected route declares its policy explicitly: `catalogue.read` for collection/detail, `catalogue.write` for create, and anonymous `[AllowAnonymous]` for `GET /health`. A valid bearer without the necessary scope gets 403; absent/invalid bearer gets 401.
- Keep this issue to create/search/detail. PATCH/archive are product design concepts but are not in the M3-12 engineering tasks and are excluded unless the issue owner explicitly expands scope. No delete endpoint.
- Treat a route point of type `accommodation` as a location snapshot only; it does not create or replace an accommodation catalogue record.

## Proposed HTTP contract

Base path: `/api/v1`; JSON camelCase; public GUID identifiers.

| Method and route | Policy | Contract |
| --- | --- | --- |
| `GET /health` | Anonymous | `200` service health. |
| `POST /api/v1/accommodations` | `catalogue.write` | Creates a private record by default; optional `visibility: "shared"` is accepted only for an authorized administrator. Returns `201` and the public accommodation representation. |
| `GET /api/v1/accommodations?name=...` | `catalogue.read` | Returns matching records visible to the caller, ordered deterministically. Missing/blank name returns the visible collection; a supplied name is a trimmed literal case-insensitive substring filter. |
| `GET /api/v1/accommodations/{id}` | `catalogue.read` | Returns a visible record; malformed GUID is `400`, missing or another user's private record is `404`. |

Proposed public response: `accommodationId`, `name`, `address`, `lat`, `lon`, `visibility`. Owner `(issuer, subject)` and persistence keys are never returned. POST rejects unknown properties and client attempts to supply IDs, ownership, or account role/status. Define exact field limits, duplicate query-parameter behavior, and complete error payloads by following the matching Attractions contracts/tests during implementation and documenting any deliberate differences.

## File-by-file implementation plan

### New production files

- `src/TravelTracker.Accommodations.Domain/TravelTracker.Accommodations.Domain.csproj` — standalone Domain project with no EF, HTTP, or other service dependency.
- `src/TravelTracker.Accommodations.Domain/Models/Accommodation.cs` — immutable/service-owned record with owner identity, coordinates, visibility, public ID, and pure field validation.
- `src/TravelTracker.Accommodations.Domain/Types/AccommodationId.cs` (or a project-local ID value type matching the current Attractions conventions) — public ID boundary without exposing database keys.
- `src/TravelTracker.Accommodations.Data/TravelTracker.Accommodations.Data.csproj` — references only the Accommodations Domain and approved EF Core/Npgsql packages.
- `src/TravelTracker.Accommodations.Data/AccommodationsDbContext.cs` — service-scoped EF context and migration assembly configuration.
- `src/TravelTracker.Accommodations.Data/Entities/AccommodationRow.cs` — persistence row with unique public ID and owner/visibility fields.
- `src/TravelTracker.Accommodations.Data/Configurations/AccommodationConfiguration.cs` — PostgreSQL column constraints, indexes, and mapping.
- `src/TravelTracker.Accommodations.Data/Repositories/IAccommodationRepository.cs` and `EfAccommodationRepository.cs` — persistence queries that apply visibility predicates before returning detail/search results.
- `src/TravelTracker.Accommodations.Data/AccommodationsDesignTimeFactory.cs` — EF migration tooling factory.
- `src/TravelTracker.Accommodations.Data/Migrations/*` — initial migration and model snapshot, generated from the configured model.
- `src/TravelTracker.Accommodations.Api/TravelTracker.Accommodations.Api.csproj` — independently runnable host referencing Security, Data, and Domain in the approved one-way direction.
- `src/TravelTracker.Accommodations.Api/Program.cs` — independent configuration, JWT bearer validation, explicit catalogue scope policies, PostgreSQL registration/migration startup behavior matching current service conventions, and Swagger in Development.
- `src/TravelTracker.Accommodations.Api/Contracts/CreateAccommodationRequest.cs`, `AccommodationRecord.cs`, and search/result/error contracts as needed — public DTOs only; no persistence/domain entity exposure.
- `src/TravelTracker.Accommodations.Api/Controllers/AccommodationsController.cs` and `HealthController.cs` — thin authorized routes and intentionally public health check.
- `src/TravelTracker.Accommodations.Api/Services/AccommodationService.cs` plus interface — orchestration, validated caller identity, visibility/standing decisions, and mapping to DTOs.
- `src/TravelTracker.Accommodations.Api/Access/*` — only the minimum Users access integration needed for live standing and administrator checks, aligned with the Attractions service boundary and secret/service-token configuration.
- `src/TravelTracker.Accommodations.Api/Errors/*` — documented validation and dependency error mapping.
- `src/TravelTracker.Accommodations.Api/appsettings.json`, `appsettings.Development.json`, and `requests.http` — safe connection-string/configuration placeholders and runnable examples; no credentials committed.

### New test files

- `tests/TravelTracker.Accommodations.Domain.Tests/*` — valid/invalid names and coordinate boundaries, default visibility, and owner/visibility invariants.
- `tests/TravelTracker.Accommodations.Data.Tests/*` — PostgreSQL mapping, migration application, create/search/detail persistence across context/process restart, owner filters, and rejected-record non-persistence.
- `tests/TravelTracker.Accommodations.Api.Tests/*` — create/search/detail DTO contracts; validation field errors; malformed/unknown IDs; private record invisibility; owner/admin/shared access; banned/deactivated/dependency failure behavior; spoofed identity/role/status rejection; and full missing/invalid/valid bearer × read/write scope matrix. Use PostgreSQL for persistence behavior and test doubles only for isolated API edges, following existing Attractions test ownership patterns.

### Existing files to update

- `TravelTracker.Tests.slnx` — include the three new projects and their tests.
- `docs/domain.md` — add the independent Accommodations model and explicitly distinguish it from the retained legacy spelling.
- `docs/data.md` — record the service-owned PostgreSQL context/repository and retained historical repository contract.
- `docs/api.md` — publish routes, DTO fields, query behavior, policies, validation, status/visibility results, and public health.
- `docs/milestone-3-contracts.md` — record the Accommodation visibility rule as implemented and close/update any scope ambiguity without changing the prior Attractions decision.
- `README.md` and/or a new `docs/accommodations-service.md` — independent startup, configuration, migration/database setup, port, health endpoint, and manual request examples.
- `src/TravelTracker.Api/Program.cs` / existing shared host — do not wire the new service into the combined host; independent startup is part of acceptance.

### Files deliberately left unchanged

- `src/TravelTracker.Domain/Models/Accomodation.cs`
- `src/TravelTracker.Data/Repositories/IAccomadationRepository.cs`
- Existing Attractions, Transport, Users production contracts and data models, except for any small shared integration change shown necessary by repository research and separately reviewed before implementation.

## Acceptance and validation evidence

- Build the independently runnable Accommodations API and the solution with zero warnings/errors.
- Apply the initial migration to PostgreSQL and prove created records remain after disposing/recreating the host/context.
- Prove invalid name/coordinates yield field errors and no row is written.
- Prove users see their own private and shared records, other users cannot infer private records through either search or detail, and insufficient scope is 403.
- Prove request-supplied IDs/issuer/subject/role/status cannot select ownership or elevate permissions.
- Prove explicit policies distinguish 401 invalid/missing bearer from 403 insufficient scope; health is anonymous.
- Run applicable new service tests and relevant shared auth tests, inspect the final diff and secret scan, and document commands/results before PR.

## Risks and choices to verify during implementation

1. The M3-12 issue says “agreed shared/private visibility,” while the written milestone decision formally records creator-private/admin-shared for Attractions and says Accommodations remain its owner's work. This plan recommends the same semantics for the accommodation catalogue; implementation must not silently invent a different rule.
2. The old accommodation scaffold has no coordinates or visibility. The new service model should be independent rather than extending that shared class and risking unrelated consumers.
3. PostgreSQL test execution needs the same local/container database setup as the existing service tests. Match the repo's current PostgreSQL test helper/configuration and report any environment limitation without substituting in-memory EF for persistence acceptance.
4. M3-12 excludes update/archive endpoints despite the broader design document describing them; scope stays create/search/detail unless the issue owner expands it.

## Approval gate

This is a proposed plan only. Do not create or modify production files until the developer approves this file-by-file plan and any policy/scope choices above.
