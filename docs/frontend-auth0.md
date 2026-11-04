# Frontend Auth0 sign-in (#33)

TravelTracker uses **Auth0** as its identity provider (owner decision, 2026-11-03). The React app uses the official `@auth0/auth0-react` SDK for authorization-code flow with PKCE, callback state/nonce handling, access-token acquisition and hosted logout. TravelTracker never collects provider passwords. Public tenant/application settings are supplied in the ignored local configuration. The development application/API grant is configured. The owner verified hosted sign-in, a successful protected Transport read and hosted logout with protected access still unavailable after refresh. These are manual checks in the owner's browser; automated failure-path evidence is listed below.

## Auth0 dashboard setup

Use manual dashboard setup; Auth0's optional AI-assisted/MCP onboarding is not required.

1. Create an Auth0 tenant and a **Single Page Web Application** named `TravelTracker frontend`. In its **Settings**, copy Domain (hostname only) and Client ID. Do not copy the Client Secret.
2. Add `http://127.0.0.1:5173` and `http://127.0.0.1:5174` to each of **Allowed Callback URLs**, **Allowed Logout URLs**, and **Allowed Web Origins**. Use the exact origin printed by Vite. `localhost` and `127.0.0.1` are different origins. Register deployed HTTPS origins separately; do not use wildcards.
3. Under **Applications → APIs**, create a custom API named `TravelTracker API`, identifier `traveltracker-api`, signing algorithm **RS256**. Add permission `catalogue.read`. Add `catalogue.write` only when write features are needed; do not grant it by default for this read-only check.
4. Under this API's **Application Access**, select **Edit** for `TravelTracker frontend` and authorize **User-Delegated Access** with `catalogue.read`. Creating the application and API does not automatically link them under per-app access policy. A missing grant causes “Client is not authorized to access resource server.” If using RBAC, assign the read permission to the intended test user through the appropriate role. The backend reads OAuth `scope`, not the Auth0 `permissions` array. Successful login alone does not establish API authorization.
5. Copy `frontend/.env.example` to `frontend/.env.local` and set:

```dotenv
VITE_AUTH0_DOMAIN=your-tenant-region.auth0.com
VITE_AUTH0_CLIENT_ID=your-public-spa-client-id
VITE_AUTH0_AUDIENCE=traveltracker-api
VITE_AUTH0_SCOPE=openid profile email catalogue.read
```

These are placeholders. Use the actual tenant domain/client ID; the audience must exactly equal the API identifier. Restart Vite after changing settings. No client secret, signing key or token belongs in a Vite variable. Missing/invalid configuration leaves navigation usable and shows a setup-pending account page.

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

Vite proxies API calls in development; production requires the routes or explicit HTTPS service URLs/CORS described in [frontend README](../frontend/README.md). Hosting must serve the SPA at its registered callback origin. Never configure the Auth0 Management API audience as the TravelTracker resource audience.

## Session and trust boundaries

| OAuth role | TravelTracker component |
| --- | --- |
| Resource owner | Traveller signing in |
| Public client | React SPA; Client ID, no secret |
| Authorization server | Selected Auth0 tenant; hosts credential entry and issues tokens |
| Resource servers | ASP.NET APIs; validate access tokens and enforce scopes/standing |

The SDK keeps tokens in memory (`cacheLocation=memory`); no application token logging or local/session-storage token persistence is added. The SDK may keep short-lived redirect transaction data for PKCE/state. A session-storage boolean records local sign-out to block authenticated calls after reload until an explicit sign-in attempt. No identity or token is stored in that flag.

`getAccessTokenSilently` supplies access tokens, never ID tokens. The SDK checks cached token expiration and may renew via its hosted session. Renewal failure and API `401` block further protected calls and offer sign-in; `403` displays insufficient permission without logging the user out. No automatic retry occurs after API rejection. Logout blocks calls before contacting Auth0 and aborts pending requests; a late token result cannot start an authenticated request. Already accepted server work cannot be undone by client logout. Provider logout failure preserves the local block and offers explicit sign-in to reconnect.

