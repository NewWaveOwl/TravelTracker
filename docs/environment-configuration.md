# Environment configuration and secrets

TravelTracker reads ASP.NET Core configuration on the server. Environment
variables use double underscores for section separators. The browser needs
public UI assets and a session cookie. Database connections, provider client
secrets, internal service credentials and private storage keys belong only in
the process that consumes them.

| Setting | Consumer | Source |
| --- | --- | --- |
| `ConnectionStrings__<Service>` | Each of Users, Attractions, Transport, Accommodations, Settings and Trips | Development launcher or per-host secret-store injection. In Production use a service database, explicit username and `SSL Mode=VerifyFull` with the correct trust chain. |
| `Authentication__Bearer__Authority`, `Authentication__Bearer__Issuer`, `Authentication__Bearer__Audience` | API hosts | Deployment configuration, with credential-free HTTPS authority and issuer. |
| `Users__InternalBaseUrl` and `Users__ServiceAccessToken` | Attractions, Transport, Accommodations and Settings | HTTPS Users address and a static or bootstrap backend credential when automatic Supabase service-token refresh is not configured. |
| `TripReferences__UsersBaseUrl` and `TripReferences__UsersServiceAccessToken` | Trips | HTTPS Users address and static or bootstrap backend credential. The other three `TripReferences__<Service>BaseUrl` values carry traveller bearer tokens, so use HTTPS in Production. |
| `Supabase__ServiceEmail`, `Supabase__ServicePassword` | Attractions, Transport, Accommodations, Settings and Trips | Optional dedicated `users.access` service-account credentials injected via secret store so `SupabaseServiceAccessTokenProvider` refreshes the internal Users bearer token before expiry. |
| `Services__<Service>` | Frontend server | All six HTTPS service addresses in Production. |
| `Authentication__Provider` | Frontend server | `Local` (default) signs in through the Users API; `Supabase` signs in through Supabase Auth. Any other value fails startup. |
| `Authentication__Bearer__Provider` | API hosts | `Local` (default) or `Supabase`; Supabase also needs `Authentication__Bearer__Authority`. See [Supabase Auth](supabase-auth.md). |
| `Supabase__Url`, `Supabase__PublishableKey` | Frontend and Users API when the provider is Supabase | Public project settings. Use a publishable or legacy anon key, never a secret or service-role key. |
| `Bootstrap__Secret`, `AdminBootstrap__Secret` | Users and, when enabled, frontend server | Operator-provisioned one-time setup credential. See [administrator setup](first-administrator-setup.md). |
| Storage keys | Server component that owns a future private storage integration | No private storage key is read by the current hosts. Keep any future key in the runtime secret store and restrict it to that component. |

## Development

Run the [Supabase dev launcher](../README.md#run-the-full-app) in a local
terminal. It saves non-secret settings in ignored
`.traveltracker-launch.json`, prompts for the two passwords without echo, and
passes connection strings and service credentials through child-process
environment rather than command arguments. Stop the launcher when finished.
The frontend and Users projects also have ASP.NET Core User Secrets IDs for
manual local development. User Secrets are a development convenience with local
filesystem protection, so use only local or staging credentials there.
For the other hosts, inject values into the specific process environment.
See [ASP.NET Core development secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets).

Development may use loopback HTTP, local database settings, or an anonymous
frontend preview. Keep these settings out of Production. Never place
credentials in committed appsettings or launch profiles, browser assets, URL
user info or query strings, shell history, or diagnostic output.

## CI

The [PR workflow](../.github/workflows/ci.yml) uses a disposable runner-local
PostgreSQL instance and fake auth fixtures, so ordinary PR validation needs no
live secrets. The host-smoke script sets the Testing environment explicitly.
If a staging check is added to CI, scope secret injection to a protected
environment and only the consuming step. Use a short-lived credential where
available, keep workflow command tracing off, and publish results without
request headers, connection strings, configuration dumps, or provider bodies.

## Runtime

Run deployed hosts in Production. Use the platform's managed secret store
to inject environment variables into each server process at deploy time.
Restrict access to the running process and its dumps. Keep HTTPS enabled for
service calls, disable example seeding and parameter logging, and use separate
database runtime and migration roles. Rotate secrets in the store and restart
the consuming hosts. Leave migrations disabled during normal startup.
Production startup reports missing or unsafe configuration by setting name.
Review the emitted logs and proxy settings before enabling HTTP tracing.

## Diagnostics and verification

The API hosts keep provider error detail and parameter logging disabled.
Migration failures report the exception type and a fixed troubleshooting
message. The launcher prints its own validation messages and the exception
type for unexpected failures, and the frontend omits provider callback text
and exception objects from its service failure logs. Keep proxy and telemetry
configuration from recording callback query strings, cookies, authorization
headers, or form bodies. Supabase confirmation links carry a one-time
confirmation hash and must be handled as sensitive.

Run the offline launcher checks with `dotnet run --file scripts/TestLauncher.cs`.
The Users API tests cover database configuration and redacted startup errors.
Frontend service-client tests inject a fake secret into an exception and assert
that the log keeps only the failure type. Keep the fixtures synthetic and
record the test counts and any deployment checks in the PR.

Branch verification on November 22, 2026:

- Offline launcher tests passed 22 of 22, including redaction of an unexpected
  path exception. A local harness ran 13 configuration and redaction probes
  against the shared source using synthetic values.
- Frontend Release build completed with zero warnings and errors.
- Solution restore could not reach NuGet for required packages. The full
  solution build, integration suites and live deployment checks remain pending.
