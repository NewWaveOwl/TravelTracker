# Update and run the shared routing server

The routing/authentication changes require updating the Windows server's
application JARs and restarting the frontend. The server remains one
`RNDTransitServer.exe` with a separate **TravelTracker window**. Its existing
graph, Java runtimes, tunnel and external configuration are preserved by the
verified update helper. No PostgreSQL schema migration is part of these features.

Independent profile-picture and trip-stop features do require their existing
Users/Trips migrations when upgrading the local app. The Windows routing update
ZIP does not apply those migrations. For missing-table, upload-size or routing
errors, follow [startup error recovery](startup-error-recovery.md).

## Verified package

Use the new `RNDTransitServer-TravelTracker-Update.zip` in RNDbackend:

```text
backend/launcher/build/updates/TravelTracker-Coverage-20261123/
```

- Runtime source commit: `7ca9005005fc74a2cdb9bf4ffedbf135db1171bc`.
- ZIP size: 15,228,721 bytes; 34 launcher/backend JARs plus the updater,
  manifest and instructions.
- SHA-256: `21FA1C2FA10D47B195E585D16AC0FFE4B87825C80CF231C24B2A82ABE7E1C998`.

This supersedes the earlier M4, Nominatim-only and PowerShell-fix ZIPs. It corrects
out-of-map car requests being reported as 503. All 137 backend and 60 launcher
tests passed. [Intercity routing](intercity-car-routing.md) also requires map
coverage for both cities and connecting roads; this application ZIP preserves
the existing graph.

Archive JAR hashes/sizes and the bundled TravelTracker query/auth classes were checked against
the manifest; both distributions built without warnings. Eight isolated updater
checks passed, and packaged CheckOnly succeeded with the genuine prior portable
JARs copied to an isolated fixture. The actual remote server was not changed by
these checks. `package-evidence.json` and `SOURCE-COMMIT.txt` sit beside the ZIP;
the source commit is also recorded in its README.

## Match the sign-in project

On the development PC:

```powershell
cd 'K:\CLASSES 26 FALL\WEB_3\ML_4\TravelTracker'
$travelTrackerSettings = Get-Content -Raw .traveltracker-launch.json | ConvertFrom-Json
$travelTrackerSettings | Select-Object SupabaseUrl, PublishableKey
```

These are public application identifiers. Use this exact pair in the server's
**TravelTracker window → TravelTracker settings**: `SupabaseUrl` goes in
**TravelTracker Supabase URL** and `PublishableKey` goes in **Publishable / legacy
anon key**. Keep RNDTransit's separate project settings. Save while the shared
server is stopped, then start it. Environment overrides of the frontend's Auth
project must match too. User passwords, database passwords and service-role keys
do not belong in these fields.

A missing/mismatched routing project now reports a configuration failure while
retaining the app session. Sign out and back in once if the previous version
already revoked your local session after its Routing401 response.

## Apply the supplied update ZIP on the server PC

1. Stop the shared server and fully exit its launcher, including the tray icon.
2. Copy the supplied `RNDTransitServer-TravelTracker-Update.zip` to the server and
   extract its contents into `D:\SERVER 2026\update`, replacing the older extracted
   update files.
3. Run the compatibility/hash check, then apply:

```powershell
cd 'D:\SERVER 2026\update'

powershell -NoProfile -ExecutionPolicy Bypass -File .\Apply-TravelTrackerUpdate.ps1 -ServerDirectory 'D:\SERVER 2026\rnd_tranzit\RNDTransitServer' -UpdateDirectory 'D:\SERVER 2026\update' -CheckOnly

# Continue after CheckOnly reports a compatible verified package.
powershell -NoProfile -ExecutionPolicy Bypass -File .\Apply-TravelTrackerUpdate.ps1 -ServerDirectory 'D:\SERVER 2026\rnd_tranzit\RNDTransitServer' -UpdateDirectory 'D:\SERVER 2026\update'

& 'D:\SERVER 2026\rnd_tranzit\RNDTransitServer\RNDTransitServer.exe'
```

The helper prints the backup/rollback location and refuses incompatible JAR sets,
modified payloads or running processes. Keep that backup. See the ZIP's README and
RNDbackend's `backend/launcher/SERVER_GUIDE.md` for rollback. Supplying
`-UpdateDirectory` explicitly also avoids the older PowerShell default-path bug.

Open **TravelTracker window**, save the matching Auth pair above, then start the
shared server. Wait for services/tunnel readiness and copy **Public HTTPS**.
The patch preserves the graph: train routes and service dates depend on the data
already imported on that PC. Capabilities and route results show its available
modes and known coverage; update/import its GTFS separately if needed.

## Restart the frontend and local APIs

Stop the previous local launcher with Ctrl+C, then run on the development PC:

```powershell
cd 'K:\CLASSES 26 FALL\WEB_3\ML_4\TravelTracker'
$travelTrackerTunnel = Read-Host 'Paste the server Public HTTPS base address'
dotnet user-secrets set 'Services:Routing' $travelTrackerTunnel --project src/TravelTracker.Frontend
dotnet run --file scripts/StartSupabaseDev.cs
```

The base address has no `/v1/...` suffix. An existing `Services__Routing`
environment value takes precedence over user secrets; keep it aligned with the
same tunnel if one is set. The launcher prompts for the internal Supabase Auth
service-account password and PostgreSQL database password separately. Use a
traveller account for browser sign-in.

Open `http://localhost:5100/map`, sign in, search explicitly for an address, and
review the location before applying it. For routes, open **Map tools → Transport**,
wait for routing options, choose the endpoints/date/mode, Find routes, select an
alternative and **Confirm selected route**. Manual map selection remains available
when geocoding or routing is unavailable. A route preview changes the itinerary
only after its confirmation action; normal trip Save persists the chosen plan.
Save stores the chosen mode, times and confirmed route shape. On reopen, a
connection with saved geometry follows that path. Manual connections and
trips saved before the geometry migration display approximate lines.

The package is built/tested locally. Actual server-PC application, authenticated
Dev Tunnel journeys and live Public Nominatim results require that configured
environment; packaging alone does not establish deployment.
