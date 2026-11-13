# Supabase Auth

The owner selected Supabase Auth to replace Auth0 on 2026-11-12. Supabase manages
login identities, verified email and passwords. TravelTracker's Users API keeps
application profiles, administrator roles, bans, deactivation and audit history.
The other services continue to own their databases and enforce private ownership.

## Configure the Supabase project once

1. In the existing Supabase project, enable email/password authentication and email
   confirmation. Configure the email provider/SMTP and Site URL for the environment.
   Local development uses `http://localhost:5100/`. Deployments must use HTTPS.
   Set the **Confirm signup** email template's confirmation link to
   `{{ .SiteURL }}/auth/confirm?token_hash={{ .TokenHash }}&type=email`.
   The confirmation page requires an explicit protected form submission before
   verification; it never automatically signs in or puts access/refresh tokens in
   the browser URL. The traveller signs in normally after confirmation.
2. Under Auth signing keys, use an asymmetric signing key (ES256 or RS256).
   The application validates keys from
   `https://PROJECT.supabase.co/auth/v1/.well-known/jwks.json`, exact issuer
   `https://PROJECT.supabase.co/auth/v1`, audience `authenticated`, and token expiry.
   Legacy HS256 signing secrets are not supported by this integration.
3. Run [supabase-auth-hook.sql](../src/Database/supabase-auth-hook.sql) in the
   project's **postgres** database. Select `public.traveltracker_access_token_hook`
   under Authentication > Hooks > Custom Access Token. Preserve any existing hook
   behavior deliberately; only one selected custom-access-token hook runs.
4. Create a separate, confirmed Auth user for calls between services. In its
   administrator-managed **app_metadata**, set `"traveltracker_service": true`.
   This user receives only `users.access`, never traveller permissions. Use a
   unique strong password and do not use this account for interactive browsing.
5. Obtain the project URL, public publishable key (or legacy **anon** key), and
   service-account email. Share non-secret setup values with the team. Distribute
   the service password and database credentials privately. The frontend never
   needs a Supabase secret key or `service_role` key.

Ordinary confirmed travellers receive `catalogue.read`, `catalogue.write`,
`users.read`, `users.write`, `settings.read`, `settings.write`, `trips.read` and
`trips.write`. These permissions enable features, not administrator authority;
Users still checks live role and standing. An operator can restrict an account by
setting `app_metadata.traveltracker_scopes` to an array of these exact strings.
An empty array grants none. Unknown strings and `users.access` are excluded from
the traveller list. User-editable `user_metadata` never grants permissions.
Anonymous users receive no TravelTracker scopes. Sign in again after permission
changes; an already issued access token retains its claims until expiry.

## Run on each developer machine

Install .NET 10, check out the branch containing this migration, and run from the
repository root. Trust the Users HTTPS development certificate once:

```sh
dotnet dev-certs https --trust
dotnet run --file scripts/StartSupabaseDev.cs -- --setup
dotnet run --file scripts/StartSupabaseDev.cs
```

The launcher supplies each API's Supabase bearer settings and allows the verified
service-account subject to call the internal Users access endpoint. The public
key is supplied to Users for verified identity lookup. Enter the **Auth service
account password** and **PostgreSQL database password** at their separate hidden
prompts. They are different credentials and are never saved in launch settings.

The six `traveltracker_*` databases and valid Session pooler credentials are still
required. Supabase Auth lives in the managed `auth` schema in `postgres`; it does
not automatically create the service databases. A PostgreSQL `28P01` remains a
database-password/role/project mismatch and is not repaired by changing Auth0.
The launcher checks configuration separately from actual credential validity.

Open `http://localhost:5100/account`, choose Sign in, or register a new account.
Confirm its email before signing in. Login and registration use server-side
requests to Supabase; tokens are kept in the encrypted HttpOnly application
session cookie rather than browser local/session storage. Session revocation and
expiry continue to invalidate open application circuits. No automatic session
renewal is provided; sign in again after expiry or restarting the frontend.

For frontend-only development, configure `Authentication:Provider=Supabase`,
`Supabase:Url` and `Supabase:PublishableKey` through .NET user-secrets or the matching
environment variables (`Authentication__Provider`, `Supabase__Url`,
`Supabase__PublishableKey`), then run:

```sh
dotnet run --project src/TravelTracker.Frontend --launch-profile http
```

Without auth settings this remains a UI preview. With them, sign-in requires
Supabase connectivity; profiles and saved data also require their running APIs.
The earlier [Auth0 guide](frontend-auth0.md) describes the explicit compatibility
path and historical verification, not the new launcher's setup.

