# Supabase Auth (optional provider)

TravelTracker signs travellers in one of two ways, chosen per deployment. **Local** is the default: the Users API checks passwords against its own PostgreSQL database and issues the tokens (see [API authentication](authentication.md)). **Supabase** hands login, passwords and email confirmation to Supabase Auth. In both cases the Users service keeps profiles, administrator roles, bans and audit history, and every other API only verifies bearer tokens.

A deployment uses exactly one provider. Accounts are keyed by the token's issuer and subject, so Local and Supabase accounts never merge, and changing the provider starts everyone with a fresh account.

## Select it

Set these on every host (environment variables use `__` for `:`):

| Setting | Hosts | Value |
| --- | --- | --- |
| `Authentication:Provider` | Frontend | `Supabase` (`Local` is the default). Any other value fails startup. |
| `Authentication:Bearer:Provider` | Every API | `Supabase` (`Local` is the default). |
| `Authentication:Bearer:Authority` | Every API | `https://PROJECT.supabase.co/auth/v1` |
| `Authentication:Bearer:Issuer` | Every API | The same URL, with no trailing slash. |
| `Authentication:Bearer:Audience` | Every API | `authenticated` |
| `Authentication:Bearer:ValidAlgorithms` | Every API | `["ES256", "RS256"]`, matching the project's asymmetric signing key. |
| `Supabase:Url`, `Supabase:PublishableKey` | Frontend and Users API | The project URL and the publishable (or legacy anon) key. A secret or `service_role` key is rejected. |
| `Bootstrap:Email`, `Bootstrap:Issuer`, `Bootstrap:Secret` | Users API | First administrator setup. `Bootstrap:Issuer` is the Issuer above. See [administrator setup](first-administrator-setup.md). |

`Authentication:Bearer:PublicKey` and `Authentication:Issuer:PrivateKey` are Local settings and are ignored here. With Supabase selected the Users API no longer serves `POST /api/v1/auth/register`, `login` or `service-token` (they answer `404`), and the Account page offers Supabase's email and password changes instead of the Users API password form. The launcher (`scripts/StartDev.cs`) only wires Local sign-in; pass the settings above through environment variables or user secrets to run the stack against Supabase. `deploy/azure/apps.bicep` already sets them.

## Configure the Supabase project once

1. Enable email/password sign-in and email confirmation, and configure SMTP and the Site URL (`http://localhost:5100/` locally, HTTPS in deployments). Set the **Confirm signup** template's link to `{{ .SiteURL }}/auth/confirm?token_hash={{ .TokenHash }}&type=email` and the **Change Email Address** template's link to `{{ .SiteURL }}/auth/confirm?token_hash={{ .TokenHash }}&type=email_change`, with **Secure email change** on. The confirmation page needs an explicit form submission; it never signs in or puts tokens in the URL.
2. Use an asymmetric signing key (ES256 or RS256). The APIs read the keys from `https://PROJECT.supabase.co/auth/v1/.well-known/jwks.json` and check the exact issuer, audience and expiry. Legacy HS256 secrets are not supported.
3. Run [supabase-auth-hook.sql](../src/Database/supabase-auth-hook.sql) in the project's `postgres` database and select `public.traveltracker_access_token_hook` under Authentication > Hooks > Custom Access Token. Only one custom access token hook runs, so merge it with any existing one on purpose.
4. Create a confirmed Auth user for calls between services and set `"traveltracker_service": true` in its administrator-managed `app_metadata`. It receives only `users.access`. Sign in as it to get an access token, and give that token to the other backends as `Users:ServiceAccessToken` or `TripReferences:UsersServiceAccessToken`; it expires, so refresh it before then (`deploy/azure/Deploy-Demo.ps1 -RefreshOnly` does this for the Azure demo). Its subject must be listed in `UsersAccess:AllowedServiceSubjects`.

## Scopes

The hook grants ordinary confirmed travellers `catalogue.read`, `catalogue.write`, `users.read`, `users.write`, `settings.read`, `settings.write`, `trips.read` and `trips.write`. `users.admin` is never a default: grant it through `app_metadata.traveltracker_scopes` to an account that is also an active administrator in the Users database, because the API checks both the scope and the live role. Setting `traveltracker_scopes` to an array of exact scope names restricts an account, and an empty array grants nothing. `user_metadata` never grants anything, and anonymous users receive no scopes. A changed grant reaches the token at the next sign-in.

## Behavior in the app

- Sign-in and registration use the email address, with passwords of at least 8 characters. A new account must confirm its email before it can sign in.
- Tokens stay in the encrypted HttpOnly session cookie, never in browser storage. There is no automatic renewal, so sign in again after expiry.
- The profile shows the verified Supabase email. The Users database stores no email address, only the issuer and subject, a role, the standing and the address. A Supabase account has no username.
- **Sign-in credentials** on the Account page changes the email or password. Each request reauthenticates with the current password, checks the new session belongs to the same subject, then ends the current TravelTracker session.
- Bootstrap confirms the email with the Supabase Auth user endpoint (`email_confirmed_at` must be set and the subject must match) and compares it, ignoring case, with `Bootstrap:Email`.

## Tests

`SupabaseBearerHost` (Transport), `SupabaseSignInTests` (Frontend) and `SupabaseIdentityTests` and `SupabaseModeTests` (Users) run the real handlers against signed test tokens and a fake Auth service. `scripts/TestSupabaseAuthHook.sql` runs in CI against a disposable PostgreSQL database and checks the hook's grants, spoofing and execution permissions. None of these prove a live project: hook selection, SMTP and the service user still need a staging check.

Official references: [JWTs and JWKS](https://supabase.com/docs/guides/auth/jwts), [custom access token hooks](https://supabase.com/docs/guides/auth/auth-hooks/custom-access-token-hook).
