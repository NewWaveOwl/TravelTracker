# M4-01 bearer-token validation plan

**Issue:** M4-01 — verify and harden bearer-token validation across the protected API hosts.
**Branch:** `bearer-token`
**Research baseline:** committed tree at `15b4a40`.
**Status:** implemented and validated locally; ready for developer review and pull request.

## Goal and scope

Verify that every protected API host uses the same production JWT bearer validation configured for Supabase Auth, trusts only validated issuer and subject claims, and rejects invalid credentials consistently. Close coverage gaps with deterministic, signed-token HTTP tests and record the endpoint/identity contract and reproducible results.

In scope: the shared `TravelTracker.Security` bearer implementation, its adoption by all six API hosts, the identity and permission checks at their protected HTTP boundaries, host-specific real-bearer tests, and the authentication/API documentation.

Out of scope: changing the approved provider, issuing tokens, frontend sign-in implementation, API passwords, authorization policy redesign, Users standing or ownership rules beyond proving that the authenticated identity cannot be spoofed, and PostgreSQL/Supabase schema or connection changes. The legacy Users `/api/v1/users/access` endpoint uses a separate service credential; preserve and document that distinct contract rather than converting it to a Supabase user token as part of this issue.

## Ground truth

Sources: the issue supplied in chat; [API authentication contract](../authentication.md); [API contracts](../api.md); [design account/access rules](../design.md#3-accounts-and-access); [domain identity boundary](../domain.md); and [data boundaries](../data.md). The owner updated the provider decision to Supabase Auth on 2026-11-13, superseding the Auth0 assumption in the original issue. Supabase also remains the PostgreSQL host; its schema and connection configuration are not part of this change.

The Milestone 4 Security PDF named by the issue was not found in the repository checkout. This plan follows the issue text, the owner's updated Supabase Auth provider decision, and current repository contracts; if the PDF contains additional requirements, compare them before merge.

### Implemented at the research baseline

- `TravelTracker.Security` registers the real ASP.NET Core JWT bearer handler, requires signed tokens, validates signing key, exact configured issuer, audience and lifetime, requires expiration, honors bounded `nbf`/expiry clock skew, allowlists asymmetric algorithms, disables inbound claim mapping, and requires one nonblank `sub` after validation. It suppresses authentication details in responses and logs failure categories without token text.
- All six runnable API hosts reference the shared security project and call `AddApiBearerAuthentication`; each configures authentication before authorization. `TravelTracker.Api` is a library, not a seventh API host. The Blazor frontend is an OIDC client, not an API resource server.
- The existing host test projects already contain real signed-token fixtures and bearer tests, but coverage differs by host. The representative authentication guide describes broad validation evidence; this issue will verify the same required matrix through every host rather than assuming shared registration alone proves all host routes.
- Existing endpoint contracts deliberately distinguish `401` for missing/invalid bearer credentials from `403` for a valid caller lacking the exact required permission. Health endpoints are anonymous.

## Identity and HTTP contract to preserve

- A protected user request is authenticated only by a JWT accepted by the production bearer handler using the selected Supabase Auth project's trusted OIDC discovery/JWKS configuration. The issuer is the exact project issuer (`https://<project-ref>.supabase.co/auth/v1`), the audience is `authenticated`, and signature, `exp`, `nbf`, and the configured asymmetric signing algorithm are validated.
- Supabase Auth's Custom Access Token Hook must add the top-level space-separated `scope` claim used by the API policies, sourced from trusted server-controlled authorization data. Default Supabase roles or client-editable metadata do not grant TravelTracker API permissions.
- Application identity is the validated `(iss, sub)` pair. `sub` is not a TravelTracker public UUID. A missing, blank, or ambiguous subject fails authentication. Headers, query parameters, and request DTO fields do not replace it; owner/account identifiers are derived from validated claims or rejected as unknown input.
- A missing, malformed, unsigned, expired, not-yet-valid, wrong-issuer, wrong-audience, or wrong-signature token receives `401` and a Bearer challenge. A valid token without the route's explicit scope receives `403`. Authentication failure responses and logs do not reveal the token, signing key, or validation exception details.
- `GET /health` stays anonymous for all six API hosts, including requests that include an invalid bearer token.
- Read/write scopes remain independent and service-specific. Catalogue, Settings, Trips and Users policies remain as recorded in `docs/api.md`; token validation must run before endpoint work, provisioning, ownership lookup, or mutation.
- The separate Users service-to-service access credential remains distinct from Supabase Auth JWT validation and is not interchangeable with a user access token.

## Host and endpoint inventory

| Host | Existing protected endpoint groups to exercise | Expected permission |
| --- | --- | --- |
| Accommodations | `GET /api/v1/accommodations` and `/{id}`; `POST /api/v1/accommodations` | `catalogue.read`; `catalogue.write` |
| Attractions | `GET /api/v1/attractions` and `/{id}`; `POST` and `PATCH` | `catalogue.read`; `catalogue.write` |
| Transport | transport-mode and route-point collection/detail `GET`; their `POST` routes | `catalogue.read`; `catalogue.write` |
| Settings | own-settings `GET` and `PUT` | `settings.read`; `settings.write` |
| Trips | own-trip collection/detail `GET`; trip `POST` and replacement `PUT` | `trips.read`; `trips.write` plus the existing `catalogue.read` dependency permission |
| Users | self-profile `GET`/`PATCH`, bootstrap/admin routes, and internal identity/access routes | Each existing policy and allowlisted service-subject rule documented in `docs/api.md`; preserve the separate legacy service-token endpoint |

The tests should exercise at least one real protected route per host for authentication, then cover every protected operation's existing authorization policy with the relevant host's scope matrix. Do not treat a valid token that lacks permission as an authentication failure.

## File-by-file implementation plan

| File or area | Planned work |
| --- | --- |
| `src/TravelTracker.Security/Authentication/BearerAuthenticationExtensions.cs` | Audit the production handler options, metadata/issuer check, validated subject check, failure response and safe logging against the contract. Change only a demonstrated validation gap; do not weaken validation to accommodate a test or provider misconfiguration. |
| `src/TravelTracker.Security/Authentication/BearerSettings.cs` | Confirm configuration defaults and bounds can match the Supabase Auth project's issuer, `authenticated` audience, scope claim and asymmetric signing algorithm. Change only if the matrix identifies a real gap. |
| `src/TravelTracker.Security/Authentication/ScopePolicies.cs` | Confirm exact, case-sensitive scope parsing stays downstream of authenticated principal validation. Do not rename or broaden scopes in this issue. |
| `src/TravelTracker.Accommodations.Api/Program.cs`, `src/TravelTracker.Attractions.Api/Program.cs`, `src/TravelTracker.Settings.Api/Program.cs`, `src/TravelTracker.Transport.Api/TransportProgram.cs`, `src/TravelTracker.Trips.Api/Program.cs`, `src/TravelTracker.Users.Api/Program.cs` | Verify every host registers the shared handler and runs authentication before authorization. Keep health explicitly anonymous. Edit only a host that fails the audit. |
| `src/TravelTracker.*.Api/appsettings*.json` and host launch settings | Verify development placeholders are nonfunctional, production settings remain externally configured, and every host uses the intended Supabase issuer and `authenticated` audience without secrets. No checked-in token or key material. Edit only if inconsistent. |
| `tests/TravelTracker.Accommodations.Api.Tests/AuthenticationTests.cs` and `Support/ApiFactory.cs` | Completed the missing/malformed/unsigned/bad-signature/wrong-issuer/wrong-audience/expired/no-expiry/future-`nbf`/bad-subject matrix through the host, and assert failures stop before Users calls and do not echo tokens. |
| `tests/TravelTracker.Attractions.Api.Tests/AuthenticationTests.cs` and `Support/ApiFactory.cs` | Retained its broad real-bearer coverage and added a sentinel malformed token with response non-disclosure assertions. Existing identity probes remain test-only. |
| `tests/TravelTracker.Transport.Api.Tests/AuthenticationTests.cs` and `Support/ApiFactory.cs` | Retained its broad real-bearer matrix and added a sentinel malformed token with response non-disclosure assertions. Existing identity probes remain test-only. |
| `tests/TravelTracker.Settings.Api.Tests/AuthenticationTests.cs` and `Support/ApiFactory.cs` | Completed the invalid-token matrix; assert rejected tokens do not reach Users standing lookups and responses do not echo tokens. Health remains public with malformed bearer input. |
| `tests/TravelTracker.Trips.Api.Tests/AuthenticationTests.cs` and `Support/TripsApiFactory.cs` | Completed the invalid-token matrix; assert rejected credentials do not trigger place/transport lookups or writes and responses do not echo tokens. Health remains public with malformed bearer input. |
| `tests/TravelTracker.Users.Tests/UserProfileScopeMatrixTests.cs`, `UsersStartupTests.cs`, and `Support/UsersApiFactory.cs` | Completed the self-profile invalid-token matrix; verify no account provisioning, no token text in responses/logs, and public health with malformed bearer input. |
| `TravelTracker.Tests.slnx` and `.github/workflows/ci.yml` | Verify all six host test projects already run in CI's non-PostgreSQL group. Add project/workflow wiring only if a required host suite is absent. No PostgreSQL migration or test category is needed for this auth-only scope. |
| `docs/authentication.md` | Updated the six-host bearer/health/permission map, validated `(iss, sub)` identity rules, safe failure behavior, Users service-token distinction, reproducible commands and provider-test limitations. Existing `docs/api.md` already records the Users route-specific access contract. |
| `docs/milestone-4/M4-01-plan.md` | This file records implementation scope, validation results and remaining live-provider limitations. |

## Test and validation plan

Use each host's actual `JwtBearer` handler and isolated RSA test key plus a local discovery/JWKS backchannel stub. Do not replace authentication with a fake principal in tests that claim to prove cryptographic validation.

For every host, test a valid correctly signed token with the right issuer, audience, lifetime, and required scope reaches the expected protected operation. Test missing credentials; malformed bearer text; unsigned token; invalid signature; wrong issuer; wrong audience; expiration beyond the configured clock-skew allowance; missing expiration; and `nbf` beyond the allowed skew all return `401`. Preserve explicit tests for the configured clock-skew boundary. Include an invalid-token request to anonymous health and confirm health remains public. Separately prove a valid token without the operation's scope receives `403`.

For identity-sensitive routes, send a valid token together with deliberately conflicting user/issuer/subject values in headers, query, or body where those inputs are bindable. Assert the persisted/returned owner or account lookup uses only the validated `(iss, sub)`, or assert the request is rejected for unknown identity fields. Assert invalid tokens do not provision Users accounts, invoke required Users/lookups, or persist writes. Use a known token sentinel and captured responses/logs to verify no token or validation internals leak.

Run targeted authentication suites for each host, then the full solution build and CI test groups:

```powershell
dotnet build TravelTracker.Tests.slnx --configuration Release
dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"
dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category=PostgreSQL"
```

The PostgreSQL group is not directly implicated, but run the same CI matrix where an isolated Supabase-compatible PostgreSQL test instance is available; do not point tests at the shared or production Supabase database. `scripts/smoke-hosts.sh` can confirm that each host starts independently, but it does not replace the signed-token tests. Record exact discovered/pass/fail/skip counts, build warning/error counts, commands, and any PostgreSQL infrastructure limitation. If Supabase project configuration is available, a separately documented manual smoke test may confirm the project's discovery/JWKS response and one correctly scoped user token without saving tokens or credentials; deterministic local tests remain the automated regression gate.

## Risks and open points

- Existing source files and documentation vary in completeness; test each host instead of inferring safety from its reference to `TravelTracker.Security`.
- The Supabase Auth project's exact issuer, `authenticated` audience, asymmetric signing algorithm, and Custom Access Token Hook's `scope` claim must match deployment configuration. A discovery, signing-key, or hook-grant problem must not be “fixed” by relaxing issuer/audience/signature validation.
- Tests with a local discovery stub verify JWT handler behavior and the wire contract, not Supabase project availability, TLS, live key rotation, configured hook behavior, or deployment settings.
- The Milestone 4 Security PDF is unavailable in this repository snapshot. Compare it before merge if the course provides it elsewhere.

## Implementation and verification record

No production authentication code or API registration required a change: the audit confirmed that all six runnable API hosts use the shared production JWT bearer handler, validate the configured signing key/signature, exact issuer and audience, token lifetime and allowed algorithms, and reject missing/blank/ambiguous subjects after validation. All hosts configure authentication before authorization; health is anonymous. The work therefore closes the cross-host automated coverage and documentation gaps without altering runtime policy.

Files changed:

- `tests/TravelTracker.Accommodations.Api.Tests/AuthenticationTests.cs` and `Support/ApiFactory.cs`
- `tests/TravelTracker.Attractions.Api.Tests/AuthenticationTests.cs` and `Support/ApiFactory.cs`
- `tests/TravelTracker.Settings.Api.Tests/AuthenticationTests.cs` and `Support/ApiFactory.cs`
- `tests/TravelTracker.Transport.Api.Tests/AuthenticationTests.cs` and `Support/ApiFactory.cs`
- `tests/TravelTracker.Trips.Api.Tests/AuthenticationTests.cs` and `Support/TripsApiFactory.cs`
- `tests/TravelTracker.Users.Tests/UserProfileScopeMatrixTests.cs`, `UsersStartupTests.cs`, and `Support/UsersApiFactory.cs`
- `docs/authentication.md`

Validation on branch `bearer-token`:

- `dotnet restore TravelTracker.Tests.slnx --disable-parallel -m:1 -p:NuGetAudit=false` — completed. The environment could not reach NuGet's vulnerability service, so audit checks were disabled for this local restore only; package versions and project files were not changed.
- `dotnet build TravelTracker.Tests.slnx --configuration Release --no-restore --verbosity minimal -m:1` — passed, 0 warnings and 0 errors.
- `dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category!=PostgreSQL" --verbosity minimal -m:1` — passed: 1,076 tests passed, 0 failed, 0 skipped. Projects without tests matching the non-PostgreSQL filter reported no matches.
- Baseline before edits: full build passed with 0 warnings/errors; the same non-PostgreSQL command passed 1,057 tests.
- `git diff --check` — passed.

The PostgreSQL group was not run because this change adds no persistence behavior and relational test infrastructure was not needed. Automated JWT tests use isolated local signing keys and discovery stubs; live Supabase project configuration, deployment TLS, uptime, key rotation and Custom Access Token Hook behavior remain unverified. Compare the Milestone 4 Security PDF when available, because it is absent from this checkout.