## Move existing users without losing their private data

Do this during a maintenance window with **all application processes stopped**.
Back up the Users, Attractions, Accommodations, Settings and Trips databases.
Transport has no owner identity columns to migrate. Do not delete Auth0 users or
revoke the old application until the new setup and old-account access are proven.

1. Export/import Auth0 identities following Supabase's official
   [Auth0 migration guide](https://supabase.com/kb/guides/migrate-from-auth0).
   Existing Auth0 passwords do not automatically work in Supabase. Compatible
   password hashes require an authorized Auth0 export; otherwise use a verified
   account invitation/password recovery procedure. Never ask teammates to send
   passwords or export files in Teams.
2. Establish an explicit operator-verified mapping from each old `(issuer, subject)`
   to the corresponding Supabase user UUID. **Do not auto-link by email**, which
   could connect unrelated accounts. Include banned/deactivated accounts and retain
   their standing. Preserve administrator roles in the Users database.
3. For each mapping, use [MigrateSupabaseIdentity.sql](../scripts/MigrateSupabaseIdentity.sql)
   with psql `-v old_issuer=... -v old_subject=... -v new_issuer=... -v new_subject=...`
   against each service database. It defaults to a preview; review the counts,
   then supply `-v apply=true` to apply that database's transaction. Run against
   Attractions, Accommodations, Settings, Trips, then Users. Supply the database
   connection/password securely using your usual psql configuration, not source.
4. The script preserves public account IDs, profiles, roles, standing, trip IDs,
   catalogue IDs, snapshots, and administrator audit records. It updates the
   permanent bootstrap identity too. Existing destination records cause a collision
   error rather than an implicit account merge. A repeat after success is a no-op.
5. There is **no distributed transaction across these databases**. Keep the app
   stopped until all five updates succeed for every mapping. If any fails, repair
   and resume or restore the coordinated backups before restarting. Do not allow
   first-login provisioning to create duplicate accounts during the cutover.
6. Update first-administrator deployment settings to the new verified issuer/email
   if applicable; a migrated administrator retains its existing role and does not
   need bootstrap again. Sign in and verify old trips, private catalogue entries,
   settings, profile, bans and admin visibility before disabling the old provider.

The migration SQL is an operator tool, not an automatically executed startup
migration. No passwords, identity exports or live database rows are changed by
building or testing this branch. The actual project hook selection, service-user
metadata, account import and ownership cutover must be completed in the authorized
Supabase project before calling the live migration complete.

## Evidence and remaining checks

Local migration verification on 2026-11-12:

- Full Release solution build: zero warnings and errors.
- Direct xUnit execution: 1,123 application/domain cases passed, zero failed or
  skipped, including 405 frontend cases. Standard VSTest's loopback test-host
  transport is restricted in this environment, so the existing in-process runner
  executed the built assemblies with PostgreSQL-marked cases excluded.
- Guided launcher: 21 offline configuration checks passed.
- PostgreSQL 17.11, isolated single-user test cluster: eight hook permission
  assertions and restricted hook-role execution passed. The identity-cutover SQL
  was exercised against five separate fixture databases: preview made no changes,
  apply preserved IDs/snapshots/roles/standing/audit, and repeat was idempotent.
  Single-user execution does not establish multi-session concurrency or EF
  persistence behavior. The broader PostgreSQL-marked suites remain unrun locally
  because loopback database connections are restricted.
- Independent read-only security review reported no actionable findings.

Automated tests use controlled provider/JWKS responses and signed test tokens.
They establish protocol, validation and application behavior, not a completed
production account import. Before rollout, test verified/unverified login,
registration and confirmation, wrong password, sign-out across tabs, expiry,
service-account authorization, API 401/403, provider outage and access to an old
account's data. Run the PostgreSQL relational suites with an isolated database;
SQL cutover and hook execution need a PostgreSQL/Supabase staging validation.

CI runs `bash scripts/test-supabase-auth-hook.sh` against a fresh disposable
PostgreSQL database. It checks traveller defaults, service-only access, metadata
spoofing, scope restrictions, anonymous/privileged-role rejection and execution
grants. This fixture verifies SQL behavior; live Supabase hook selection and
database-owner identity cutover still require staging verification.

Official references: [JWTs and JWKS](https://supabase.com/docs/guides/auth/jwts),
[custom access-token hooks](https://supabase.com/docs/guides/auth/auth-hooks/custom-access-token-hook),
[sign in](https://supabase.com/docs/reference/javascript/auth-signinwithpassword),
and [registration](https://supabase.com/docs/reference/javascript/auth-signup).
