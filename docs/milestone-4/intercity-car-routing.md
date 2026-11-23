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

The current gate includes the five startup regressions added independently in
`b689b61`. Earlier local runs reported scheduling timeouts in session notification,
late panel disposal and the two-request parser test. No tests were removed; the
complete final run passed with bounded parallelism:

```powershell
dotnet restore TravelTracker.slnx
dotnet build TravelTracker.slnx --configuration Release --no-restore
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore -m:1 --filter 'Category!=PostgreSQL' --logger 'trx;LogFilePrefix=intercity-car-routing-gate' -- xUnit.MaxParallelThreads=4
```
