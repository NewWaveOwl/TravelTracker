# API bearer authentication (#34)

**Selected identity provider: Supabase Auth** (owner decision, 2026-11-12; replaces Auth0). Supabase Auth issues user access-token JWTs; each API remains a resource server and validates those tokens against the project's issuer, authenticated audience, signing keys and required scopes. The client sends the Supabase Auth session's `access_token` as the API Bearer token. Do not send a Supabase API key or service-role credential as a user's bearer identity. See [Supabase configuration and account cutover](supabase-auth.md), the official [Supabase JWT guide](https://supabase.com/docs/guides/auth/jwts), [JWT claims reference](https://supabase.com/docs/guides/auth/jwt-fields) and [Custom Access Token Hook guide](https://supabase.com/docs/guides/auth/auth-hooks/custom-access-token-hook). Auth0 setup details remain in [the historical frontend guide](frontend-auth0.md) for compatibility and past verification context.

Supabase Auth handles credentials and authentication; the Users service remains responsible for TravelTracker profiles, roles and account standing. Supabase Auth's Custom Access Token Hook must issue the signed `scope` claim used by the APIs, containing only the trusted, space-separated permissions granted to that user. Do not derive permissions from user-editable metadata. Provider selection does not replace the Users account-standing or resource-ownership checks. Public API keys do not grant administrator authority, and Supabase secret/service-role keys must never be used as traveller tokens. The frontend keeps refresh/session credentials server-side and sends only the current user access token to protected API calls.

All six API hosts—Accommodations, Attractions, Settings, Transport, Trips and Users—validate access tokens with the shared ASP.NET Core JWT bearer handler in `TravelTracker.Security`. They do not issue tokens or implement sign-in. User tokens use Supabase Auth's `authenticated` audience; each host enforces its own explicit endpoint scopes and service-specific account/ownership checks.

## Configuration and startup

For the selected provider use `Authentication:Bearer:Provider=Supabase`, with Authority and Issuer set to `https://PROJECT.supabase.co/auth/v1`, Audience `authenticated`, and allowed asymmetric algorithms matching the project (`ES256` or `RS256`). Supabase signing keys come directly from the issuer's `/.well-known/jwks.json`. The signed `scope` claim is produced by the access-token hook described in [Supabase configuration and account cutover](supabase-auth.md); without that hook, tokens do not gain TravelTracker feature permissions.

The generic OIDC configuration below is retained for explicit Auth0 compatibility and existing resource-server tests. It is not the Supabase launcher setup.

After configuring `ConnectionStrings__Transport` and migrations ([Transport startup](transport-service.md)), `dotnet run --project src/TravelTracker.Transport.Api --launch-profile http` starts in Development using placeholder identity settings in `appsettings.Development.json`. Health and Swagger work immediately and anonymous catalogue requests return `401`. These placeholders do not provide a running identity provider or valid access tokens. There are no signing keys or authentication bypasses in Development defaults. The guided local Supabase launcher configures the provider settings for all API hosts; see [Supabase configuration and account cutover](supabase-auth.md).

