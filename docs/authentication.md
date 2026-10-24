# API bearer authentication (#34)

The existing `TravelTracker.Api` is the representative resource server. It validates access tokens with ASP.NET Core's real JWT bearer handler. It does not issue tokens or implement sign-in. Other service issues must adopt this pattern and independently verify their endpoints.

## Configuration and startup

Set these deployment-owned values before starting the API (PowerShell example; substitute your selected provider's actual values):

```powershell
$env:Authentication__Bearer__Authority = 'https://identity.example.invalid/tenant'
$env:Authentication__Bearer__Issuer = 'https://identity.example.invalid/tenant'
$env:Authentication__Bearer__Audience = 'traveltracker-api'
$env:Authentication__Bearer__ScopeClaimType = 'scope'
dotnet run --project src/TravelTracker.Api --launch-profile http
```

The addresses above are documentation placeholders, not a running identity service. Configure each environment through environment variables or its own configuration source. Authority is the HTTPS OIDC discovery base address, Issuer is the exact expected `iss`, and Audience is this API's resource identifier. They are identifiers, not secrets. Missing values, non-HTTPS authority/issuer, and blank scope claim names fail startup. Do not put credentials, real tokens, private keys or client secrets in committed settings or HTTP examples. The resource server needs no client secret.

Signing keys come from the authority's discovery metadata/JWKS, including framework key refresh. Signature, exact issuer, audience, expiration and not-before are checked; tokens must be signed and contain an expiration. Clock skew is zero, so deployment clocks must be synchronized. Never disable these checks or HTTPS discovery to make a failing deployment work. Authentication runs before authorization. Error responses omit validation internals.

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

Read and write are independent. A write-only token cannot subsequently GET the created Location without read permission. Missing/invalid tokens yield `401` and a Bearer challenge; valid tokens lacking a required scope yield `403`, before model binding or mutation. Existing validation and not-found semantics apply after permission checks. A fallback policy requires authentication for future endpoints, but authors must still add the appropriate named scope policy. Deliberately public endpoints require `[AllowAnonymous]`.

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

`AuthenticationTests` uses isolated RSA keys and public-only static discovery metadata, while retaining the production JWT handler, validation settings, token-validated event and policy registration. It covers missing/malformed/unsigned tokens, invalid signatures, expired/not-yet-valid/missing-expiry tokens, wrong issuer/audience, missing/blank subject, absent/wrong/case-varied/prefix scopes, alternate configured claim names, repeated claims, every implemented action's access policy, anonymous health, and rejected writes leaving no created attraction. A test-only controller proves the validated identity resists a separate identity header/query. Existing resource tests now use real signed tokens too. No external provider or credentials are needed for tests.

Assets are catalogue integrity and the trusted caller identity. The trust boundary is the bearer token received over HTTP; trusted discovery configuration and the issuer's signing keys establish identity. Forged or replayed-to-another-audience tokens are rejected by validation; exact scopes prevent accidental privilege inheritance. Residual risks include compromised issuer keys/tokens, overly broad provider grants, token replay before expiry, provider availability during discovery, and the still-unimplemented standing/ownership rules. Keep deployment configuration trusted, keys at the provider, token lifetimes appropriate, and never log bearer values.

Framework reference: [Microsoft's .NET 10 JWT bearer configuration guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0).
