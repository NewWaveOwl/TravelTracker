# TravelTracker

TravelTracker is a .NET 10 trip planner with a Blazor frontend, six local APIs and PostgreSQL storage. Sign-in is built in: the Users API checks passwords against its own PostgreSQL database and issues short-lived signed tokens that every other API verifies. No external identity provider is needed. Supabase works as an ordinary PostgreSQL server through a launcher preset.

**Live demo:** [Open TravelTracker](https://traveltracker-frontend.politebush-89a65c90.canadacentral.azurecontainerapps.io).
Sign in to save trips and view History. Address lookup and route previews require
the Windows routing server and tunnel to be running. See the
[Azure demo deployment guide](docs/azure-demo-deployment.md) for operation,
service-token renewal, and hosting limits.

| To do this | Start here |
| --- | --- |
| Preview the UI, map, and manual markers | [Run only the frontend](#run-only-the-frontend) |
| Sign in, search the catalogue, save trips, or open History | [Run the full app](#run-the-full-app) |
| Use Supabase as the PostgreSQL server | [Supabase as the database](docs/supabase.md) |

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

Use the same launcher command in PowerShell, Bash, or a macOS terminal; PowerShell 7 is not required. The quickest start needs podman or docker and starts a disposable PostgreSQL together with the whole stack:

```sh
dotnet run --file scripts/StartDev.cs -- --local-db --apply-migrations   # first run only
dotnet run --file scripts/StartDev.cs -- --local-db
```

The database container stops when you press Ctrl+C, and its data is kept for the next launch. To run the database on its own, or to wipe its data, use `dotnet run --file scripts/StartLocalDb.cs [-- --reset]` in one terminal and set `TRAVELTRACKER_DATABASE="Host=localhost;Port=54330;Username=postgres;Password=dev;Maximum Pool Size=5"` for the launcher in another.

`TRAVELTRACKER_DATABASE` is any Npgsql connection string. Without it the launcher asks for one at a hidden prompt. To use Supabase instead, pass `--supabase` (see [Supabase as the database](docs/supabase.md)).

The launcher builds the solution and starts the frontend plus Users, Attractions, Transport, Accommodations, Settings and Trips APIs. Each launch generates a fresh signing key pair in memory and gives the private half only to the Users API and the public half to every API, so nothing about sign-in is saved to disk and every restart signs everyone out. Passwords and tokens are never saved in the launch settings.

On Windows and macOS, trust the local Users API HTTPS certificate once with `dotnet dev-certs https --trust`. On Linux the launcher needs nothing: it exports the public development certificate to a temporary folder and points OpenSSL at it for that launch only. Do not disable certificate validation.

### Check settings

```sh
# Validate the database choice without network access or prompts.
dotnet run --file scripts/StartDev.cs -- --check-config
dotnet run --file scripts/StartDev.cs -- --supabase --check-config
```

Check mode validates configuration shape only; it does not test database access. `TRAVELTRACKER_LAUNCH_CONFIG` selects an alternate local file for the Supabase preset's saved, non-secret settings.

### Sign in and save a trip

1. Create the first administrator by following [First administrator setup](docs/first-administrator-setup.md), or just register as a traveller.
2. When startup completes, open [TravelTracker](http://localhost:5100/), choose **Create an account** and register with an email and a password of at least 8 characters. There is no email confirmation or password reset; ask an administrator for account recovery.
3. Build a plan in [Map View](http://localhost:5100/map), then use the workspace **Save trip** button or **Ctrl+S** (**Cmd+S** on macOS). Save any new catalogue entries explicitly in review first. Save errors keep your draft available for correction or retry. Empty and single-stop plans are supported; any supported place category may start or end the trip.
4. Copy the saved trip URL, stop the stack with **Ctrl+C**, restart and sign in again (a restart invalidates sessions), then reopen that URL or find the trip in **History**.

A service save still requires a name, time zone and valid start/end. A single stop needs no invented journey or transport; see the [minimal save contract](docs/trips-service.md#minimal-draft-and-save-fields-105). Saved-trip details offer **Tickets & bookings → Import tickets** for a private, editable PDF draft. Extraction never saves or changes the trip; see [supported formats and limits](docs/ticket-import.md).

The launcher prints the internal service token's expiry (12 hours). Restart before it expires; it does not silently refresh the service token.

In **Map View**, each floating panel has a box icon. Click it to collapse or expand the panel. Double-click or double-tap it, or press **Home**, to restore the panel's position and expand it without clearing trip edits. Drag the header or icon with a mouse or touch; keyboard users can focus the icon and use arrow keys (Shift for larger steps). On phones, panels start collapsed. **New trip** and **Save trip** stay at the top right of the map. The **Trip** tab keeps its panels in place without movement controls. See [the current UI reference](docs/milestone-4/canonical-ui-alignment.md).

In the **Map** and **Trip** workspaces, choose the beginning and ending days together in the **Trip duration** calendar. A newly selected range includes both full days in the trip's time zone; each connection keeps its own departure and arrival times. The map's **Recenter** control returns to the first trip stop, or the initial map position when the trip has no stops.

Phones use larger map controls, wrapping form actions and scrollable menus. Map panels start collapsed in portrait and short touch-screen landscape layouts; tap a panel icon to open it. See [phone layout guidance and browser verification](docs/milestone-4/phone-layout.md).

Place forms show the **street address** prominently, with the selected map position underneath in smaller gray text. Search explicitly for an address or choose a point on the interactive map, then confirm it. Attractions, accommodations, transport points and trip markers support map entry without typing numeric coordinates. Optional numeric adjustments remain in closed Advanced details. An unknown address keeps its location unselected until you choose it.

### Shared routing and address lookup

The map can suggest editable addresses through Public Nominatim and preview chosen
routes through the shared RNDTransit Windows server. Address search requires
**Search address** or Enter; it does not search while typing. A spinner inside the
address box covers queue waiting and lookup. Manual entry remains available.

Routing is optional. Development defaults to `http://localhost:18088` for a local
server. Production starts without routing configured; to enable it, set
`Services__Routing` to an HTTPS base URL.

Set the server's **Public HTTPS** Dev Tunnel base address before launching:

```powershell
$env:Services__Routing = 'https://YOUR-TUNNEL-18088.YOUR-REGION.devtunnels.ms'
dotnet run --file scripts/StartDev.cs
```

Use the base URL without an API suffix. Configure the same TravelTracker Auth
project in the server's TravelTracker settings. See the [integration, public-service
rules and Windows update guide](docs/public-nominatim-integration.md).

In **Map tools → Transport**, routing options load automatically for a connection,
and its palette-selected transport is preselected even before a departure is set.
Choose origin, destination, departure and a supported mode. Find and select a route, review its
stops/times, then **Confirm selected route** to apply mode and exact times to an
adjacent connection. Cancel preserves the draft. Save persists the chosen plan;
provider geometry stays in the current session. See the [server update and full
app commands](docs/milestone-4/server-routing-update.md).

**Profile pictures:** Account → Edit profile → **Upload photo** uses the protected Users picture API and migration merged in [PR #137](https://github.com/NewWaveOwl/TravelTracker/pull/137), now included from `milestone_4`. Choose one static PNG or JPEG up to 1 MiB and 512 × 512 pixels. File selection saves the photo separately from profile text edits; a rejected upload keeps the previous photo. See [picture UI behavior and validation](docs/milestone-4/profile-picture-ui.md).

### Troubleshooting database login

`28P01: password authentication failed` is a PostgreSQL credential error. Check that the host, username and password belong to the same server and role. Stop and restart the launcher to re-enter the password.

If profile-picture reads/uploads or trip saves return 503 after an update, check
the database error before retrying. Missing `UserPictures` or `TripStops` tables
mean the existing service databases need the committed EF migrations. Preserve
unsaved work with **Browser draft → Save map draft**, then stop the local stack
with Ctrl+C and run once with a migration-capable database role:

```sh
dotnet run --file scripts/StartDev.cs -- --apply-migrations
```

Wait for the Users and Trips migration-success messages. Later launches use the
normal command without the flag. This updates the existing databases; do not run
the database bootstrap again. See [startup error recovery](docs/milestone-4/startup-error-recovery.md)
for the separate image-size and routing/tunnel failures.

### Create the service databases (maintainer)

Create the six service databases with [the bootstrap SQL](src/Database/bootstrap.sql), using the server's default `postgres` database and a role with `CREATEDB`. `scripts/StartLocalDb.cs` does this for its container. Then run `dotnet run --file scripts/StartDev.cs -- --apply-migrations` for the initial schema or new EF migrations; use a migration-capable role. Never recreate shared databases to resolve a credential error.

## Tests and reference

Offline launcher configuration checks (no live database):

```sh
dotnet run --file scripts/TestLauncher.cs
```

Application tests:

```sh
dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"
```

For PostgreSQL tests, set `TRAVELTRACKER_TEST_POSTGRES` to an isolated PostgreSQL instance with `CREATEDB` and use `--filter "Category=PostgreSQL"`.

`dotnet run --file scripts/TestDatabase.cs -- up` starts a disposable PostgreSQL 17 container (podman if installed, otherwise docker; override with `TRAVELTRACKER_CONTAINER_RUNTIME`) and prints the `TRAVELTRACKER_TEST_POSTGRES` value to use. `-- env` prints only the connection string and `-- down` removes the container. CI uses its own service container and does not run this script. See the [journey test guide](docs/milestone-3/critical-journey.md).

- [API authentication](docs/authentication.md)
- [Supabase as the database](docs/supabase.md)
- [Supabase PostgreSQL migration audit and staging verification](docs/supabase-migration-verification.md)
- [Environment configuration and secrets](docs/environment-configuration.md)
- [API contracts and service URLs](docs/api.md)
- [Trips API and persistence](docs/trips-service.md)
- [First administrator setup](docs/first-administrator-setup.md)
- [Contribution workflow](CONTRIBUTING.md)
