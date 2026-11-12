# M4-01 bearer-token validation plan

**Issue:** M4-01 — verify and harden bearer-token validation across the protected API hosts.
**Branch:** `bearer-token`
**Research baseline:** committed tree at `15b4a40`.
**Status:** research and implementation plan only; no production behavior has been changed.

## Goal and scope

Verify that every protected API host uses the same production JWT bearer validation configured for Auth0, trusts only validated issuer and subject claims, and rejects invalid credentials consistently. Close coverage gaps with deterministic, signed-token HTTP tests and record the endpoint/identity contract and reproducible results.

In scope: the shared `TravelTracker.Security` bearer implementation, its adoption by all six API hosts, the identity and permission checks at their protected HTTP boundaries, host-specific real-bearer tests, and the authentication/API documentation.

Out of scope: changing identity providers, issuing tokens, frontend OIDC/sign-in behavior, API passwords, authorization policy redesign, Users standing or ownership rules beyond proving that the authenticated identity cannot be spoofed, and PostgreSQL/Supabase schema or connection changes. The legacy Users `/api/v1/users/access` endpoint uses a separate service credential; preserve and document that distinct contract rather than converting it to an Auth0 user token as part of this issue.

## Ground truth

Sources: the issue supplied in chat; [API authentication contract](../authentication.md); [API contracts](../api.md); [design account/access rules](../design.md#3-accounts-and-access); [domain identity boundary](../domain.md); and [data boundaries](../data.md). Auth0 remains the identity provider. Supabase remains the PostgreSQL host and is not part of this change.

The Milestone 4 Security PDF named by the issue was not found in the repository checkout. This plan follows the issue text and current repository contracts; if the PDF contains additional requirements, compare them before implementation.

### Implemented at the research baseline

- `TravelTracker.Security` registers the real ASP.NET Core JWT bearer handler, requires signed tokens, validates signing key, exact configured issuer, audience and lifetime, requires expiration, honors bounded `nbf`/expiry clock skew, allowlists asymmetric algorithms, disables inbound claim mapping, and requires one nonblank `sub` after validation. It suppresses authentication details in responses and logs failure categories without token text.
- All six runnable API hosts reference the shared security project and call `AddApiBearerAuthentication`; each configures authentication before authorization. `TravelTracker.Api` is a library, not a seventh API host. The Blazor frontend is an OIDC client, not an API resource server.
- The existing host test projects already contain real signed-token fixtures and bearer tests, but coverage differs by host. The representative authentication guide describes broad validation evidence; this issue will verify the same required matrix through every host rather than assuming shared registration alone proves all host routes.
- Existing endpoint contracts deliberately distinguish `401` for missing/invalid bearer credentials from `403` for a valid caller lacking the exact required permission. Health endpoints are anonymous.

## Identity and HTTP contract to preserve

- A protected user request is authenticated only by a JWT accepted by the production bearer handler using trusted Auth0 discovery/JWKS configuration. The expected issuer is an exact match, the resource audience is the configured `traveltracker-api`, and signature, `exp`, `nbf`, and permitted signing algorithm are validated.
- Application identity is the validated `(iss, sub)` pair. `sub` is not a TravelTracker public UUID. A missing, blank, or ambiguous subject fails authentication. Headers, query parameters, and request DTO fields do not replace it; owner/account identifiers are derived from validated claims or rejected as unknown input.
- A missing, malformed, unsigned, expired, not-yet-valid, wrong-issuer, wrong-audience, or wrong-signature token receives `401` and a Bearer challenge. A valid token without the route's explicit scope receives `403`. Authentication failure responses and logs do not reveal the token, signing key, or validation exception details.
- `GET /health` stays anonymous for all six API hosts, including requests that include an invalid bearer token.
- Read/write scopes remain independent and service-specific. Catalogue, Settings, Trips and Users policies remain as recorded in `docs/api.md`; token validation must run before endpoint work, provisioning, ownership lookup, or mutation.
- The separate Users service-to-service access credential remains distinct from Auth0 JWT validation and is not interchangeable with a user access token.

## Host and endpoint inventory

| Host | Existing protected endpoint groups to exercise | Expected permission |
| --- | --- | --- |
| Accommodations | `GET /api/v1/accommodations` and `/{id}`; `POST /api/v1/accommodations` | `catalogue.read`; `catalogue.write` |
| Attractions | `GET /api/v1/attractions` and `/{id}`; `POST` and `PATCH` | `catalogue.read`; `catalogue.write` |
| Transport | transport-mode and route-point collection/detail `GET`; their `POST` routes | `catalogue.read`; `catalogue.write` |
| Settings | own-settings `GET` and `PUT` | `settings.read`; `settings.write` |
| Trips | own-trip detail `GET`; trip `POST` | `trips.read`; `trips.write` plus the existing `catalogue.read` dependency permission |
| Users | self-profile `GET`/`PATCH`, bootstrap/admin routes, and internal identity/access routes | Each existing policy and allowlisted service-subject rule documented in `docs/api.md`; preserve the separate legacy service-token endpoint |

The tests should exercise at least one real protected route per host for authentication, then cover every protected operation's existing authorization policy with the relevant host's scope matrix. Do not treat a valid token that lacks permission as an authentication failure.

## File-by-file implementation plan

| File or area | Planned work |
| --- | --- |
| `src/TravelTracker.Security/Authentication/BearerAuthenticationExtensions.cs` | Audit the production handler options, metadata/issuer check, validated subject check, failure response and safe logging against the contract. Change only a demonstrated validation gap; do not weaken validation to accommodate a test or provider misconfiguration. |
| `src/TravelTracker.Security/Authentication/BearerSettings.cs` | Confirm configuration defaults and bounds match deployed Auth0 settings and current documentation. Change only if the matrix identifies a real gap. |
| `src/TravelTracker.Security/Authentication/ScopePolicies.cs` | Confirm exact, case-sensitive scope parsing stays downstream of authenticated principal validation. Do not rename or broaden scopes in this issue. |
| `src/TravelTracker.Accommodations.Api/Program.cs`, `src/TravelTracker.Attractions.Api/Program.cs`, `src/TravelTracker.Settings.Api/Program.cs`, `src/TravelTracker.Transport.Api/TransportProgram.cs`, `src/TravelTracker.Trips.Api/Program.cs`, `src/TravelTracker.Users.Api/Program.cs` | Verify every host registers the shared handler and runs authentication before authorization. Keep health explicitly anonymous. Edit only a host that fails the audit. |
| `src/TravelTracker.*.Api/appsettings*.json` and host launch settings | Verify development placeholders are nonfunctional, production settings remain externally configured, and every host uses the intended issuer/audience without secrets. No checked-in token or key material. Edit only if inconsistent. |
| `tests/TravelTracker.Accommodations.Api.Tests/AuthenticationTests.cs` and `Support/{ApiFactory,DiscoveryBackchannelHandler}.cs` | Complete missing/invalid/valid bearer cases through the Accommodations host and verify no create or ownership side effect occurs for a rejected token. |
| `tests/TravelTracker.Attractions.Api.Tests/AuthenticationTests.cs` and `Support/{ApiFactory,DiscoveryBackchannelHandler,AuthenticationLogCapture,IdentityProbeController}.cs` | Retain its existing broad real-bearer coverage; close any route/identity/error-leak gap found during cross-host comparison. Keep identity probes test-only. |
| `tests/TravelTracker.Transport.Api.Tests/AuthenticationTests.cs` and `Support/{ApiFactory,DiscoveryBackchannelHandler,AuthenticationLogCapture,IdentityProbeController}.cs` | Retain existing broad matrix; verify protected collection/detail/create routes and that identity headers/query values cannot replace validated claims. |
| `tests/TravelTracker.Settings.Api.Tests/AuthenticationTests.cs` and `Support/{ApiFactory,DiscoveryBackchannelHandler,FallbackProbeController}.cs` | Complete the real-bearer matrix for settings routes; prove rejected tokens do not reach Users-standing lookup or read/write behavior. |
| `tests/TravelTracker.Trips.Api.Tests/AuthenticationTests.cs` and `Support/{TripsApiFactory,DiscoveryBackchannelHandler}.cs` | Complete the matrix for trip detail/create; prove invalid credentials do not trigger place/transport lookups or writes and caller ownership derives from validated claims. |
| `tests/TravelTracker.Users.Tests/UserProfileScopeMatrixTests.cs`, `UsersStartupTests.cs`, and `Support/{UsersApiFactory,UsersLogCapture}.cs` | Complete the matrix for Users self and internal routes, preserving scope and allowlisted-service-subject checks. Verify no rejected request provisions an account and errors/logs omit token contents. |
| `TravelTracker.Tests.slnx` and `.github/workflows/ci.yml` | Verify all six host test projects already run in CI's non-PostgreSQL group. Add project/workflow wiring only if a required host suite is absent. No PostgreSQL migration or test category is needed for this auth-only scope. |
| `docs/authentication.md` and `docs/api.md` | Record the six-host bearer/health/permission map, validated `(iss, sub)` identity rules, safe failure behavior, the Users service-token distinction, and exact reproducible test commands/results. Avoid claiming a live Auth0 test when tests use local test signing keys and discovery stubs. |
| `docs/milestone-4/M4-01-plan.md` | Keep this approved plan, then append implementation files changed, final test/build results, any uncovered host gap, and remaining live-provider limitations after implementation. |

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

The PostgreSQL group is not directly implicated, but run the same CI matrix where an isolated Supabase-compatible PostgreSQL test instance is available; do not point tests at the shared or production Supabase database. `scripts/smoke-hosts.sh` can confirm that each host starts independently, but it does not replace the signed-token tests. Record exact discovered/pass/fail/skip counts, build warning/error counts, commands, and any PostgreSQL infrastructure limitation. If Auth0 tenant configuration is available, a separately documented manual smoke test may confirm discovery and one valid scoped request without saving tokens or credentials; deterministic local tests remain the automated regression gate.

## Risks and open points

- Existing source files and documentation vary in completeness; test each host instead of inferring safety from its reference to `TravelTracker.Security`.
- The Auth0 tenant's exact issuer (including trailing slash), audience, signing algorithm, and scope claim must match deployment configuration. A discovery or tenant grant problem must not be “fixed” by relaxing issuer/audience/signature validation.
- Tests with a local discovery stub verify JWT handler behavior and the wire contract, not Auth0 uptime, TLS, real tenant key rotation, or deployed secret configuration.
- The Milestone 4 Security PDF is unavailable in this repository snapshot. Confirm whether it includes other explicit controls before implementation.
