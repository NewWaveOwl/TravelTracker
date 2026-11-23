# Real OTP mode verification (#106)

Observed locally on 2026-11-22 using the existing Montreal assets, an isolated
loopback-only OTP process on port 21885, and no realtime updater or geocoder calls.
These are real OTP GraphQL responses, not adapter fixtures. The installed Windows
server, tunnel and original graph/configuration were not changed.

## Runtime and loaded data

- OpenTripPlanner 2.10.0, commit 521cd5d, graph serialization 277.
- Microsoft OpenJDK 25.0.5.0.2 OTP runtime; Kotlin build uses the existing Java 21
  toolchain separately.
- Router time zone America/Toronto. Loaded STM routes include BUS and SUBWAY; the
  loaded EXO_TRAINS feed contains five RAIL routes.
- The graph reports a global service-time range of
  `2026-08-24T04:00:00Z` to `2027-01-04T05:00:00Z`. This is a union range, not proof
  that each route/feed runs on every date. Its upper boundary is local midnight
  after January 3. OTP does not expose per-feed dates/bounds in this metadata
  contract; unknown values must stay unknown.

The JAR and graph used for this run were already present under
`K:\CLASSES 26 FALL\APP_DEV\tools\otp-montreal`; no dataset download was performed.
The table also records the available neighboring OSM/GTFS inputs for reproduction.
There is no retained build manifest tying those exact three source-file hashes to
the existing graph, so that provenance is unverified. The isolated graph copy's
hash matches the listed graph; its live metadata confirms the loaded feed IDs and
modes. A fresh graph build was not run for these smoke requests.

| Asset | Bytes | SHA-256 |
| --- | ---: | --- |
| otp.jar | 191382029 | `5A5D1EFD30B19295418AC49AD048C5F5CAC341AF63DD41D5E2AE5AD9E7B37885` |
| stm-scheduled/graph.obj | 280853822 | `BCD2FBEEAECCD75AB88666503C69F822D1804B03A57CA56171706B8B31FD2A86` |
| Montreal.osm.pbf | 85533718 | `F1541B75E5422FFAEBCEF4BB5FF263ED2F4EE0ADE2EFCCE650BF7FDAAEFD36E0` |
| stm-gtfs.zip | 69093778 | `CE03A0A0B1FE5FBB9FB4D74ED574185B2400B00E469DE4BB59E58820DAB4609B` |
| exo-trains-gtfs.zip | 118805 | `C258FB6EE3B5B7F820C83E0813AF1FB17F4F21A0E5D92E69340CD26DD8905B19` |

## Sample journeys

All requests used `backend/src/main/resources/plan.graphql`, departure
`2026-11-23T13:00:00Z` (08:00 in America/Toronto), and explicitly excluded realtime
updates. Walking/cycling/car/bus used origin 45.5088,-73.5686 and
destination 45.5312,-73.6150. Rail used 45.4735,-73.6035 to 45.4953,-73.5705.

| Requested mode | Alternatives | First duration (seconds) | First distance (metres) | Returned leg modes |
| --- | ---: | ---: | ---: | --- |
| WALK | 1 | 3742 | 4656.6 | WALK |
| BICYCLE | 1 | 1258 | 5064.8 | BICYCLE |
| CAR | 1 | 513 | 4885.6 | CAR |
| BUS | 4 | 1849 | 4906.3 | WALK → BUS → WALK |
| RAIL | 4 | 530 | 3660.6 | WALK → RAIL → WALK |

Every response had no GraphQL or routing errors, nonempty geometry and the actual
requested leg mode. The three direct modes had different encoded-geometry hashes;
cycling/car are not relabelled or accelerated walking. Bus/rail responses included
named boarding/alighting locations and scheduled departure/arrival timestamps.
Road times are estimates, with no live traffic claim.

Raw requests/responses and independently parsed summaries remain in the ignored
RNDbackend output directory:

```text
backend/build/routing-evidence/M4-106-c2174980326c4bc581788f0afb6032b7/
```

The new RNDbackend `backend/scripts/Test-TravelTrackerRouting.ps1` also reproduced
all five results in a fresh PowerShell process against the owned router, and in
its default isolated mode. The latter retains a complete report under
`backend/build/routing-evidence/M4-106-default-isolated-20261122/`, explicitly
marking loaded binary/graph hashes separately from available source inputs. The
`Prepare-TravelTrackerOtp.ps1` companion creates a new scheduled-only graph from
selected regional OSM/STM/optional EXO inputs and writes a build manifest; it
refuses an existing destination. Its existing-directory safety check passed. A
fresh full graph build was not run during this verification.

The later #108 TravelTracker-only query was captured separately under
`backend/build/routing-evidence/M4-108-root-tt-query-20261122/`; all five modes and
11 alternatives passed live queries and offline production-adapter normalization.
That query supplies named transit stop IDs, positions and intermediate stops;
the sampled bus alternatives reported 15–17 intermediate stops. The verification
script now defaults to `traveltracker-plan.graphql`; use its `-QueryFile` option
with `backend/src/main/resources/plan.graphql` to select the historical #106 query.

This proves the sampled journeys on this exact graph/date. It does not prove
national coverage, every point/date, the user's installed graph, production memory
capacity, authenticated tunnel integration or deployment. Runtime capability
metadata distinguishes schema support, loaded routes and unknown data; an actual
route request remains the test for a selected journey. Deterministic adapter tests
separately verify no-route, unsupported data, timing, validation and provider
failure behavior without consuming public geocoder quota.
