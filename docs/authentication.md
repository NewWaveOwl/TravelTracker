# API bearer authentication (#34)

The existing `TravelTracker.Api` is the representative resource server. It validates access tokens with ASP.NET Core's real JWT bearer handler. It does not issue tokens or implement sign-in. Other service issues must adopt this pattern and independently verify their endpoints.

## Configuration and startup

Plain `dotnet run --project src/TravelTracker.Api --launch-profile http` starts in Development using the placeholder identity settings in `appsettings.Development.json`. Health and Swagger work immediately and anonymous catalogue requests return `401`. These placeholders do not provide a running identity provider or valid access tokens. There are no signing keys or authentication bypasses in Development defaults.

To use protected operations, set these deployment-owned values before starting the API (PowerShell example; substitute your selected provider's actual values):

```powershell
$env:Authentication__Bearer__Authority = 'https://identity.example.invalid/tenant'
$env:Authentication__Bearer__Issuer = 'https://identity.example.invalid/tenant'
$env:Authentication__Bearer__Audience = 'traveltracker-api'
$env:Authentication__Bearer__ScopeClaimType = 'scope'
dotnet run --project src/TravelTracker.Api --launch-profile http
```

The addresses above are documentation placeholders, not a running identity service. Configure each environment through environment variables or its own configuration source. Authority is the HTTPS OIDC discovery base address, Issuer is the exact expected `iss`, and Audience is this API's resource identifier. They are identifiers, not secrets. Production settings remain blank and must be supplied. Missing values, non-HTTPS authority/issuer, and blank scope claim names fail startup in every environment. Do not put credentials, real tokens, private keys or client secrets in committed settings or HTTP examples. The resource server needs no client secret.

Signing keys come from the authority's discovery metadata/JWKS, including framework key refresh. Signature, exact issuer, audience, expiration and not-before are checked; tokens must be signed and contain an expiration. Authentication runs before authorization. Never disable these checks or HTTPS discovery to make a failing deployment work.

`ClockSkewSeconds` defaults to 30, allowing a small, bounded tolerance for issuer/API clock drift around expiration and not-before. It accepts 0 through 120; values outside this range fail startup. Override with `Authentication__Bearer__ClockSkewSeconds`. Expired tokens beyond the configured allowance still return `401`; keep deployment clocks synchronized.

`ValidAlgorithms` defaults to `["RS256"]`. Only explicitly allowed algorithms may validate a signature, even with a trusted signing key. Supported configured choices are RS256/384/512, PS256/384/512 and ES256/384/512; empty lists, unknown algorithms, symmetric algorithms and `none` fail startup. Choose only what the provider actually issues. Override entries with `Authentication__Bearer__ValidAlgorithms__0` (and subsequent array indices if needed).

Authority and Issuer are intentionally independent: a legitimate discovery base URL can differ from its published token issuer. Copy the discovery document's exact `issuer` including any trailing slash/version path. Successful validation also requires discovery metadata to match configured Issuer; a mismatch fails closed with `401`. This check happens during discovery/token validation, not by making startup depend on an external provider. Client responses omit validation internals (`IncludeErrorDetails=false`). Server logs emit a safe failure category, or an explicit discovery/configured-issuer mismatch warning, without token values or exception text. A failed metadata retrieval in the post-validation callback gets a specific discovery-retrieval warning and fails closed; request-aborted cancellation propagates normally. The metadata manager caches configuration, so the callback does not force a network fetch on every request.

`ScopeClaimType` selects one exact claim name (`scope` by default, or e.g. `scp` for a provider that uses it). Values use OAuth space-separated scopes or repeated claims/JSON string arrays. Matching is ordinal and case-sensitive: `catalogue.read.extra`, comma-separated values and roles never imply `catalogue.read`. Supporting a provider with a different permission representation requires an explicitly reviewed parser and tests; this change does not select a provider or assert provider compatibility.

## Implemented policy map

| Operation | Required permission |
| --- | --- |
| GET `/health` | None; explicitly anonymous, including with an invalid bearer token |
| GET `/api/v1/attractions` | `catalogue.read` |
| GET `/api/v1/attractions/{id}` | `catalogue.read` |
| POST `/api/v1/attractions` | `catalogue.write` |
| PATCH `/api/v1/attractions/{id}` | `catalogue.write` |
| GET `/api/v1/transport-modes/{id}` | `catalogue.read` |
| POST `/api/v1/transport-modes` | `catalogue.write` |
| GET `/api/v1/route-points/{id}` | `catalogue.read` |
| POST `/api/v1/route-points` | `catalogue.write` |

Read and write are independent. A write-only token cannot subsequently GET the created Location without read permission. Missing/invalid tokens yield `401` and a Bearer challenge; valid tokens lacking a required scope yield `403`, before model binding or mutation. Existing validation and not-found semantics apply after permission checks. Both the fallback policy (no attribute) and default policy (bare `[Authorize]`) require `catalogue.read`. Authors must still add explicit write policies to mutations; the read default does not infer permissions from HTTP verbs. Deliberately public endpoints require `[AllowAnonymous]`. Swagger names the read permission for implicit policies and correctly lists both `401` and `403`.

Development Swagger has an **Authorize** control for an access token and documents each operation's permission plus `401`/`403`. Swagger itself is available only in Development; protect or disable it in deployed environments. Send API tokens only over HTTPS outside local development.

## Identity and boundaries

Inbound claim mapping is disabled and `User.Identity.Name` is the validated `sub`. Missing or blank subjects are rejected. Use the validated `(iss, sub)` pair for future Users mapping: `sub` is not assumed to be a TravelTracker UUID. Request headers, query values and bodies cannot replace this identity. No production identity-debug endpoint is exposed.

This representative catalogue API enforces token permissions only. Account-standing checks, resource ownership, Users mapping, banned/deactivated rules, sign-in, and independent service adoption remain with their related issues. A token with write permission currently authorizes catalogue writes; the identity provider must grant that permission only to authorized callers. Do not treat scopes as completed ownership or account-status enforcement.

## Adoption recipe

1. Reference `Microsoft.AspNetCore.Authentication.JwtBearer` matching the service's .NET runtime. Copy the focused settings/registration/policy types into the owning API, preserving one-way layer dependencies. Domain and Data do not depend on bearer or HTTP types.
2. Bind the owning service's authority, issuer, audience and scope claim name through environment-specific configuration. Use its own resource audience; do not broaden audience validation to accept another service's tokens.
3. Register `AddApiBearerAuthentication`, then call `UseAuthentication` before `UseAuthorization`. Add explicit policies for the service's agreed scopes (`users.read/write/admin`, `trips.read/write`, `settings.read/write`, or catalogue permissions), independently of roles or standing rules.
4. Annotate each action with its least-privilege policy, explicitly exempt health, and publish the operation-to-policy map in OpenAPI. Add the owning service's required ownership/account checks separately.
5. Run the real bearer matrix below in that service's host. Test the selected provider's actual metadata and token format in its integration environment before deployment. Never copy test signing keys or test metadata overrides into production.

## Verification and security considerations

`AuthenticationTests` uses isolated RSA keys and an HTTP backchannel stub serving public-only discovery/JWKS JSON. The framework's real configuration manager follows Authority to `.well-known/openid-configuration` and then `jwks_uri`; it is not replaced with static configuration. Tests assert both requested addresses, valid discovery, metadata/issuer mismatch rejection and a legitimate differing authority/issuer pair. This exercises discovery parsing and key retrieval, but does not prove a live provider's TLS, availability or key rotation behavior.

The production JWT handler, validation settings, token-validated event and policy registration stay on. Coverage includes missing/malformed/unsigned tokens, invalid signatures, expired/not-yet-valid/missing-expiry tokens, configured clock tolerance, algorithm allowlisting, wrong issuer/audience, missing/blank subject, absent/wrong/case-varied/prefix scopes, alternate configured claim names, repeated claims, every implemented action's access policy, implicit default/fallback scopes, anonymous health, and rejected writes leaving no created attraction. A test-only controller proves the validated identity resists a separate identity header/query. Separate tests load the shipped Development settings without ApiFactory overrides, and verify missing Production settings still fail startup. Existing resource tests use real signed tokens too. No external provider or credentials are needed for tests.

Assets are catalogue integrity and the trusted caller identity. The trust boundary is the bearer token received over HTTP; trusted discovery configuration and the issuer's signing keys establish identity. Forged or replayed-to-another-audience tokens are rejected by validation; exact scopes prevent accidental privilege inheritance. Residual risks include compromised issuer keys/tokens, overly broad provider grants, token replay before expiry, provider availability during discovery, and the still-unimplemented standing/ownership rules. Keep deployment configuration trusted, keys at the provider, token lifetimes appropriate, and never log bearer values.

Framework reference: [Microsoft's .NET 10 JWT bearer configuration guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0).
