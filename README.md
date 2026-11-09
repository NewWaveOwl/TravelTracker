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

The C# launcher works on **Linux and Windows**. It builds and starts the frontend and all six APIs. You need:

- The Supabase **Session pooler** host and assigned database username from **Connect**, the database password, and the root certificate from **Database Settings**.
- The Auth0 tenant domain and **frontend application Client ID**, plus the **machine-to-machine Client ID and secret** for an application granted `users.access` on `traveltracker-api`.
- Six initialized `traveltracker_*` databases. If your team already shares a Supabase project, ask the maintainer for access to its existing databases.

Configure the frontend client using the [Auth0 setup guide](docs/frontend-auth0.md). This is a one-time step per machine. The guide covers the local callback URL and the grants for profile, catalogue, and trip access.

### Linux (Bash)

Replace the example values with your team's settings. Keep the certificate path quoted if it contains spaces.

```bash
export Authentication__Bearer__Authority='https://your-tenant.us.auth0.com/'
export Authentication__Bearer__Issuer="$Authentication__Bearer__Authority"
export Authentication__Bearer__Audience='traveltracker-api'

dotnet dev-certs https --trust
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:${SSL_CERT_DIR:-/usr/lib/ssl/certs}"

dotnet run --file scripts/StartSupabaseDev.cs -- 'your-region.pooler.supabase.com' 'your-role.yourprojectref' 'your-m2m-client-id' '/path/to/supabase-ca.crt'
```

The `SSL_CERT_DIR` example uses Ubuntu's OpenSSL certificate directory. For another Linux distribution, use the trust path shown by `dotnet dev-certs https --trust --verbose`.

### Windows (PowerShell)

```powershell
$env:Authentication__Bearer__Authority = 'https://your-tenant.us.auth0.com/'
$env:Authentication__Bearer__Issuer = $env:Authentication__Bearer__Authority
$env:Authentication__Bearer__Audience = 'traveltracker-api'

dotnet dev-certs https --trust

dotnet run --file scripts/StartSupabaseDev.cs -- 'your-region.pooler.supabase.com' 'your-role.yourprojectref' 'your-m2m-client-id' 'C:\path\to\supabase-ca.crt'
```

### Sign in and save a trip

1. Enter the Auth0 machine-to-machine secret and the Supabase database password at the hidden prompts.
2. When the launcher reports that TravelTracker is running, open [Map View](http://localhost:5100/map) and sign in. Build an itinerary, choose **Review on map**, then **Save trip**. If you picked new locations or modes, save those catalogue entries first.
3. To verify persistence, copy the saved trip URL, stop the stack with **Ctrl+C**, restart it, sign in again, and reopen the URL or find the trip in **History**.

### Initialize a new Supabase project (maintainer)

Create the six service databases once with [the bootstrap SQL](src/Database/supabase-bootstrap.sql). Run it against the project's default `postgres` database using a role with `CREATEDB`. With PostgreSQL client tools installed, use:

```sh
psql -X -W "host=your-region.pooler.supabase.com port=5432 user=postgres.yourprojectref dbname=postgres sslmode=verify-full sslrootcert='/path/to/supabase-ca.crt'" -f src/Database/supabase-bootstrap.sql
```

Add `--apply-migrations` to the end of the C# launcher command for the first schema setup or when there are new EF migrations. Have one maintainer run this step with a migration-capable login. Teammates use their assigned runtime login for everyday development.

## Tests and reference

```sh
dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"
```

For PostgreSQL tests, set `TRAVELTRACKER_TEST_POSTGRES` to an isolated PostgreSQL instance with `CREATEDB` and use `--filter "Category=PostgreSQL"`. See the [journey test guide](docs/milestone-3/critical-journey.md).

- [API contracts and service URLs](docs/api.md)
- [Trips API and persistence](docs/trips-service.md)
- [First administrator setup](docs/first-administrator-setup.md)
- [Contribution workflow](CONTRIBUTING.md)