To use protected operations in the generic OIDC compatibility mode, set these deployment-owned values before starting the API (PowerShell example; use the selected provider's actual values):

```powershell
$env:Authentication__Bearer__Provider = 'Oidc'
$env:Authentication__Bearer__Authority = 'https://identity.example.invalid/tenant'
$env:Authentication__Bearer__Issuer = 'https://identity.example.invalid/tenant'
$env:Authentication__Bearer__Audience = 'traveltracker-api'
$env:Authentication__Bearer__ScopeClaimType = 'scope'
dotnet run --project src/TravelTracker.Transport.Api --launch-profile http
```

The generic OIDC example values are documentation placeholders, not a running identity service. In `Oidc` mode, Authority is the HTTPS OIDC discovery base address, Issuer is the exact expected `iss`, and Audience is this API's resource identifier. For Supabase, set `Provider` to `Supabase`, Authority and Issuer to the exact project issuer (`https://<project-ref>.supabase.co/auth/v1`), and Audience to `authenticated`; these are identifiers, not secrets. Configure each environment through environment variables or its own configuration source. Production settings remain external. Missing values, non-HTTPS authority/issuer, and blank scope claim names fail startup in every environment. Do not put credentials, real tokens, private keys or client secrets in committed settings or HTTP examples. Neither mode requires an API client secret; Supabase resource servers do not use a service-role key.

Supabase signing keys are loaded from its direct JWKS endpoint; in generic OIDC mode they come from discovery metadata/JWKS. Both use framework key refresh. Supabase Auth must use an asymmetric JWT signing key whose algorithm is included in `ValidAlgorithms`; the shipped default is `RS256`, so set the configured allowlist to the project's actual asymmetric algorithm if it differs (for example `ES256`). Do not enable symmetric `HS256` validation or copy a signing secret into an API. Signature, exact issuer, audience, expiration and not-before are checked; tokens must be signed and contain an expiration. Authentication runs before authorization. Never disable these checks or HTTPS key retrieval to make a failing deployment work.

`ClockSkewSeconds` defaults to 30, allowing a small, bounded tolerance for issuer/API clock drift around expiration and not-before. It accepts 0 through 120; values outside this range fail startup. Override with `Authentication__Bearer__ClockSkewSeconds`. Expired tokens beyond the configured allowance still return `401`; keep deployment clocks synchronized.

`ValidAlgorithms` defaults to `["RS256"]`. Only explicitly allowed algorithms may validate a signature, even with a trusted signing key. Supported configured choices are RS256/384/512, PS256/384/512 and ES256/384/512; empty lists, unknown algorithms, symmetric algorithms and `none` fail startup. Choose only what the provider actually issues. Override entries with `Authentication__Bearer__ValidAlgorithms__0` (and subsequent array indices if needed).

In Supabase mode, the signing-key retriever pins keys to the configured project issuer and reads only its HTTPS JWKS endpoint. Legacy `Provider=Oidc` compatibility mode uses OIDC discovery metadata and verifies its issuer against configured `Issuer`; it remains available for the historical Auth0 setup and generic resource-server tests, but is not used by the Supabase launcher. Client responses omit validation internals (`IncludeErrorDetails=false`). Server logs emit safe failure categories without token values or exception text. A failed signing-key retrieval fails closed; request-aborted cancellation propagates normally. The configuration manager caches keys and refreshes them as needed.

Supabase's default user access token does not grant TravelTracker API scopes. Configure the Supabase Auth **Custom Access Token Hook** to add a top-level `scope` claim with the exact, space-separated user grants needed by the app (`catalogue.read`, `catalogue.write`, `users.read`, `users.write`, `settings.read`, `settings.write`, `trips.read`, and `trips.write`). The hook must source grants from trusted server-controlled authorization data, not `user_metadata`, request fields, or client-supplied values. Never grant `users.access` through this user-token hook; that permission belongs to the separate allowlisted service credential. `ScopeClaimType` selects one exact claim name (`scope` here). Matching is ordinal and case-sensitive: `catalogue.read.extra`, comma-separated values and roles never imply `catalogue.read`. A missing or malformed scope claim does not authenticate extra privileges; protected operations return `403` when the required grant is absent.

## Implemented policy map

| Operation | Required permission |
| --- | --- |
| GET `/health` | None; explicitly anonymous, including with an invalid bearer token |
| GET `/api/v1/accommodations` and `/api/v1/accommodations/{id}` | `catalogue.read` |
| POST `/api/v1/accommodations` | `catalogue.write` |
| GET `/api/v1/attractions` | `catalogue.read` |
| GET `/api/v1/attractions/{id}` | `catalogue.read` |
| POST `/api/v1/attractions` | `catalogue.write` |
| PATCH `/api/v1/attractions/{id}` | `catalogue.write` |
| GET `/api/v1/transport-modes` and `/api/v1/transport-modes/{id}` | `catalogue.read` |
| POST `/api/v1/transport-modes` | `catalogue.write` |
| GET `/api/v1/route-points` and `/api/v1/route-points/{id}` | `catalogue.read` |
| POST `/api/v1/route-points` | `catalogue.write` |
| GET `/api/v1/settings` | `settings.read` |
| PUT `/api/v1/settings` | `settings.write` |
| GET `/api/v1/trips` and `/api/v1/trips/{id}` | `trips.read` |
| POST `/api/v1/trips` | `trips.write` and `catalogue.read` |
| PUT `/api/v1/trips/{id}` | `trips.write` and `catalogue.read` |
| GET `/api/v1/users/me` | `users.read` |
| PATCH `/api/v1/users/me` | `users.write` |
| Users bootstrap/admin/internal and legacy service-access routes | The route-specific policy and identity rules in [the API contract](api.md#users-contracts); the legacy `/api/v1/users/access` service credential remains separate |

Read and write are independent. A write-only token cannot subsequently GET the created Location without read permission. Missing/invalid tokens yield `401` and a Bearer challenge; valid tokens lacking a required scope yield `403`, before model binding or mutation. Existing validation and not-found semantics apply after permission checks. Both the fallback policy (no attribute) and default policy (bare `[Authorize]`) require `catalogue.read`. Authors must still add explicit write policies to mutations; the read default does not infer permissions from HTTP verbs. Deliberately public endpoints require `[AllowAnonymous]`. Swagger names the read permission for implicit policies and correctly lists both `401` and `403`.

Development Swagger has an **Authorize** control for an access token and documents each operation's permission plus `401`/`403`. Swagger itself is available only in Development; protect or disable it in deployed environments. Send API tokens only over HTTPS outside local development.

## Identity and boundaries

Inbound claim mapping is disabled and `User.Identity.Name` is the validated `sub`. Each accepted user token must contain exactly one nonblank `sub`; Supabase tokens must also have exactly the `authenticated` role and must not identify an anonymous user. The issuer must match the configured project issuer. Use the validated `(iss, sub)` pair for Users mapping: `sub` is not assumed to be a TravelTracker UUID. Request headers, query values and bodies cannot replace this identity. No production identity-debug endpoint is exposed.

Bearer validation establishes only the caller identity and token permissions. Each owning service separately enforces account-standing and resource-ownership rules where its contract requires them. A token with write permission authorizes only the operations listed for that host; the identity provider must grant permissions only to authorized callers. Do not treat scopes as a substitute for ownership or account-status enforcement.

## Adoption recipe

1. Reference `Microsoft.AspNetCore.Authentication.JwtBearer` matching the service's .NET runtime. Copy the focused settings/registration/policy types into the owning API, preserving one-way layer dependencies. Domain and Data do not depend on bearer or HTTP types.
2. For Supabase, bind the project's HTTPS Auth issuer as both Authority and Issuer, validate user tokens against audience `authenticated`, and retrieve keys from its direct JWKS endpoint. For legacy OIDC/Auth0 compatibility, use its discovery base and exact issuer. Configure the Supabase Custom Access Token Hook to provide trusted API scopes.
3. Register `AddApiBearerAuthentication`, then call `UseAuthentication` before `UseAuthorization`. Add explicit policies for the service's agreed scopes (`users.read/write/admin`, `trips.read/write`, `settings.read/write`, or catalogue permissions), independently of roles or standing rules.
4. Annotate each action with its least-privilege policy, explicitly exempt health, and publish the operation-to-policy map in OpenAPI. Add the owning service's required ownership/account checks separately.
5. Run the real bearer matrix below in that service's host. Test the selected provider's actual signing-key endpoint and token format in its integration environment before deployment. Never copy test signing keys or test metadata overrides into production.

## Verification and security considerations

The Attractions and Transport `AuthenticationTests` use isolated RSA keys and an HTTP backchannel stub serving public-only OIDC discovery/JWKS JSON. The framework's real configuration manager follows Authority to `.well-known/openid-configuration` and then `jwks_uri`; it is not replaced with static configuration. Tests assert both requested addresses, valid discovery, metadata/issuer mismatch rejection and a legitimate differing authority/issuer pair. `SupabaseAuthenticationTests` separately exercises Supabase's direct JWKS endpoint, issuer/audience/signature/lifetime, subject, role, anonymous-user and scope checks through the real handler. These local tests do not prove a live provider's TLS, availability, key rotation, hook deployment or configured grants.

The production JWT handler, validation settings, token-validated event and policy registration stay on in every API test host. The six host suites exercise valid signed tokens and missing/malformed/unsigned tokens, invalid signatures, expired/not-yet-valid/missing-expiry tokens, wrong issuer/audience, missing/blank subject, and the host's scope behavior. The Attractions and Transport suites additionally cover configured clock tolerance, algorithm allowlisting, alternate scope claim names, repeated claims, all their route policies, identity header/query spoofing, and discovery metadata behavior. The Accommodations, Settings, Trips and Users suites verify rejected tokens stop before dependency lookups, account provisioning or mutations where applicable. The response contract is checked for a bearer challenge without echoed token material. Anonymous health is exercised with no credential and malformed bearer credentials. Automated tests use local keys and discovery stubs; they do not require Supabase credentials or verify live project settings, configured signing-key algorithms, hook deployment, or role/scope grants.

### M4-01 reproducible verification

Run from the repository root after restoring the solution packages:

```powershell
dotnet test tests/TravelTracker.Accommodations.Api.Tests --configuration Release --no-restore --filter "Category!=PostgreSQL"
dotnet test tests/TravelTracker.Attractions.Api.Tests --configuration Release --no-restore --filter "Category!=PostgreSQL"
dotnet test tests/TravelTracker.Settings.Api.Tests --configuration Release --no-restore --filter "Category!=PostgreSQL"
dotnet test tests/TravelTracker.Transport.Api.Tests --configuration Release --no-restore --filter "Category!=PostgreSQL"
dotnet test tests/TravelTracker.Trips.Api.Tests --configuration Release --no-restore --filter "Category!=PostgreSQL"
dotnet test tests/TravelTracker.Users.Tests --configuration Release --no-restore --filter "Category!=PostgreSQL"
```

The CI-wide verification is `dotnet build TravelTracker.Tests.slnx --configuration Release` followed by `dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"`. The implementation record in [M4-01 plan](milestone-4/M4-01-plan.md) lists the exact run results. The tests verify local JWT handler behavior and HTTP contracts, not live Supabase project configuration, deployed TLS, provider uptime, or key rotation.

Assets are catalogue integrity and the trusted caller identity. The trust boundary is the bearer token received over HTTP; trusted provider configuration and the issuer's signing keys establish identity. Forged or replayed-to-another-audience tokens are rejected by validation; exact scopes prevent accidental privilege inheritance. Residual risks include compromised issuer keys/tokens, overly broad provider grants, token replay before expiry, provider availability during discovery or JWKS retrieval, and the still-unimplemented standing/ownership rules. Keep deployment configuration trusted, keys at the provider, token lifetimes appropriate, and never log bearer values.

Framework reference: [Microsoft's .NET 10 JWT bearer configuration guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0).
