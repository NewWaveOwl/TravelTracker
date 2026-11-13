# TravelTracker

TravelTracker is a .NET 10 trip planner with a Blazor frontend, six local APIs, Supabase Auth, and PostgreSQL storage on Supabase. Supabase Auth owns sign-in identities and passwords; the Users API still owns TravelTracker profiles, account status and roles.

| To do this | Start here |
| --- | --- |
| Preview the UI, map, and manual markers | [Run only the frontend](#run-only-the-frontend) |
| Sign in, search the catalogue, save trips, or open History | [Run the full app](#run-the-full-app) |
| Configure Supabase Auth or migrate existing identities | [Supabase Auth setup and migration](docs/supabase-auth.md) |

Install the **.NET 10 SDK** and Git. Clone the repository, then run commands from its root:

```sh
git clone https://github.com/NewWaveOwl/TravelTracker.git
cd TravelTracker
```

When testing an unmerged pull request, switch to that PR's source branch before running the app. Keep local changes committed or stashed before switching branches.

## Run only the frontend

```sh
dotnet run --project src/TravelTracker.Frontend --launch-profile http
```

Open [TravelTracker](http://localhost:5100/) or [Map View](http://localhost:5100/map). This preview works without a database, Auth configuration, or API processes. Sign-in and saved-trip operations require the full stack. Map tiles require internet access. Stop with **Ctrl+C** before starting the full app; both use port 5100.

## Run the full app

Use the same launcher command in PowerShell, Bash, or a macOS terminal; PowerShell 7 is not required:

```sh
dotnet run --file scripts/StartSupabaseDev.cs
```

The launcher builds the solution and starts the frontend plus Users, Attractions, Transport, Accommodations, Settings and Trips APIs. On first run, it asks for missing settings and saves only public/non-secret values in the Git-ignored `.traveltracker-launch.json`. Every launch prompts privately for the dedicated Supabase Auth service account password and the separate PostgreSQL database password. Neither password, access tokens nor refresh tokens are saved in the launch settings.

### Maintainer setup before teammates launch

Follow [Supabase Auth setup](docs/supabase-auth.md) to:

1. Enable email/password sign-in and configure email confirmation and permitted redirect URLs.
2. Use an asymmetric **ES256 or RS256** JWT signing key; legacy HS256 access tokens are not accepted.
3. Install and enable the custom access-token hook. It derives TravelTracker scopes from administrator-managed `app_metadata`, never user-editable metadata.
4. Create and confirm a dedicated internal service Auth user with **`users.access` only**. This account is used by the APIs for Users account-standing checks; it is not a traveller login. The launcher validates its JWT, checks the current Auth user, and configures its subject in the Users service allowlist. Use a confirmed, operator-controlled email address.
5. Initialize the six existing service databases and provide each teammate an assigned database role. Migrating Auth does not replace database credentials or automatically move old account identities.

Use the team's existing Supabase project. A new teammate needs access to the shared setup, not a new project or Auth0 application. Preserve existing TravelTracker user IDs when linking migrated identities so trip and catalogue ownership stays intact; the migration guide describes the cutover requirements.

### First run on each machine

Obtain these values from the maintainer:

| Launcher setting | Where to find it |
| --- | --- |
| Pooler host or PostgreSQL URI | Supabase **Connect > Session pooler**, port **5432**. Copy the host or URI. URI input is hidden; embedded passwords are discarded and requested again privately. |
| Database username | Same connection panel; include the `.project-reference` suffix and use the assigned runtime role. |
| Root certificate | Download the database SSL root certificate and supply its path **on this computer**. Spaces and surrounding quotes are supported. |
| Supabase Project URL | Project API settings: `https://project-reference.supabase.co`. Use the origin without `/auth/v1`. |
| Publishable key | API keys: an `sb_publishable_...` key, or the legacy **anon** key. **Never enter an `sb_secret_...` or `service_role` key.** |
| Service account email | The dedicated, confirmed Supabase Auth internal service user provisioned above. |

The public URL/key are also passed to the frontend and Users API by the launcher. The issuer is `<Project URL>/auth/v1`, the audience is `authenticated`, and the APIs validate issuer, audience, expiry, signature and signed scopes. No Auth0 domain, client ID, client secret, audience or callback setup is required for this launcher.

Trust the local Users API HTTPS certificate once:

```sh
dotnet dev-certs https --trust
```

On Ubuntu, .NET HTTP clients may also need the OpenSSL trust directory set in the launching terminal:

```bash
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:${SSL_CERT_DIR:-/usr/lib/ssl/certs}"
```

For other distributions, use the trust path reported by `dotnet dev-certs https --trust --verbose`. Do not disable certificate validation. Windows, Linux and macOS use the same launcher; certificate trust, local certificate paths and external network access still need verification on each target OS.

### Change or check settings

```sh
# Review every setting; Enter keeps its value. Save without starting services.
dotnet run --file scripts/StartSupabaseDev.cs -- --setup

# Validate saved settings and certificate path without network or password prompts.
dotnet run --file scripts/StartSupabaseDev.cs -- --check-config
```

Check mode validates configuration shape only. It does not test database access, certificate contents, Auth credentials, signing-key availability or hook configuration. `Supabase__Url`, `Supabase__PublishableKey`, and `TRAVELTRACKER_SERVICE_EMAIL` environment values override saved values. `TRAVELTRACKER_LAUNCH_CONFIG` selects an alternate local configuration file.

If upgrading an older Auth0 launcher setup, **rename** `.traveltracker-launch.json` to keep a backup, then run `--setup`. Re-enter the same database host, role and local certificate path, followed by the new Supabase Auth values. Old Auth0 launch fields are rejected with migration instructions; the obsolete four-argument command and `Start-SupabaseDev.ps1` are not the Supabase Auth startup path. Never add passwords, privileged API keys, or tokens to the JSON file or commit them to Git.

### Sign in and save a trip

1. Enter the **Supabase Auth service account password**, then the **PostgreSQL database password**, at the hidden prompts. These are different credentials.
2. When startup completes, open [TravelTracker](http://localhost:5100/) and sign in using a traveller's Supabase Auth account. New sign-ups may need to confirm their email before signing in.
3. Build an itinerary in [Map View](http://localhost:5100/map), select **Review on map**, and **Save trip**. Save any new catalogue entries first.
4. Copy the saved trip URL, stop the stack with **Ctrl+C**, restart and sign in, then reopen that URL or find the trip in **History**.

The launcher prints the internal service token's expiry. Restart before it expires; it does not silently refresh the service session.

### Troubleshooting database login

`28P01: password authentication failed` is a PostgreSQL credential error, even after moving sign-in to Supabase Auth. Check that the pooler host, suffixed database username and database password belong to the same project and role. Do not enter a Supabase website password or Auth user password at the database prompt. Stop and restart the launcher to re-enter the password. Changing authentication providers does not repair database passwords.

### Initialize a new Supabase project (maintainer)

Create the service databases with [the bootstrap SQL](src/Database/supabase-bootstrap.sql), using the project's default `postgres` database and a role with `CREATEDB`. With PostgreSQL client tools installed:

```sh
psql -X -W "host=your-region.pooler.supabase.com port=5432 user=postgres.yourprojectref dbname=postgres sslmode=verify-full sslrootcert='/path/to/supabase-ca.crt'" -f src/Database/supabase-bootstrap.sql
```

Run `dotnet run --file scripts/StartSupabaseDev.cs -- --apply-migrations` for initial schema setup or new EF migrations. A maintainer must use a migration-capable database role. Teammates use their assigned runtime role for normal development. Never recreate the existing shared databases to resolve an authentication error.

## Tests and reference

Offline launcher configuration checks (no live database or Auth credentials):

```sh
dotnet run --file scripts/TestLauncher.cs
```

Application tests:

```sh
dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"
```

For PostgreSQL tests, set `TRAVELTRACKER_TEST_POSTGRES` to an isolated PostgreSQL instance with `CREATEDB` and use `--filter "Category=PostgreSQL"`. See the [journey test guide](docs/milestone-3/critical-journey.md).

- [Supabase Auth setup and migration](docs/supabase-auth.md)
- [API contracts and service URLs](docs/api.md)
- [Trips API and persistence](docs/trips-service.md)
- [First administrator setup](docs/first-administrator-setup.md)
- [Contribution workflow](CONTRIBUTING.md)
