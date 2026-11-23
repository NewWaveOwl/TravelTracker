# Recover from database, picture and routing failures

These failures have separate causes. Updating the RNDTransit Windows server does
not migrate TravelTracker's six PostgreSQL databases.

## Picture and trip-save 503 responses

On 2026-11-23, the project's PostgreSQL logs confirmed that `UserPictures` and
`TripStops` were missing. The Users picture endpoints failed with
`PostgresException`; trip validation succeeded, but its database write failed
with `DbUpdateException`. The source already contains the required migrations:

| Database | Pending table | Committed migration |
| --- | --- | --- |
| `traveltracker_users` | `UserPictures` | `20261122215928_ProfilePicture` |
| `traveltracker_trips` | `TripStops` | `20261123004432_TripStops` |

Before stopping the local launcher, open **Trip → Browser draft → Save map draft**
and wait for **Map draft saved in this browser.** This explicit browser copy is
separate from the failed trip save. After restarting and signing in as the same
account in the same browser, reopen the same map URL and use **Restore map draft**.
An existing trip's `/map/{tripId}` and a new trip's `/map` have separate draft keys;
opening the new-trip page cannot restore an existing trip's browser copy.
If the browser copy fails, copy the details you need: restarting can discard the current unsaved session.
Then stop the launcher with Ctrl+C and run once with the configured
migration-capable database role:

```powershell
cd 'K:\CLASSES 26 FALL\WEB_3\ML_4\TravelTracker'
dotnet run --file scripts/StartSupabaseDev.cs -- --apply-migrations
```

The normal hidden Auth-service and database-password prompts still apply. Do not
put passwords in the command, JSON configuration or chat. The launcher applies
each service's committed pending migrations before starting that service. Wait
for `Users database migrations applied.` and `Trips migrations are applied.`
before testing. Permission or migration errors require correcting that failure;
an HTTP health response alone does not verify the schema.

The two migrations add tables and preserve existing account, trip and leg rows.
Use the existing database backup procedure before a shared deployment. Do not
rerun database bootstrap SQL, drop databases, or replace migration history.
If the configured login is a runtime-only role, a maintainer must apply the
migrations with the deployment role and ensure the runtime role has the needed
table permissions. See the [migration inventory](../supabase-migration-verification.md).

After the migration run, later starts use:

```powershell
dotnet run --file scripts/StartSupabaseDev.cs
```

Sign in, reload the profile, and check History before retrying a save. Confirm
one saved trip by reopening it. These live checks and the actual migration run
remain operator verification until their results are observed.

## Picture upload 413 responses

The API accepts a single PNG or JPEG with at most **1 MiB** of input and at most
**512 pixels on each side**. A compressed photo can be below the byte limit and
still exceed the dimensions. Resize/export it before uploading. This validation
is independent of the missing picture table; applying migrations does not change
the upload limits. Failed uploads retain the previous saved picture.

## Cycling options time out or addresses return 503

Cycling uses the RNDTransit routing service through the configured Dev Tunnel.
Address lookup uses that same server, with Public Nominatim as a separate upstream.
The frontend has an eight-second request deadline; the server bounds protected
TravelTracker operations at seven seconds. A timeout identifies a request that
did not complete, not a broken cycling algorithm or a reason to raise all timeouts.

On the server PC, confirm the latest [routing update](server-routing-update.md)
is installed, start the shared server, wait for OTP and tunnel readiness, and copy
the current **Public HTTPS** address. Verify the TravelTracker Supabase URL and
publishable key match the local app's public Auth pair.

During the 2026-11-23 investigation, both anonymous health endpoints answered
200 in under half a second at the configured tunnel. One capabilities request
using a deliberately invalid synthetic credential with the app's expected issuer
returned `503 authentication_unavailable` in 360 ms. That result identifies the
server's missing/mismatched TravelTracker Auth configuration before provider work;
it is not a successful authenticated test. No real account credential or provider
request was used. The operator confirmed the update and tunnel were current.

Read the app's public pair on the development PC:

