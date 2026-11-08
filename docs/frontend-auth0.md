# Frontend Auth0 sign-in (#33)

TravelTracker uses **Auth0** as its identity provider (owner decision, 2026-11-03). The Blazor Web App in `src/TravelTracker.Frontend` signs users in with the ASP.NET Core OpenID Connect handler: authorization-code flow with PKCE, callback state and nonce handling, access-token acquisition and hosted logout. TravelTracker never collects provider passwords.

Status of the evidence: hosted login, a protected Transport read and hosted logout were verified against a live Auth0 tenant in the Blazor client on 2026-11-04, including logout updating a second open tab without reloading it. The session invalidation follow-up below addresses the two original review findings; the remaining live failure checklist still applies before treating #33 as fully verified.

## Auth0 dashboard setup

Use manual dashboard setup; Auth0's optional AI-assisted/MCP onboarding is not required.

1. Create an Auth0 tenant and an application named `TravelTracker frontend`.
   - **Regular Web Application** (recommended): the client secret stays on the server, in user-secrets or an environment variable.
   - A **Single Page Web Application** also works with no secret, because PKCE is always on. Leave `Auth0:ClientSecret` empty in that case.
2. Copy the Domain (hostname only) and Client ID from **Settings**.
3. Add the app origins to **Allowed Callback URLs** as `<origin>/signin-oidc`, and to **Allowed Logout URLs** as `<origin>/`. Development uses `http://localhost:5100/signin-oidc` and `http://localhost:5100/`. Register deployed HTTPS origins separately; do not use wildcards.
4. Under **Applications → APIs**, create a custom API named `TravelTracker API`, identifier `traveltracker-api`, signing algorithm **RS256**. Add `catalogue.read` for catalogue reads and `users.read` for the signed-in profile. Grant `users.write` to clients that call the profile edit endpoint. Add `catalogue.write` when attraction writes are needed.
5. Under this API's **Application Access**, select **Edit** for `TravelTracker frontend` and authorize **User-Delegated Access** with `catalogue.read` and `users.read`. Creating the application and API does not link them under per-app access policy. A missing grant causes "Client is not authorized to access resource server." If using RBAC, assign the read permissions to the intended test user through a role. The backend reads the OAuth `scope` claim, not the Auth0 `permissions` array. Successful login alone does not establish API authorization.
6. Store the settings outside git, from `src/TravelTracker.Frontend`:

```powershell
dotnet user-secrets set "Auth0:Domain" "your-tenant-region.auth0.com"
dotnet user-secrets set "Auth0:ClientId" "your-client-id"
dotnet user-secrets set "Auth0:Audience" "traveltracker-api"
dotnet user-secrets set "Auth0:ClientSecret" "only-for-a-regular-web-application"
```

`Auth0:Scope` defaults to `openid profile email catalogue.read users.read`. If you already override this setting, add `users.read` to the configured scopes and sign in again. The account page uses this permission to load the Users profile and confirm administrator standing. Environment variables such as `Auth0__Domain` work too. The audience must exactly equal the API identifier. Restart the app after changing settings. Missing or malformed settings leave navigation usable, log a warning at startup, and show a setup-pending account page.

## Resource servers

Set these for each API process, using the tenant's exact discovery issuer (Auth0 commonly includes a trailing slash):

```powershell
$env:Authentication__Bearer__Authority = 'https://your-tenant-region.auth0.com/'
$env:Authentication__Bearer__Issuer = 'https://your-tenant-region.auth0.com/'
$env:Authentication__Bearer__Audience = 'traveltracker-api'
$env:Authentication__Bearer__ScopeClaimType = 'scope'
```

Verify the issuer from `https://YOUR_DOMAIN/.well-known/openid-configuration`. Preserve signature, expiry, audience and issuer validation. A custom domain must be used consistently. Current catalogue hosts share the `traveltracker-api` audience; future services with different audiences need their own token request configuration and must never reuse a catalogue token blindly.

Attractions also needs PostgreSQL and the independently implemented live Users access endpoint/credential; follow [Attractions setup](attractions-service.md). An Auth0 login does not replace that account-standing dependency. A valid token can still receive `503` when Users is unavailable. Do not bypass Users checks to make the demo pass.

