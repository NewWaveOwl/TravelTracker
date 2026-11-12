# TravelTracker

TravelTracker is a .NET 10 trip planner with a Blazor frontend, six local APIs, Auth0 sign-in, and shared PostgreSQL storage on Supabase.

| To do this | Start here |
| --- | --- |
| Preview the UI, map, and manual markers | [Run only the frontend](#run-only-the-frontend) |
| Sign in, search the catalogue, save trips, or open History | [Run the full app](#run-the-full-app) |

Install the **.NET 10 SDK** and run the commands below from the TravelTracker repository root.

## Run only the frontend

```sh
dotnet run --project src/TravelTracker.Frontend --launch-profile http
```

Open [TravelTracker](http://localhost:5100/) or go straight to [Map View](http://localhost:5100/map). The preview runs without Supabase, Auth0 configuration, or API processes. The map tiles require internet access. Stop the preview with **Ctrl+C** before starting the full app.

## Run the full app

The C# launcher builds and starts the frontend and all six APIs. Use the **same command in PowerShell, Bash, or a macOS terminal**; PowerShell 7 is not required:

```sh
dotnet run --file scripts/StartSupabaseDev.cs
```

On the first run, it asks for missing settings and saves the non-secret values in `.traveltracker-launch.json` in the repository root. This file is Git-ignored and belongs to this machine. On later runs, use the same command and enter the hidden secret prompts. Stop with **Ctrl+C**.

### First run on each machine

Install the **.NET 10 SDK**, clone the repository, and run from its root. Obtain these settings from your team:

| Launcher setting | Where to find it |
| --- | --- |
| Pooler host or PostgreSQL URI | Supabase project → Connect → Direct → **Session pooler**. Paste the Host or full URI. Input is hidden; any URI password is discarded and requested again privately. |
| Database username | Same panel → User. Include the `.project-reference` suffix; use your assigned runtime role. |
| Root certificate | Supabase Database Settings → SSL configuration. Download it and enter its path **on this computer**. Spaces and surrounding quotes are supported. |
| Auth0 domain and frontend Client ID | Auth0 → Applications → your **frontend** application → Settings. A domain with `https://` is accepted too. |
| Backend Client ID | Auth0 → Applications → your **machine-to-machine** application. It must be authorized for `users.access` on the API. |
| Audience | Your Auth0 API identifier; defaults to `traveltracker-api`. |
| Frontend type | Defaults to `spa` (Single Page Application with PKCE). Use `--setup` to select `regular` for a Regular Web Application, which also prompts for its frontend secret at launch. |

The launcher asks for the **M2M client secret** and **database password** privately at every launch; neither is stored in the settings file. For the shared project, the six `traveltracker_*` databases must already exist. A new teammate needs access to those databases, not a new Supabase project.

The frontend Auth0 application must allow callback `http://localhost:5100/signin-oidc`, logout `http://localhost:5100/`, and the API scopes in the [Auth0 setup guide](docs/frontend-auth0.md). Those provider settings are shared; local configuration and certificates are per machine. Existing `Auth0__Domain`, `Auth0__ClientId`, and `Authentication__Bearer__Audience` environment values override saved values. The launcher can also derive the domain from `Authentication__Bearer__Authority` when there is no saved domain.

Trust the local Users API's HTTPS development certificate once:

```sh
dotnet dev-certs https --trust
```

On Linux, .NET HTTP clients may also need the OpenSSL trust directory set in the terminal that launches the app. For Ubuntu:

```bash
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:${SSL_CERT_DIR:-/usr/lib/ssl/certs}"
```

For other distributions, use the trust path reported by `dotnet dev-certs https --trust --verbose`. Do not disable certificate verification. Windows, Linux, and macOS use the same launcher; OS certificate trust, network access to Supabase/Auth0, and the local certificate path still need setup on each machine. Cross-platform startup must be verified on the target OS.

### Change or check settings

```sh
# Review every setting; Enter keeps the current value. Save without starting services.
dotnet run --file scripts/StartSupabaseDev.cs -- --setup

# Validate saved settings and certificate path without network access or secret prompts.
dotnet run --file scripts/StartSupabaseDev.cs -- --check-config
```

Check mode validates configuration only, not the database password, certificate contents, Auth0 grants, or connectivity. Correct any named setting with `--setup`. For malformed JSON or unsupported fields, correct or rename the local settings file first. Never add passwords, client secrets, or tokens to this file.

The old four-argument command remains supported, with Auth0 values taken from saved settings, environment variables, or prompts. `scripts/Start-SupabaseDev.ps1` remains a legacy launcher; use the C# command above for the guided setup on every OS.

### Sign in and save a trip

1. Enter the Auth0 machine-to-machine secret and the Supabase database password at the hidden prompts.
2. When the launcher reports that TravelTracker is running, open [Map View](http://localhost:5100/map) and sign in. Build an itinerary, choose **Review on map**, then **Save trip**. If you picked new locations or modes, save those catalogue entries first.
3. To verify persistence, copy the saved trip URL, stop the stack with **Ctrl+C**, restart it, sign in again, and reopen the URL or find the trip in **History**.

### Initialize a new Supabase project (maintainer)

Create the six service databases once with [the bootstrap SQL](src/Database/supabase-bootstrap.sql). Run it against the project's default `postgres` database using a role with `CREATEDB`. With PostgreSQL client tools installed, use:

```sh
psql -X -W "host=your-region.pooler.supabase.com port=5432 user=postgres.yourprojectref dbname=postgres sslmode=verify-full sslrootcert='/path/to/supabase-ca.crt'" -f src/Database/supabase-bootstrap.sql
```

Run `dotnet run --file scripts/StartSupabaseDev.cs -- --apply-migrations` for the first schema setup or when there are new EF migrations. Have one maintainer run this step with a migration-capable login. Teammates use their assigned runtime login for everyday development.

## Tests and reference

Offline launcher regression checks (run the help command first to build the launcher):

```sh
dotnet run --file scripts/StartSupabaseDev.cs -- --help
dotnet run --file scripts/TestLauncher.cs
```

Application tests:

```sh
dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"
```

For PostgreSQL tests, set `TRAVELTRACKER_TEST_POSTGRES` to an isolated PostgreSQL instance with `CREATEDB` and use `--filter "Category=PostgreSQL"`. See the [journey test guide](docs/milestone-3/critical-journey.md).

- [API contracts and service URLs](docs/api.md)
- [Trips API and persistence](docs/trips-service.md)
- [First administrator setup](docs/first-administrator-setup.md)
- [Contribution workflow](CONTRIBUTING.md)