```powershell
$travelTrackerSettings = Get-Content -Raw .traveltracker-launch.json | ConvertFrom-Json
$travelTrackerSettings | Select-Object SupabaseUrl, PublishableKey
```

Stop shared services on the server PC, open **TravelTracker window → TravelTracker
settings**, set both TravelTracker fields to that pair, Save, then start services
again. Keep RNDTransit's own project settings. If the local app has `Supabase__Url`
or `Supabase__PublishableKey` environment overrides, use that effective pair.

On the development PC, stop the local launcher and enter that current base URL:

```powershell
cd 'K:\CLASSES 26 FALL\WEB_3\ML_4\TravelTracker'
$travelTrackerTunnel = (Read-Host 'Paste the server Public HTTPS base address').Trim().TrimEnd('/')
$env:Services__Routing = $travelTrackerTunnel
dotnet user-secrets set 'Services:Routing' $travelTrackerTunnel --project src/TravelTracker.Frontend

# Anonymous operational status: sends no account token and does not probe providers.
Invoke-RestMethod -Uri "$travelTrackerTunnel/health/traveltracker" -TimeoutSec 10

dotnet run --file scripts/StartSupabaseDev.cs
```

Setting the environment value too avoids a stale `Services__Routing` overriding
the saved user secret in that terminal. Use the base address with no `/v1/...`
suffix. The tunnel must expose Ktor's API port, normally 18088, rather than OTP's
private port. A failed health request must be resolved at the server/tunnel first.
Successful health is operational status, not proof that Auth, Nominatim or a
particular journey works.

Once signed in, open Map tools → Transport; options load automatically. Confirm
Bike is available before Find routes. Search addresses explicitly with Search
or Enter. If health works but protected calls still fail, inspect the server's
TravelTracker status and Auth settings; retain manual entries while resolving
the failure. No new routing-server build is required for the local database
migrations or frontend error-feedback changes.

## Long car routes return 503

Montreal to Trois-Rivieres was reproduced as a fast `OUTSIDE_BOUNDS` response
from the loaded Montreal street map. The adapter incorrectly converted that
normal coverage result into 503. The coverage update returns an explicit
no-route result and keeps the selected route choices. Generating the journey
also requires a graph covering both cities and their connecting roads; increasing
timeouts or installing application JARs does not add those streets. Follow the
[intercity diagnosis and update instructions](intercity-car-routing.md).

## Recovery-change validation

The Release solution build passed with zero warnings/errors. All 1,781
application tests passed, with zero failures or skips, including 727 frontend
tests. The 25 added frontend regressions cover early image-size feedback,
bounded/sanitized routing setup errors, connection failures, retry and retained
edits. All 55 Node keyboard/panel interaction regressions also passed. The
focused picture suite passed 56 tests; the focused combined suite
passed 142, compared with a 117-test baseline. A read-only source review found
no remaining findings. Commands:

```powershell
dotnet restore TravelTracker.slnx
dotnet build TravelTracker.slnx --configuration Release --no-restore --verbosity quiet
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter 'Category!=PostgreSQL' --logger 'trx;LogFilePrefix=startup-recovery' --verbosity quiet
node --test tests/TravelTracker.Frontend.Tests/TripSaveKeyboardTests.mjs tests/TravelTracker.Frontend.Tests/TripMapCanvasKeyboardTests.mjs tests/TravelTracker.Frontend.Tests/MovablePanelTests.cjs tests/TravelTracker.Frontend.Tests/TripDateRangePickerKeyboardTests.mjs tests/TravelTracker.Frontend.Tests/Browser/tripSaveShortcut.test.mjs
```

Local PostgreSQL relational tests were not run because the isolated test
connection was not configured. The existing migrations were reviewed, not
applied to the shared service databases. The live routing check established the
configuration failure, not a successful account/route journey after repair.

Refreshed desktop/phone browser fixtures also passed 41 assertions, including
Bike preview, Cancel, Confirm, Undo and exact-second Save payloads, with zero page
errors. Their Auth/provider responses are simulated; see the
[feature verification record](routing-feature-delivery.md#recovery-fixes-and-publication-verification-2026-11-23).
