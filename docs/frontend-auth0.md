# Frontend Auth0 sign-in (#33)

TravelTracker uses **Auth0** as its identity provider (owner decision, 2026-11-03). The Blazor Web App in `src/TravelTracker.Frontend` signs users in with the ASP.NET Core OpenID Connect handler: authorization-code flow with PKCE, callback state and nonce handling, access-token acquisition and hosted logout. TravelTracker never collects provider passwords.

Status of the evidence: the hosted login, protected Transport read and hosted logout were manually confirmed by the owner (2026-11-03) on the earlier React client. This port to Blazor has automated tests but has **not** been run against a live Auth0 tenant yet; repeat the live checklist below before treating #33 as verified for Blazor.

## Auth0 dashboard setup

Use manual dashboard setup; Auth0's optional AI-assisted/MCP onboarding is not required.

1. Create an Auth0 tenant and an application named `TravelTracker frontend`.
   - **Regular Web Application** (recommended): the client secret stays on the server, in user-secrets or an environment variable.
   - A **Single Page Web Application** also works with no secret, because PKCE is always on. Leave `Auth0:ClientSecret` empty in that case.
2. Copy the Domain (hostname only) and Client ID from **Settings**.
3. Add the app origins to **Allowed Callback URLs** as `<origin>/signin-oidc`, and to **Allowed Logout URLs** as `<origin>/`. Development uses `http://localhost:5100/signin-oidc` and `http://localhost:5100/`. Register deployed HTTPS origins separately; do not use wildcards.
4. Under **Applications → APIs**, create a custom API named `TravelTracker API`, identifier `traveltracker-api`, signing algorithm **RS256**. Add permission `catalogue.read`. Add `catalogue.write` only when write features are needed.
5. Under this API's **Application Access**, select **Edit** for `TravelTracker frontend` and authorize **User-Delegated Access** with `catalogue.read`. Creating the application and API does not link them under per-app access policy. A missing grant causes "Client is not authorized to access resource server." If using RBAC, assign the read permission to the intended test user through a role. The backend reads the OAuth `scope` claim, not the Auth0 `permissions` array. Successful login alone does not establish API authorization.
6. Store the settings outside git, from `src/TravelTracker.Frontend`:

```powershell
dotnet user-secrets set "Auth0:Domain" "your-tenant-region.auth0.com"
dotnet user-secrets set "Auth0:ClientId" "your-client-id"
dotnet user-secrets set "Auth0:Audience" "traveltracker-api"
dotnet user-secrets set "Auth0:ClientSecret" "only-for-a-regular-web-application"
```

`Auth0:Scope` defaults to `openid profile email catalogue.read`. Environment variables such as `Auth0__Domain` work too. The audience must exactly equal the API identifier. Restart the app after changing settings. Missing or malformed settings leave navigation usable, log a warning at startup, and show a setup-pending account page.

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
- The session ends when the access token does (the cookie expires with it). There is no silent renewal. Expiry, a rejected token (API `401`) and a missing token all show a sign-in prompt and stop protected calls; `403` shows insufficient permission without ending the session. No automatic retry follows an API rejection.
- Sign-out is a POST with an antiforgery token. It clears the session cookie and redirects to Auth0's logout endpoint, which returns to the start page. The circuit is dropped by that navigation. A sign-out that lands while a protected request is in flight discards the late result, because the user is re-read after every await.
- Tokens are attached only by `ProtectedClient` to a named configured service and a relative `/api/v1/` path. It rejects non-HTTPS service URLs outside loopback, URLs with credentials, query or fragment, and any other path. The HTTP client does not follow redirects and does not use cookies, and waiting is bounded to eight seconds. Anonymous health requests continue without bearer headers.
- Provider errors from the callback are logged on the server and mapped to a fixed code (`/account?signin=<code>`); only static recovery text is rendered. Identity claims are not treated as Users roles or account-standing decisions.
- Development runs over plain HTTP on localhost, so the handler uses query response mode and SameSite=Lax correlation cookies. Use HTTPS in any deployment.

## Verification

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