The frontend calls the services from the server over HTTP, so no CORS or proxy setup is needed. Never configure the Auth0 Management API audience as the TravelTracker resource audience.

## Session and trust boundaries

| OAuth role | TravelTracker component |
| --- | --- |
| Resource owner | Traveller signing in |
| Client | Blazor server app; Client ID, optional secret that never leaves the server |
| Authorization server | Selected Auth0 tenant; hosts credential entry and issues tokens |
| Resource servers | ASP.NET APIs; validate access tokens and enforce scopes/standing |

- The Blazor server receives the tokens. The browser only holds an encrypted, HttpOnly, SameSite=Lax session cookie. The access token rides in that cookie as a claim so interactive circuits can read it without an HTTP context. No token is logged or written to local/session storage.
- Each completed login receives a random server-owned session ID. A server-side registry holds active IDs and their expiry, without storing bearer tokens. Cookie validation and protected calls require an active registered session as well as an unexpired access token. The session ends when the access token does; there is no silent renewal.
- Sign-out is a POST with an antiforgery token. It revokes the registered session before clearing the cookie or starting hosted logout. Open circuits for that session receive an authentication-state change and show the sign-in state. Protected calls check session validity before sending and again before returning a result, so a late success from a revoked or replaced session is discarded. Provider logout failure does not restore the local session.
- An API `401` revokes the session that sent the rejected request. Navigation, refreshing the page, reopening a tab or replaying its cookie cannot restore it; a new sign-in creates a new session. A successful replacement sign-in revokes the previous cookie session so existing tabs cannot keep using the earlier login. A late `401` from an old request does not revoke a newer login. `403` shows insufficient permission without ending the session. No automatic retry follows an API rejection.
- Tokens are attached only by `ProtectedClient` to a named configured service and a relative `/api/v1/` path. It rejects non-HTTPS service URLs outside loopback, URLs with credentials, query or fragment, and any other path. The HTTP client does not follow redirects and does not use cookies, and waiting is bounded to eight seconds. Anonymous health requests continue without bearer headers.
- Calls to Auth0 are limited to ten seconds. If the tenant cannot be reached when sign-in starts, the app redirects to `/account?signin=start-failed` with a fixed message instead of an error page. If it cannot be reached at sign-out, the local session is still ended first and the account page says the provider logout could not be completed (`/account?signedout=local`).
- Provider errors from the callback are logged on the server and mapped to a fixed code (`/account?signin=<code>`); only static recovery text is rendered. Identity claims are not treated as Users roles or account-standing decisions.
- Development runs over plain HTTP on localhost, so the handler uses query response mode and SameSite=Lax correlation cookies. Use HTTPS in any deployment.

The registry is local to one frontend process. Restarting it signs out existing sessions, even if their encrypted cookies are still readable; unknown session IDs fail closed. Expired entries are removed during registry use. Multiple frontend instances require a shared session registry and cross-instance revocation notifications before deployment; sticky routing alone does not provide that guarantee.

This protects bearer-token use and returned API data from stale circuits and replayed revoked cookies. The session ID is obtained from the validated encrypted cookie or the circuit's server-side principal, never a client-supplied request field. Local revocation stops this frontend from using a token; it does not revoke an already-issued bearer token at Auth0 or cancel a request already accepted by a backend.

## Verification

### Live Blazor check, 2026-11-04

Verified against PR #74 commit `61cc90c`:

- Reused the existing Auth0 Single Page Application with PKCE and no client secret. Stored the domain, client ID, audience and scopes in the frontend's local .NET user-secrets store, outside git.
- Added `http://localhost:5100/signin-oidc` to Allowed Callback URLs and `http://localhost:5100/` to Allowed Logout URLs. Existing React URLs were preserved.
- Started the frontend at `http://localhost:5100` and Transport at `http://localhost:5105`, with Transport configured for the same discovery issuer, `traveltracker-api` audience and `scope` claim.
- Selected **Sign in** and reached the hosted login without a callback URL mismatch. The owner completed login; the browser returned to `/account` without callback code or state in the URL and displayed **You are signed in**.
- The interactive account page displayed **Your protected request completed successfully.** after the Transport API check. This verifies the live browser-to-Blazor-to-Transport path, beyond an anonymous health check.
- The Release build completed with zero warnings and zero errors. `dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category!=PostgreSQL"` passed all 352 cases: 86 frontend, 89 API and 177 Attractions, with none failed or skipped.

