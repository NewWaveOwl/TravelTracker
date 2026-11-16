# M4-02 privileged API access policy plan

**Issue:** audit and enforce role, scope, standing and owner checks at protected API boundaries.
**Branch:** `privileged-API-access-policies`
**Dependency:** blocked by #97 (validated identity contract), as recorded in the issue.
**Status:** research and proposed authorization matrix; waiting for developer approval before implementation.

## Goal and scope

Apply one explicit, reviewable authorization contract across Accommodations, Attractions, Settings, Transport/Route Points, Trips and Users. Every protected operation must combine its required bearer scope with any applicable live account-standing, role and ownership rule. Service-to-service calls must use the separate allowlisted `users.access` credential. No endpoint may accept a caller-provided role, status, issuer/subject or owner ID as proof of authority.

In scope: API authorization attributes/policies, server-side authorization orchestration, owner filtering, safe dependency failure behavior, API/service contract documentation and automated tests. No UI changes, identity-provider replacement, schema redesign, or new administrator UI.

The Milestone 4 Security PDF named by the issue was not found in this repository checkout. The matrix below follows `docs/design.md`, `docs/domain.md`, `docs/authentication.md`, the service contracts and current code. The user should compare the course PDF before implementation if it adds a conflicting rule.

## Authorization matrix (proposed contract)

Scope is necessary but never sufficient by itself. “Active” and “banned” are resolved from the Users service on protected requests, not accepted from caller claims. `(iss, sub)` below always means the validated bearer identity.

| Host / operation | Required scope | Required live role and standing | Owner / visibility rule | Denial behavior |
| --- | --- | --- | --- | --- |
| All hosts: `GET /health` | None | None | No account data | Public `200`, including with an invalid bearer token |
| Accommodations: list/search and detail | `catalogue.read` | Active or banned | Active sees shared plus own private; banned sees own private only. Administrators do not bypass another owner's private visibility. | No/invalid token `401`; missing scope `403`; hidden or absent record `404`; Users unavailable `503` |
| Accommodations: create private | `catalogue.write` | Active traveller or administrator | Server assigns `(iss, sub)` as owner; request ID/owner/role/status fields are rejected. | Missing scope or non-active account `403`; invalid fields `400`; Users unavailable `503` |
| Accommodations: create shared | `catalogue.write` | Active administrator | Server assigns creator identity; only administrator can set shared visibility. | Non-admin/banned `403`; validation `400`; Users unavailable `503` |
| Attractions: list/search and detail | `catalogue.read` | Active or banned | Same creator-private/shared visibility as Accommodations. Admin cannot read another user's private item. | Hidden or absent private record `404`; missing scope `403`; Users unavailable `503` |
| Attractions: create private | `catalogue.write` | Active traveller or administrator | Server assigns `(iss, sub)`; reject client-controlled owner, role or status. | Non-active account `403`; invalid fields `400`; Users unavailable `503` |
| Attractions: create shared | `catalogue.write` | Active administrator | Shared visibility is server-authorized; creator does not gain private records belonging to others. | Non-admin/banned `403`; Users unavailable `503` |
| Attractions: PATCH private/shared | `catalogue.write` | Active owner for private; active administrator for shared | Private ownership remains `(iss, sub)`; PATCH cannot change owner or visibility. | Other owner's private item `404`; visible but unauthorized shared write `403`; unavailable Users/storage safely fail |
| Settings: `GET /api/v1/settings` | `settings.read` | Active or banned | Only the validated caller's settings; no user ID route/body. | Deactivated/unknown `403`; Users outage `503` |
| Settings: `PUT /api/v1/settings` | `settings.write` | Active only | Persist key is validated `(iss, sub)`; reject identity/owner properties. | Banned/deactivated/unknown `403`; Users outage `503`; concurrency/validation retain documented `412`/`400` behavior |
| Transport modes and Route Points: collection/detail reads | `catalogue.read` | Active account; no administrator role required | These are shared catalogue records and have no per-user owner. | Missing scope `403`; banned/deactivated/unknown `403`; Users outage `503` |
| Transport modes and Route Points: create | `catalogue.write` | Active administrator | Shared catalogue records have no user owner; owner fields are rejected. | Non-admin/banned/deactivated `403`; Users outage `503` |
| Trips: collection/detail reads | `trips.read` | Active or banned | List/detail constrained to validated `(iss, sub)`; administrators cannot read another traveller's trip. | Other owner's or absent trip `404`; deactivated/unknown `403`; Users outage `503` |
| Trips: create | `trips.write` **and** `catalogue.read` | Active only | Server assigns trip owner from validated identity; catalogue reference checks use caller token and preserve private visibility. | Missing either scope `403`; private/absent references `404`; dependency outage `503` |
| Trips: replace | `trips.write` **and** `catalogue.read` | Active owner only | Owner and revision are checked before and during write; admins do not bypass ownership. | Other owner's/absent trip `404`; stale/missing revision `412`/`428`; Users/catalogue outage `503` |
| Users: `GET /api/v1/users/me` | `users.read` | Active or banned | Resolve only the validated `(iss, sub)`; first-use provisioning remains subject to account contract; deactivated cannot read. | Deactivated `403`; no role/status from request |
| Users: `PATCH /api/v1/users/me` | `users.write` | Active only | Only explicitly editable profile fields; role/status/account ID are never writable. | Banned/deactivated `403`; invalid fields `400` |
| Users: `GET /api/v1/users` | **Proposed `users.admin`** (currently `catalogue.read`) | Active administrator, checked in Users persistence | Returns account listing only after live admin check; public IDs only. | Missing scope/non-admin/non-active `403` |
| Users: `PATCH /api/v1/users/{id}/role` | **Proposed `users.admin`** (currently `catalogue.read`) | Active administrator, rechecked transactionally | Target is selected by public ID; caller cannot change own identity; role change requires expected role and reason, audit and last-admin protection. | Non-admin `403`; absent target `404`; conflict/last-admin `409` |
| Users: first-admin bootstrap | `catalogue.read` plus one-time operator secret and configured verified provider identity | Only configured bootstrap identity; no pre-existing admin required | Request cannot select an identity or role; operation is single-use. | Invalid setup credentials `403`; unavailable setup `404`; already used `409` |
| Users: `/internal/users/identity` and `/internal/users/{id}/access` | `users.access` plus allowlisted validated service subject | Authorized backend service only | Resolve/check supplied end-user `(iss, sub)` only when supplied by the allowlisted service after its own token validation; never provision from this lookup. | Non-allowlisted/missing scope `403`; unavailable lookup `503`; unknown identity `404` |
| Users: legacy `/api/v1/users/access` | Separate legacy service bearer credential (anonymous at ASP.NET policy layer) | Configured service credential only | Service-supplied identity is trusted only at this explicitly documented backend boundary; no traveler token or public API key. | Invalid credential `401`; invalid request `400`; must not expose privileged account fields |

