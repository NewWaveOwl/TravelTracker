# Historical shared OTP routing and Photon proposal

**Superseded address-provider decision (2026-11-22):** the owner selected Public
Nominatim instead of a local Photon installation. Follow
[the implemented integration and Windows/tunnel setup](public-nominatim-integration.md).
The original capacity-trial proposal below is retained as historical context.

Status: proposed future work, recorded on 2026-11-22. This document does not mean
that geocoding, OTP integration, or a new server window is implemented in
TravelTracker. The current app still supports manually entered addresses and
coordinates, with approximate map connections.

## Goal and constraints

Automatically suggest a street address when a traveller places a marker, then
offer realistic car, bus, and train itineraries with route geometry. Reuse the
owner's RNDTransitMTL server and Windows launcher rather than running duplicate
OTP instances.

The server has 16 GB RAM and approximately 100 GB storage available for planning.
Confirm whether that storage figure is free space or total disk capacity before
installing data. Windows, existing OTP graphs and schedules, the realtime bridge,
the backend, logs, and database replacement files must all fit within the budget.

This follows [Design sections 7 and 9](design.md): optional routing and address
help must preserve manual planning, explain unavailable estimates, and never save
provider suggestions without the traveller's confirmation. See also
[trip points and routes](trip-points-and-routes.md) for ownership and timing rules.

## Recommendation

Trial **one shared OTP instance plus one self-hosted Photon instance**, initially
using Canadian address data and the existing Montreal-area routing graph. Do not
install a separate Nominatim database for this trial. Photon can serve reverse
lookup and search suggestions from a prepared database, using embedded OpenSearch.