Not verified live in this initial check: hosted logout, concurrent sign-out, canceled login, expired/rejected authentication, missing permissions, API outages or PostgreSQL persistence. At the time of this check, review found that an already-open circuit retained its token after another tab signed out, and an API `401` only changed the account page's local state. The follow-up below fixes those session invalidation paths and adds a live logout check; this initial live check alone does not establish completion of #33.

### Session invalidation follow-up

The frontend now registers each login, revokes that session on logout or an API `401`, validates it when restoring the cookie and sending protected calls, and notifies existing circuits of revocation. Regression tests cover the production authentication provider and session registry, including separate circuit scopes and restoration through the real cookie middleware. These are deterministic application tests against a fake provider and stub backend, separate from the live Auth0 check above.

Follow-up verification on 2026-11-04:

- `dotnet restore TravelTracker.slnx` and `dotnet build TravelTracker.slnx --configuration Release --no-restore` passed, with zero build warnings or errors.
- `dotnet test TravelTracker.Tests.slnx --configuration Release --no-restore --filter "Category!=PostgreSQL"` passed 367 cases: 101 frontend, 89 API and 177 Attractions, with none failed or skipped. The frontend gained 15 regression cases. PostgreSQL tests were not run locally.
- Independent read-only security review found no remaining actionable findings after the replacement-login edge case was fixed.
- Restarted the frontend, completed a live hosted sign-in and opened the account page in two browser tabs. The second tab successfully read the protected Transport resource. Signing out in the first tab completed hosted logout and returned to the home page; the second tab immediately showed **Sign in** and removed its earlier API-success result without a reload. Refreshing it remained signed out.

Live canceled-login, expiry/rejection, missing-permission, API-outage and in-flight sign-out checks remain outside this follow-up; deterministic tests cover the relevant session revocation and late-response behavior.

### Automated coverage and remaining live checklist

Run `dotnet test tests/TravelTracker.Frontend.Tests`. The tests use stub HTTP handlers and a fake authentication state for token attachment, expiry, path and URL validation, rejected tokens (`401`), insufficient permission (`403`), sign-out during a request, redirects, failure-code mapping and the account page states. `SignInFlowTests` also drives the real OpenID Connect handler headlessly against an in-process fake Auth0 tenant (discovery, signing keys, token endpoint with a PKCE check): authorize redirect, callback, session, expired token, wrong nonce, missing correlation cookie, rejected PKCE verifier, denied grant, and sign-out with and without an antiforgery token. It also refuses id tokens with the wrong issuer, audience or signature or an expired one, replayed callbacks, token endpoint errors, and a token response with no access token; it checks the secret goes only to the token endpoint, the session cookie is HttpOnly and SameSite=Lax with no token in it, and the session ends when the access token does (fake clock). "Check API access" is covered by restoring the user from the real session cookie the way circuit start does and running the same `ProtectedClient` call against a stub Transport: it asserts the issued token is sent as the bearer, and that nothing is sent when signed out, expired or after sign-out, with Transport 401 and 403 surfaced. These establish client behavior against a protocol-faithful fake, not live Auth0 or real networking. Only the SignalR transport of the circuit itself is not driven; the bUnit page tests cover the button's rendering and states. Existing real JWT backend tests cover signature, issuer, audience, expiry and scope enforcement with test signing keys, not Auth0-issued tokens.

Live checklist after registration:

