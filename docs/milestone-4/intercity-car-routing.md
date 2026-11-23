# Montreal to Trois-Rivieres car routing

The 2026-11-23 report was reproduced against an isolated copy of the existing
Montreal OpenTripPlanner 2.10 graph. A CAR request from 45.5088,-73.5686 to
46.3432,-72.5477 returned no edges and `OUTSIDE_BOUNDS` for `TO` in **195 ms**.
The destination is outside that street graph. This response does not indicate a
long-running request or establish a reason to increase timeouts.

The shared adapter incorrectly classified this normal routing result as a provider
failure, producing HTTP 503. Backend commit
[`7ca9005`](https://github.com/NewWaveOwl/RNDTransitMTL/commit/7ca9005005fc74a2cdb9bf4ffedbf135db1171bc)
now maps `OUTSIDE_BOUNDS` to HTTP 200 with `status: no_route`,
`reason: outside_coverage` and an empty itinerary list. Disconnected direct roads
(`NO_DIRECT_MODE_CONNECTION`) also return a normal no-route result. Unknown
provider errors and outages still return sanitized 503s. Authentication, capacity
limits and the existing seven-second server/eight-second frontend deadlines are
unchanged.

The frontend explains that the loaded street map must cover both places and the
connecting roads. The selected endpoints, car mode and departure remain available;
no candidate is offered and Find routes remains usable. Repeating the same request
cannot supply missing map data. A road route requires an expanded graph; the
application update alone preserves the existing graph.

## Application update

The new backend update is:

```text
RNDbackend/backend/launcher/build/updates/TravelTracker-Coverage-20261123/
  RNDTransitServer-TravelTracker-Update.zip
```

- Runtime source: `7ca9005005fc74a2cdb9bf4ffedbf135db1171bc`.
- ZIP size: 15,228,721 bytes.
- SHA-256: `21FA1C2FA10D47B195E585D16AC0FFE4B87825C80CF231C24B2A82ABE7E1C998`.
- 34 application/dependency JARs; payload hashes and sizes verified from the ZIP.
- Source commit embedded in the archive and recorded beside it.

Use the existing [stopped-server update and rollback procedure](server-routing-update.md#apply-the-supplied-update-zip-on-the-server-pc),
substituting this ZIP for the older M4 package. Both installDist distributions
rebuilt successfully. All **137 backend and 60 launcher tests** passed with no
failures, errors, skips or compiler warnings. The five new backend regressions
include both outside endpoints, disconnected car routing, unknown-error handling
and the authenticated HTTP 200/503 distinction. Eight isolated updater checks
and the packaged compatibility CheckOnly against genuine prior portable JARs
passed. These checks did not change the actual server PC.

## Verified regional map

A separate graph was built from the
[Geofabrik Quebec extract](https://download.geofabrik.de/north-america/canada/quebec/)
dated 2026-11-22. The complete source download matched its publisher checksum.
Osmium 1.19.1 selected the corridor bounded by longitude -74.6 to -72.05 and
latitude 45.0 to 46.7, preserving complete ways and matching turn restrictions
and street-area relations. The extract's way-reference check reported zero
missing nodes. The selected source is 156,408,049 bytes, with SHA-256
`80D8C65E84618ACB7A5A4BF73937405507C4AC0AEC18032FA83420051E293818`.

The existing preparation script built a new scheduled graph in an isolated
directory in approximately 214 seconds, using the same STM and EXO_TRAINS
archives. Both feed hashes still match the retained existing data. The graph is
355,888,004 bytes, SHA-256
`7C6D4D4B9CE65C50E1005DF545CB4129D753614B19AEE2455A62A002B156AF19`.
The original graph's hash is unchanged. No installed server data was replaced.

Real requests on this exact graph, with departure 2026-11-23 at 08:00 in
America/Toronto, produced:

| Car sample | Distance | Estimated duration |
| --- | --- | --- |
| Montreal to Trois-Rivieres | 137.37 km | 5,462 seconds (91.0 minutes) |
| Trois-Rivieres to Montreal | 136.97 km | 5,413 seconds (90.2 minutes) |

The walking, cycling, bus and rail samples also succeeded. Production-adapter
replay accepted all five outbound modes and 11 alternatives, plus the return car
candidate. The verification tool compares equivalent JSON numbers by value;
[`7ca4693`](https://github.com/NewWaveOwl/RNDTransitMTL/commit/7ca4693d3475cda887876ce0d023f323232d4e02)
adds six regressions preserving rejection of changed, missing, string or nonfinite
coordinates. The latest backend gate passed **143 backend and 60 launcher tests**.
This test-tool change does not require rebuilding the runtime ZIP above.

The actual car geometry also passed the production frontend's endpoint and
geometry validation through simulated HTTP with an isolated test session:
9,131 encoded characters/3,181 points outbound and 9,371/3,286 on the return.
This does not establish a real account/tunnel journey or browser interaction.
Both owned private OTP processes were stopped after recording their results.

The map archive is:

```text
RNDbackend/backend/launcher/build/updates/TravelTracker-RegionalMap-20261123/
  TravelTracker-Montreal-TroisRivieres-Map.zip
```

- ZIP size: **276,912,512 bytes** (about 264 MiB).
- SHA-256: `49240965425E623F4C231C824566023BA5D7E5AEE512CE3C4A080E5646E2E9C5`.
- All 34 payload files were checked against the archive manifest by size and
  SHA-256. Archive paths use forward slashes.
- Includes the graph, exact regional OSM input, ODbL attribution, extraction
  recipe, build/source manifests, original coverage failure and successful route
  evidence. It contains no server feed credentials or Auth session.

Apply the application ZIP and this separate map package on the stopped server.
Follow the map archive's **README.md**: verify its graph checksum and the server's
STM/exo schedule hashes, copy the existing prepared data to a new staging folder,
replace the graph/build configuration in that copy, then use **Setup / import**
with a new empty destination. Keep the previous data folder for rollback. This
graph contains STM and EXO_TRAINS; a server with additional feeds needs a matching
rebuild instead. Restart the server and local TravelTracker launcher, then verify
Find routes and Confirm selected route with a real signed-in account.

This is a regional map, not national coverage. The supplied archive and successful
local requests do not establish installation on the user's server PC, public
tunnel authentication or live traffic/realtime predictions.

## Source and validation evidence

The original raw request, response, router enum and reproduction hashes are in
RNDbackend's ignored `backend/build/routing-evidence/long-car-20261123/` directory.
The original graph was not modified. The new frontend regression exercises the
real RoutingClient through a controlled HTTP response; it verifies the retained
Montreal/Trois-Rivieres car request and map-expansion guidance. It failed before
the guidance change and passed afterward. The focused suite increased from 63
to 64 passing tests.

TravelTracker's Release solution build passed with **zero warnings and errors**.
All **1,809 application tests**, including **755 frontend tests**, passed with
zero failures or skips. Local PostgreSQL tests were not run because
`TRAVELTRACKER_TEST_POSTGRES` is not configured; no database or migration changed.
The read-only source review found no actionable findings. A successful protected
request through the remote tunnel and installation on the server PC remain
separate deployment checks.

[CI run 45209487392](https://github.com/NewWaveOwl/TravelTracker/actions/runs/45209487392)
passed for runtime head `49d8137`: 1,809 application tests, 85 PostgreSQL tests
and 55 Node interaction cases, with zero build warnings/errors. Host smoke,
database migration and Supabase hook checks also passed.

The current gate includes the five startup regressions added independently in
`b689b61`. Earlier local runs reported scheduling timeouts in session notification,
late panel disposal and the two-request parser test. No tests were removed; the
complete final run passed with bounded parallelism:

```powershell
dotnet restore TravelTracker.slnx
dotnet build TravelTracker.slnx --configuration Release --no-restore
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore -m:1 --filter 'Category!=PostgreSQL' --logger 'trx;LogFilePrefix=intercity-car-routing-gate' -- xUnit.MaxParallelThreads=4
```