The [Canada Photon download](https://download1.graphhopper.com/public/north-america/canada/index.html)
for the 1.x release series was approximately **4.6 GB compressed** when checked on
2026-11-22. Download size is not installed size or RAM usage. Select matching
Photon and database versions; measure the unpacked size and leave room for an old
and a replacement database during updates. Do not download worldwide data for
this first trial.

The current RNDTransit launcher defaults OTP to a **4 GB Java heap**. This is not
a limit on its entire process memory. The earlier discussion's 4-8 GB regional
geocoder allowance was a rough estimate, not a benchmark or minimum requirement.
Choose Photon heap limits only after measuring the running server; allow room for
native memory, filesystem caching, Windows, and the other services. Start with low
concurrency and benchmark OTP and Photon together before promising capacity.

Photon is free software under Apache 2.0; operating the server and maintaining
data still consume resources. Preserve the required software notices and
OpenStreetMap data attribution. If the regional trial does not fit, keep manual
entry working and evaluate a smaller address index or a hosted geocoder with
explicitly accepted terms and quotas. Nominatim and Pelias remain alternatives,
not additional required services.

## Current evidence and reuse boundary

The inspected RNDTransitMTL source was `main` at
[`706887d`](https://github.com/NewWaveOwl/RNDTransitMTL/tree/706887df7e85768e6440fe97f692e7949c5876ef/backend).
This was a source inspection, not a live server or resource benchmark.

| Already present in RNDTransitMTL | Needed for TravelTracker |
| --- | --- |
| Windows launcher supervising Ktor, OTP, realtime bridge, and an optional HTTPS tunnel | A TravelTracker control window connected to the same service owner |
| OTP queries returning leg geometry, distances, times, street directions, and transit names | A point-to-point route planning contract with a caller-selected departure time |
| A public destination-generation API using duration, modes, and regional policy | Separate route and address endpoints; do not reuse random destination generation for chosen markers |
| Walking, cycling, bus, metro, and train selections in the wrapper | Explicit car-mode support and verification against the imported street graph |
| Generic labels such as "Pinned location", "Current location", and "Random destination" | A reverse lookup result that can populate an editable address |

Canada-wide address coverage would not expand OTP routing coverage. Real routes
remain limited by the imported OSM region, transit feeds, and their service dates.
Scheduled transit, realtime predictions, and estimated road travel times must be
distinguished in the response and UI. No live road-traffic support is claimed.

TravelTracker retains its Blazor frontend and existing .NET APIs and service-owned
PostgreSQL persistence. The shared Kotlin backend supplies routing/geocoding over
HTTP; it does not become the owner of TravelTracker accounts, catalogues, or trips.
Provider-specific responses stay behind that HTTP boundary.

## One Windows application, two control windows

Extend the existing launcher EXE with a TravelTracker control window, opened from
the same application as the RNDTransit window. Both observe one service controller
and one set of shared OTP, bridge, and Photon processes.

- Opening another window never starts another OTP graph or Photon database.
- Closing a control window hides it; an explicit application-level Stop or Exit
  action explains which shared services and clients will be affected.
- Keep app settings, diagnostics, and request limits identifiable by client.
- Keep OTP, the bridge, and Photon private; expose only the required protected
  backend endpoints through HTTPS. Verify TravelTracker authentication and access
  rules explicitly rather than assuming existing RNDTransit sessions are compatible.
- Show service health separately from transit feed freshness and route availability.
- Make Photon optional. Its failure must not trigger the existing launcher's
  whole-stack shutdown behavior or stop RNDTransit routing. Give it separate health
  and recovery handling, with manual address entry available throughout.

Managing TravelTracker's .NET processes from the EXE is a separate packaging and
lifecycle decision. Adding a server control window does not convert the web client
into a native Windows client or relocate its databases.

## Proposed integration contracts

These routes are examples for a future contract, not existing APIs. Select their
final owning TravelTracker API and access policy before implementation; the
frontend continues to use public HTTP DTOs only.

| Proposed shared-backend operation | Request | Response and failure behavior |
| --- | --- | --- |
| `GET /v1/places/reverse` | Validated latitude, longitude, and optional language | Suggested address, available address parts, lookup coordinates, source, and attribution; distinguish no match from temporary unavailability |
| `POST /v1/routes/plan` | Chosen origin/destination, mode, and departure timestamp with offset | Candidate itineraries with per-leg geometry, distance, timing basis, and transit details; distinguish no route, unsupported coverage/date/mode, and provider unavailability |

Apply bounded timeouts, cancellation, caching, and fair request limits so a busy
TravelTracker client cannot monopolize RNDTransit's routing capacity. Avoid logging
raw coordinates, addresses, tokens, or private trip contents. Outside results do
not write to a trip or catalogue automatically.

## Marker address behavior

1. Place a marker, or finish moving it, then request its address once. Do not query
   on every pointer movement.
2. Show lookup progress without blocking map interaction. Display the suggested
   street address prominently and latitude/longitude underneath in smaller text.
3. Keep the address editable and require the normal Add/Update/Save confirmation.
   A lookup result must not change the marker's coordinates.
4. Discard stale results after moving/deleting the marker, changing the active
   stop, leaving the editor, or typing an address manually.
5. If no suitable address exists or the service fails, retain the marker and
   manual fields. A nearby object's address is a suggestion, not an exact-house
   guarantee.

## Delivery and acceptance evidence

1. **Capacity trial:** measure existing idle/loaded OTP memory and disk usage;
   verify Photon and embedded OpenSearch on the target Windows installation;
   measure download, extraction, replacement space, lookup latency, and concurrent
   routing/lookup memory. Keep the current server installation and data recoverable.
2. **Address lookup:** implement the shared endpoint and TravelTracker client;
   verify valid results, missing house numbers, no match, outage, timeout,
   cancellation, stale responses, manual edits, and absence of automatic writes.
3. **Route planning:** implement chosen endpoints and departure times, add cars to
   the wrapper, and verify each supported mode, geometry rendering, transit service
   dates, scheduled/realtime labeling, and unsupported coverage without invented
   routes. Preserve manually entered times when estimates are unavailable.
4. **Launcher integration:** add the second control window and optional Photon
   lifecycle; prove one process owner, no duplicate instances, correct close/stop
   behavior, independent geocoder failure, and request fairness between apps.

Update the affected API/service contracts, `docs/design.md`, and README when each
slice is implemented. Before then, resolve geographic scope, actual free storage,
measured resource limits, protected service access, and which .NET API mediates the
calls. No production deployment or server change is authorized by this plan alone.

## References

- [Photon features, requirements, prepared databases, and license](https://github.com/komoot/photon)
- [Photon embedded deployment and import/update operations](https://github.com/komoot/photon/blob/master/docs/usage.md)
- [RNDTransit backend and launcher](https://github.com/NewWaveOwl/RNDTransitMTL/tree/706887df7e85768e6440fe97f692e7949c5876ef/backend)
- [OTP routing modes](https://docs.opentripplanner.org/en/latest/RoutingModes/)
- [OTP optional geocoder, currently documenting transit stop-cluster search](https://docs.opentripplanner.org/en/latest/sandbox/GeocoderAPI/)
- [Nominatim as an alternative: reverse lookup behavior](https://nominatim.org/release-docs/latest/api/Reverse/)
