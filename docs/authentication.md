# API bearer authentication (#34)

**Identity: built in.** The Users API stores a password hash per account in its own PostgreSQL database and issues the access tokens; every other API is a resource server that verifies them. Nothing is delegated to an external identity provider (Supabase Auth and Auth0 were removed). The Users service stays the owner of TravelTracker profiles, roles and account standing.

## How a token is made

- `POST /api/v1/auth/register` and `POST /api/v1/auth/login` (anonymous, HTTPS only) take `{ "username", "password" }`. A username is 3 to 32 ASCII letters, digits, `.`, `_` or `-`, is compared case-insensitively, and is fixed at registration. Passwords are at least 6 and at most 128 characters, with no other rules and are hashed with ASP.NET Core's `PasswordHasher` (PBKDF2); only the hash is stored (`UserAccounts.PasswordHash`). Login compares against a dummy hash when the username is unknown, so a miss costs the same as a wrong password and both answer `401`. A banned or deactivated account answers `403`.
- A token is an ES256 JWT with `iss` = the configured issuer, `aud` = the configured audience, `sub` = the account's public ID, `scope` = the traveller scopes (`catalogue.read catalogue.write users.read users.write users.admin settings.read settings.write trips.read trips.write`) and a lifetime of `Authentication:Issuer:LifetimeMinutes` (default 60). `users.admin` is only a gate to the Users routes, which recheck live administrator standing in the database.
- `POST /api/v1/auth/service-token` takes the operator secret `UsersAccess:ServiceToken` (at least 32 characters) as its bearer and returns a token whose subject is `traveltracker-service` and whose only scope is `users.access`, valid for `Authentication:Issuer:ServiceLifetimeMinutes` (default 60). The other backends send it to the Users internal routes for account-standing lookups.
- The system holds no email addresses. There is no confirmation, password reset or refresh token. A session ends when its token does.
- A signed-in password change is `POST /api/v1/users/me/password` with `{ "currentPassword", "newPassword" }`, requires `users.write`, and is limited to the caller's own credential: the lookup uses the validated issuer and subject from the token, never a value from the request. The current password must match (a mismatch answers `400` on `errors.currentPassword`, and nothing is written). The new password follows the same 6 to 128 character rule as registration, enforced by `PasswordCredentials` for both paths. On success the hash is replaced and the route answers `204`. The frontend reports success only after that `204`.
- Session decision: a password change does **not** sign the caller out and does **not** revoke tokens. Tokens are stateless, so an already issued token keeps working until its `LifetimeMinutes` runs out, including one issued before the change. The frontend keeps the session and tells the user it stayed signed in. Revoking earlier tokens would need a server-side revocation store, which this service does not have; that is a known residual risk for a stolen token.

## Nothing is stored in application code

The private signing key is read from configuration (`Authentication:Issuer:PrivateKey`, a PEM ECDSA P-256 key) and never written anywhere by TravelTracker. Accounts, hashes, roles and audit rows are PostgreSQL data. The frontend's only state is the in-memory session registry, which a restart clears.

## Configuration and startup

Every API host reads the same settings (environment variables use `__` for `:`):

| Setting | Meaning |
| --- | --- |
| `Authentication:Bearer:PublicKey` | PEM public key (`-----BEGIN PUBLIC KEY-----`) that verifies signatures. Not secret. The Users API may leave it blank and derives it from its private key. |
| `Authentication:Bearer:Issuer` | Exact `iss` the tokens must carry. The launcher uses the Users origin. |
| `Authentication:Bearer:Audience` | The `aud` this API accepts. |
| `Authentication:Bearer:ScopeClaimType` | Claim that carries scopes, `scope`. |
| `Authentication:Bearer:ClockSkewSeconds` | 0 to 120, default 30. |
| `Authentication:Bearer:ValidAlgorithms` | Allowed asymmetric algorithms, default `["ES256"]`. |
| `Authentication:Issuer:PrivateKey` | Users API only. PEM ES256 private key. |

Generate a key pair with OpenSSL:

```sh
openssl ecparam -name prime256v1 -genkey -noout | tee private.pem | openssl ec -pubout > public.pem
```

`scripts/StartDev.cs` generates a throwaway pair in memory on every launch and hands the halves to the right hosts. Missing or unparsable `PublicKey`, a blank issuer or audience, an out-of-range clock skew and an unsupported algorithm all fail startup in every environment. The checked-in `appsettings.Development.json` files carry a placeholder public key with no matching private key, so Development starts with working health and Swagger while every token is rejected until real settings are supplied.

Without `Authentication:Issuer:PrivateKey` the sign-in endpoints answer `503` and log why. Signature, exact issuer, audience, expiration and not-before are checked; tokens must be signed and carry an expiration, and only explicitly allowed algorithms validate (symmetric algorithms and `none` fail startup). Client responses omit validation internals (`IncludeErrorDetails=false`); logs carry failure categories without token values. Never put a private key, real token or password in committed settings or HTTP examples.