### Decisions needing approval with this plan

1. **Dedicated Users administrator scope:** approve `users.admin` for account listing and role changes, replacing the current `catalogue.read` requirement. This prevents catalogue readers from inheriting account-administration permission. It requires a corresponding trusted Supabase hook grant for administrator tokens and docs/configuration updates, but no UI changes.
2. **Transport role and standing policy:** approve live Users checks on Transport/Route Points reads and writes, with active-only reads and administrator-only writes as the shared-catalogue interpretation of `docs/design.md`. Current controllers enforce scopes but do not resolve live standing or role.

If either policy is not approved, keep the existing behavior for that decision and document the exception explicitly; do not infer the missing rule during implementation.

## Current implementation findings

- All six API hosts use the shared JWT bearer validation and explicit scope attributes for their current protected routes. Health routes are explicitly anonymous.
- Accommodations, Attractions, Settings and Trips already resolve Users standing on protected operations; their service-specific docs describe the current fail-closed response. Verify each route and status path against tests before changing behavior.
- Accommodations and Attractions derive owner identity from validated claims, hide another owner's private records, and give administrators no private-record bypass. Attractions has active-admin checks for shared writes; Accommodations has corresponding checks.
- Settings and Trips derive ownership from validated identity. Trips also checks references through their owning services using the caller token and hides absent/private references as `404`.
- Transport currently applies `catalogue.read`/`catalogue.write` but has no live Users lookup, role check or resource owner field. Its catalogue write routes are therefore scope-only today.
- Users `/me` uses dedicated `users.read`/`users.write`; internal routes require `users.access` and an allowlisted service subject. Account listing, role changes and bootstrap currently use `catalogue.read`; list/role persistence performs live administrator checks, while bootstrap additionally requires a one-time operator secret and configured identity.
- The current shared fallback policy requires `catalogue.read` for otherwise unannotated endpoints. The implementation must inventory every mapped route and either give it an explicit policy or deliberately mark it anonymous; an implicit fallback must not stand in for a route-specific access decision.
- The named PDF is unavailable locally. No claims about its additional requirements are made here.

## Files and areas to inspect/change after approval