Tokens are attached only by the opt-in protected client to a named configured service and relative `/api/v1/` path. It rejects non-HTTPS destinations outside loopback, refuses cross-origin redirects, omits cookies, bounds HTTP waiting and never exposes provider error text. Anonymous health requests continue without bearer headers. Auth0 authorization and HTTP waits are limited to ten seconds. If SDK initialization still remains pending for fifteen seconds, the account page replaces its spinner with a connection-error panel offering Reload and Back to start; navigation remains usable. Identity claims are not treated as Users roles or account-standing decisions.

## Verification

From `frontend`, run `npm run build` and `npm test`. Tests use a mocked SDK and controlled HTTP responses for token attachment, cancellation, late-token/logout rejection, locally signed-out reload, renewal failure, safe canceled-login recovery, `401` and `403`. These establish client behavior, not live Auth0 protocol or server acceptance.

Live checklist after registration:

1. Open the account icon and select **Sign in**. Confirm the browser redirects to the configured Auth0 tenant and returns to `#/account`. No callback code/state should remain in the URL.
2. Start Transport (`TravelTracker.Api`, port 5105) with the Auth0 bearer settings above. Select **Check API access**. The client reads the existing Example Bus sample through `GET /api/v1/transport-modes/00000000-0000-0000-0000-000000000101`, requiring `catalogue.read`. This proves a protected public DTO read without depending on Attractions' separate PostgreSQL/Users setup. The check uses no backend assemblies and grants no write permission. Do not paste tokens into logs/screenshots.
3. Cancel/deny login and verify recoverable messaging. An invalid callback/state must not authenticate.
4. Use a test account without `catalogue.read`; verify `403` and no retry/logout loop. Test a rejected/expired session; verify re-sign-in is offered and protected calls stop.
5. Sign out during token acquisition and during an API call; verify new authenticated calls stop, no late success appears, and reload stays locally signed out. Confirm a new explicit login reconnects.
6. Stop an API/dependency and verify a useful error while navigation remains usable. `/health` success alone does not prove protected access.

Current verification evidence (2026-11-03):

- Auth0 discovery is reachable and the application/API grant is configured. An anonymous authorization check now returns `login_required`, and browser navigation reaches Auth0 Universal Login.
- The owner reported successful hosted login and return to TravelTracker. This is manual owner evidence; the agent-controlled browser tab is a separate session.
- The account-page access check now targets Transport's stable public sample, with the running API configured for the real tenant issuer and `traveltracker-api` audience. Anonymous requests remain protected. The owner reported “Your protected request completed successfully.” from this live read. The server requires the configured issuer/audience and `catalogue.read` scope; no token values were collected in chat or committed.
- Frontend build passes without warnings. All 42 frontend tests pass, including UI recovery for renewal failure, API `401`, API `403`, logout races and storage failure. Existing real JWT backend tests cover signature, issuer, audience, expiry and scope enforcement; they use test signing keys, not Auth0-issued tokens.
- The owner confirmed hosted sign-out and that Check API access remains unavailable after refreshing until another explicit sign-in. Automated tests also cover provider-logout failure, locally signed-out reload and late token/response races.
- PR CI passed for the implementation and startup-recovery changes. Final documentation updates require their own CI result.

The minimum #33 client journey is verified in development. Live cancellation/denial was observed during initial configuration troubleshooting; expiry, API `401`/`403`, storage failures and logout races are covered by controlled automated tests, not claimed as real expired-Auth0-token experiments. Production deployment, long-duration session behavior, provider key rotation and the separate Users/Attractions integration remain outside this verification. Independent PR approval is still required before merge.

References: [Auth0 React quickstart](https://auth0.com/docs/quickstart/spa/react), [SDK source](https://github.com/auth0/auth0-react).