`ScopeClaimType` selects one exact claim name. Matching is ordinal and case-sensitive: `catalogue.read.extra`, comma-separated values and roles never imply `catalogue.read`. A missing or malformed scope claim grants nothing; protected operations return `403` when the required grant is absent.

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
| POST `/api/v1/users/me/password` | `users.write` |
| GET `/api/v1/users` and PATCH `/api/v1/users/{id}/role` | `users.admin` plus live active-administrator authorization |
| Users bootstrap/admin/internal and legacy service-access routes | The route-specific policy and identity rules in [the API contract](api.md#users-contracts); the legacy `/api/v1/users/access` service credential remains separate |

Read and write are independent. A write-only token cannot subsequently GET the created Location without read permission. Missing/invalid tokens yield `401` and a Bearer challenge; valid tokens lacking a required scope yield `403`, before model binding or mutation. Existing validation and not-found semantics apply after permission checks. Both the fallback policy (no attribute) and default policy (bare `[Authorize]`) require `catalogue.read`. Authors must still add explicit write policies to mutations; the read default does not infer permissions from HTTP verbs. Deliberately public endpoints require `[AllowAnonymous]`. Swagger names the read permission for implicit policies and correctly lists both `401` and `403`.

Development Swagger has an **Authorize** control for an access token and documents each operation's permission plus `401`/`403`. Swagger itself is available only in Development; protect or disable it in deployed environments. Send API tokens only over HTTPS outside local development.

## Identity and boundaries

Inbound claim mapping is disabled and `User.Identity.Name` is the validated `sub`. Each accepted user token must contain exactly one nonblank `sub`; The issuer must match the configured issuer. Use the validated `(iss, sub)` pair for Users mapping; for password accounts `sub` is the account's public ID. Request headers, query values and bodies cannot replace this identity. No production identity-debug endpoint is exposed.

Bearer validation establishes only the caller identity and token permissions. Each owning service separately enforces account-standing and resource-ownership rules where its contract requires them. A token with write permission authorizes only the operations listed for that host; the identity provider must grant permissions only to authorized callers. Do not treat scopes as a substitute for ownership or account-status enforcement. The Users service requires `users.admin` and rechecks live active-administrator standing for account listing and role changes. First-administrator bootstrap instead requires `catalogue.read` plus the one-time operator secret and verified identity described in [the API contract](api.md#users-contracts).

## Adoption recipe

1. Reference `Microsoft.AspNetCore.Authentication.JwtBearer` matching the service's .NET runtime. Copy the focused settings/registration/policy types into the owning API, preserving one-way layer dependencies. Domain and Data do not depend on bearer or HTTP types.
2. Configure the shared public key, issuer and audience described above; the Users API's `TokenIssuer` supplies the scopes.
3. Register `AddApiBearerAuthentication`, then call `UseAuthentication` before `UseAuthorization`. Add explicit policies for the service's agreed scopes (`users.read/write/admin`, `trips.read/write`, `settings.read/write`, or catalogue permissions), independently of roles or standing rules.
4. Annotate each action with its least-privilege policy, explicitly exempt health, and publish the operation-to-policy map in OpenAPI. Add the owning service's required ownership/account checks separately.
5. Run the real bearer matrix below in that service's host. Never copy test signing keys into production.

## Verification and security considerations

The API test hosts sign tokens with isolated test keys and add that key to the production validator's key list, so the real handler, validation settings and policies stay on. `AuthenticationTests` in the Attractions and Transport suites also assert that configuration with a missing or malformed public key, blank issuer or audience, bad clock skew or unsupported algorithms fails startup. `PasswordAuthTests` and `CredentialPersistenceTests` in the Users suite cover registration, login, the uniform refusal for wrong passwords and unknown emails, non-active accounts, the service token and the persisted hash. `PasswordChangeApiTests` covers the signed-in change: success with the session kept, a wrong current password, the length bounds, anonymous and forged bearers, read-only scope, banned accounts, and that one account cannot reach another's hash. These local tests do not prove deployed TLS, key distribution or the configured key's confidentiality.

The production JWT handler, validation settings, token-validated event and policy registration stay on in every API test host. The six host suites exercise valid signed tokens and missing/malformed/unsigned tokens, invalid signatures, expired/not-yet-valid/missing-expiry tokens, wrong issuer/audience, missing/blank subject, and the host's scope behavior. The Attractions and Transport suites additionally cover configured clock tolerance, algorithm allowlisting, alternate scope claim names, repeated claims, all their route policies, identity header/query spoofing, and configuration validation. The Accommodations, Settings, Trips and Users suites verify rejected tokens stop before dependency lookups, account provisioning or mutations where applicable. The response contract is checked for a bearer challenge without echoed token material. Anonymous health is exercised with no credential and malformed bearer credentials. Automated tests use local keys and discovery stubs; they do not require Supabase credentials or verify live project settings, configured signing-key algorithms, hook deployment, or role/scope grants.

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

The CI-wide verification is `dotnet build TravelTracker.Tests.slnx --configuration Release` followed by `dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"`. The implementation record in [M4-01 plan](milestone-4/M4-01-plan.md) lists the exact run results. The tests verify local JWT handler behavior and HTTP contracts, not deployed TLS or key rotation.

Assets are catalogue integrity and the trusted caller identity. The trust boundary is the bearer token received over HTTP; the configured public key establishes identity. Forged or replayed-to-another-audience tokens are rejected by validation; exact scopes prevent accidental privilege inheritance. Residual risks include compromised issuer keys/tokens, every API sharing one public key (so any holder of the Users private key can mint any token), token replay before expiry, no rate limit on the sign-in endpoints beyond the frontend's, no key rotation, and the still-unimplemented standing/ownership rules. Keep deployment configuration trusted, the private key on the Users host only, token lifetimes appropriate, and never log bearer values.

Framework reference: [Microsoft's .NET 10 JWT bearer configuration guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0).