| File or area | Planned work |
| --- | --- |
| `src/TravelTracker.Security/Authentication/ScopePolicies.cs` and `BearerAuthenticationExtensions.cs` | Add/verify the exact `users.admin` policy if approved; preserve fail-closed scope matching, authenticated fallback and no scope implication. |
| `src/TravelTracker.Users.Api/Program.cs` | Register the admin policy using the configured exact scope; keep `users.access` constrained to `UsersAccess:AllowedServiceSubjects`. |
| `src/TravelTracker.Users.Api/Controllers/UsersController.cs` | Apply the approved administrative policy to list/role routes; preserve transactional live-admin, audit and bootstrap safeguards; review every internal and legacy access route. |
| `src/TravelTracker.Users.Data/Repositories/EfUserRepository.cs` and tests | Preserve in-transaction actor/target/last-admin checks; only change if threat review identifies a demonstrated gap. |
| `src/TravelTracker.Transport.Api/Controllers/TransportModesController.cs`, `RoutePointsController.cs`, `TransportProgram.cs` | If approved, add live Users standing/role resolution at protected boundaries; require active accounts for reads and active administrator for writes; fail closed on Users failure. Keep health public. |
| `src/TravelTracker.Transport.Api/Access/**` (new only if approved) | Add a narrow caller context/client/middleware for validated `(iss, sub)` and live Users result. Avoid duplicate policy logic or trusting token role/status claims. |
| `src/TravelTracker.Accommodations.Api/**` and `src/TravelTracker.Attractions.Api/**` | Audit all route/action and service authorization paths; retain validated owner assignment, creator-only private visibility, live standing and no admin private-data bypass. Fix only gaps found. |
| `src/TravelTracker.Settings.Api/**` | Audit scope, live standing, validated identity ownership, rejected owner fields and outage behavior across read/write routes. |
| `src/TravelTracker.Trips.Api/**`, `src/TravelTracker.Api/Trips/**` | Audit scope combination, live standing, owner filters, revision checks, upstream caller-token use, hidden private references and fail-closed dependency behavior. |
| Six API test projects: `TravelTracker.Accommodations.Api.Tests`, `TravelTracker.Attractions.Api.Tests`, `TravelTracker.Settings.Api.Tests`, `TravelTracker.Transport.Api.Tests`, `TravelTracker.Trips.Api.Tests`, `TravelTracker.Users.Tests` | Complete route-by-route scope/role/standing/owner matrix tests, tampering cases, allowed-operation cases, outage cases and non-disclosing private-resource cases. Existing API test hosts and stubs should be extended rather than replaced. |
| `docs/authentication.md`, `docs/api.md`, and `docs/{accommodations,attractions,settings,transport,trips}-service.md` | Publish the approved endpoint-to-role/scope/owner matrix, exceptions, status behavior, live Users boundary and service failure contract. Update the Supabase hook scope guidance only if `users.admin` is approved. |
| `.github/workflows/ci.yml` / `TravelTracker.Tests.slnx` | Confirm all six host suites are included in the non-PostgreSQL CI group; add wiring only if a suite is missing. |

## Acceptance and test plan

For every matrix row, exercise a valid allowed caller and each denied dimension separately: missing scope, wrong live role, banned/deactivated/unknown standing, different owner, and malformed or client-supplied identity/owner/role/status input. Confirm checks happen before reads or writes where applicable. Use real bearer validation in host tests; use controlled Users HTTP stubs and owner-aware stores, not a fake authenticated principal for tests claiming to verify the boundary.

Required cases include: cross-owner accommodation/attraction search and direct-ID access returns the same `404` as absence; admin still cannot see another person's private record or trip; owner/role/status spoofing cannot change saved identity or permissions; banned user can read only the contract-permitted owned data and cannot mutate; deactivated/unknown accounts are denied; only active administrators can modify shared catalogues or administer accounts; missing any required combined scope returns `403`; valid allowed operations succeed; Users/catalogue timeout, invalid response, unauthorized backend response or malformed identity fails closed with `503`/documented status and no partial write. Preserve health `200` without Users calls, even with malformed bearer input.

Run each affected host's non-PostgreSQL tests, then `dotnet build TravelTracker.Tests.slnx --configuration Release` and `dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"`. Run the PostgreSQL category with an isolated `TRAVELTRACKER_TEST_POSTGRES` database where available because ownership predicates and role/account updates touch persisted data. Record discovered/passed/failed/skipped counts, warning/error counts, commands and dependency limitations. Do not claim provider-hook/live tenant verification from local stubs.

## Risks and boundaries

- Adding `users.admin` is a scope contract change; a provider hook that omits it will correctly return `403` until trusted grants are configured. Do not grant it to ordinary traveller tokens.
- Transport role/standing behavior is not currently in its service contract. The proposed rule needs developer approval before code changes.
- Users outages must not degrade into trusting stale role/standing claims. Use the documented `503` fail-closed behavior.
- Avoid accidental permission broadening through the global `catalogue.read` fallback, token role claims, owner IDs in request DTOs, or administrator bypasses of private ownership.
- Do not log bearer credentials, service tokens, caller identities or private record contents.
- No UI changes are in scope. API scope issuance and HTTP contracts may be documented/configured, but do not modify frontend views or interaction flows.