1. Open the account page and select **Sign in**. Confirm the browser redirects to the configured Auth0 tenant and returns to `/account` with no callback code or state left in the URL.
2. Start Transport (`TravelTracker.Api`, port 5105) with the Auth0 bearer settings above. Select **Check API access**. The app reads the Example Bus sample through `GET /api/v1/transport-modes/00000000-0000-0000-0000-000000000101`, requiring `catalogue.read`. This proves a protected public DTO read without depending on Attractions' PostgreSQL/Users setup. Do not paste tokens into logs or screenshots.
3. Cancel or deny login and verify recoverable messaging. An invalid callback must not authenticate.
4. Use a test account without `catalogue.read`; verify `403` and no retry or sign-out loop. Verify an expired or rejected session offers sign-in and stops protected calls.
5. Sign out during an API call; verify no late success appears and that after a refresh protected access stays unavailable until an explicit sign-in.
6. Stop an API and verify a useful error while navigation remains usable. `/health` success alone does not prove protected access.

Outside this verification: production deployment, long-duration session behavior, provider key rotation and the separate Users/Attractions integration.

References: [ASP.NET Core OpenID Connect](https://learn.microsoft.com/aspnet/core/security/authentication/configure-oidc), [Auth0 regular web application quickstart](https://auth0.com/docs/quickstart/webapp/aspnet-core).

## Settings scopes (#48)

The account's own preferences use two scopes on the Settings API. `settings.read` is in the default `Auth0:Scope` (the view needs it to load values), so add it to the API's scopes and to the frontend application's user-delegated grant before signing in. `settings.write` is optional, like `catalogue.write`: without it the Settings page is read-only and says so. To allow saving, add `settings.write` to the API, grant it, set `Auth0:Scope` to `openid profile email catalogue.read users.read settings.read settings.write` and sign in again. The page reads the grant from the server-owned session metadata, never from a browser-decoded token; the Settings service remains authoritative for every write. See [Settings service](settings-service.md#frontend-48).

## Attraction write grants (#43)

For users intended to create/edit attractions, authorize `catalogue.write` in the API's user-delegated application access and user role/grant. Configure `Auth0:Scope` as `openid profile email catalogue.read users.read catalogue.write`; the default remains read-only. Restart and sign in again to obtain the new grant. The frontend stores scope returned by the trusted token endpoint as server-owned session metadata (when OAuth omits scope, requested scopes apply). ID-token claims cannot impersonate this metadata. It controls UI availability only; no access token is browser-decoded, and Attractions remains authoritative for scope, ownership and live Users standing. Older sessions without granted-scope metadata remain read-only until a new sign-in.

Live verification on 2026-11-04: the API originally defined only `catalogue.read`, so requesting write access alone did not grant it. With owner approval, `catalogue.write` was added to the API and to the frontend's user-delegated application grant. After a fresh sign-in and consent, **Create attraction** appeared and opened the creation form. No live attraction was submitted. This cloud configuration and the local user-secrets are not committed; other environments need the same explicit setup. The independent Users service now supplies sign-in profile provisioning, live standing and administrator operations; see [setup](first-administrator-setup.md).

Protected attraction name searches allow exactly one encoded name query on the collection route. Token destination validation, no redirects/cookies, request time bounds, session revocation and rejection of late results after sign-out also apply to POST/PATCH. A 403 never claims success and retains form input. Unconfirmed writes are not retried automatically because a network failure may occur after the service committed the record.

## Accommodation write grant (#46)

Browsing and selecting accommodations requires the existing `catalogue.read` grant. Adding an accommodation also requires `catalogue.write`, which remains opt-in like attraction and transport creation. For users intended to add places, authorize `catalogue.write` in the API and frontend application's user-delegated grant, then configure `Auth0:Scope` as `openid profile email catalogue.read users.read settings.read catalogue.write` and sign in again. The frontend calls the Accommodations service at `Services:Accommodations` (local default `http://localhost:5106`) through the protected HTTP client. It never sends the Users backend service token to the browser or Accommodations API.

The form defaults new places to private. The optional shared-visibility control is shown only when the live Users profile indicates an administrator; the Accommodations service independently checks scope, account standing, ownership and administrator status. A frontend hint does not grant permission, and `403` responses remain errors. The default Auth0 scope stays read-only. Failed submissions retain the form, and POST is not retried automatically after an ambiguous network failure because the service may already have created the place.
