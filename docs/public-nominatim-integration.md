# Shared routing and Public Nominatim integration

Owner decision, 2026-11-22: Public Nominatim replaces the proposed self-hosted Photon
trial. Source integration includes the shared Kotlin backend, the Blazor client,
and a second Windows launcher control window. Target-PC deployment and live
tunnel/OTP/provider checks remain separate from automated fixture verification.
No local address database, Photon installation, OTP graph rebuild or database
migration is required for this change.

## Ownership and service behavior

TravelTracker keeps its existing .NET services, Supabase accounts and PostgreSQL
persistence. The Blazor server calls `Services:Routing` through `ProtectedClient`,
which checks the requesting session before and after each call. The Kotlin backend
owns the address-provider translation and point-to-point OTP preview contract.
Suggestions and previews never save catalogue records or trips automatically.

One RNDTransit launcher controller supervises the existing Ktor, OTP, realtime bridge
and tunnel. Opening **TravelTracker** creates another control window, not another
service stack. Closing it hides it; shared Stop/Restart affects both apps. A failed
address lookup does not terminate routing or RNDTransit generation. Existing graph
coverage, GTFS service dates and feed freshness still determine route availability.

## Public Nominatim policy and address UI

The developer explicitly selected the public service after reviewing its
[usage policy](https://operations.osmfoundation.org/policies/nominatim/).

- Search and reverse lookup share one queue: provider request starts are at least
  one second apart across all users. Run one backend instance unless this limiter
  and cache are moved to a shared coordinator.
- Search requires Search or Enter. There is no autocomplete, periodic lookup,
  prefetch, grid scan or bulk request. Reverse lookup follows user marker placement
  or a completed move, never every pointer movement.
- The backend sends an identifying User-Agent, caches repeated results in memory,
  honors provider backoff and makes no automatic retries. The 512-entry cache keeps
  positive matches for 24 hours and no-match results for five minutes. Restart clears
  it; avoid restart-driven repeated queries.
- The address box shows an animated spinner throughout queue waiting and lookup.
  Cancel and manual edits remain available. Moving/removing a marker, changing the
  active stop, typing an address or leaving the view invalidates late results.
- Reverse lookup suggests editable text without changing the marker position.
  Search results require selection and normal marker confirmation. A nearby address
  is not guaranteed to identify the exact house at a chosen coordinate.
- Show OpenStreetMap attribution and keep personal/confidential information out of
  requests. Do not log addresses, coordinates, private trip contents or tokens.
  Authentication headers never go to Nominatim.
- Provider URL is operator-configurable. Manual entry remains usable during service
  failure or access withdrawal; availability of the public service is not guaranteed.

At most four address lookups are admitted at once; excess requests receive 503.
Provider I/O times out after four seconds and an entire operation after seven.
Eight total TravelTracker operations may enter authentication at once; route I/O
has its own two-request limit. These bounds protect the shared server; a queued
request can time out under load while the UI keeps the draft.

## Shared HTTP contract

Address and route operations require a session accepted by the configured
TravelTracker Supabase project. Its public project URL/key may differ from RNDTransit.
Leaving both TravelTracker fields blank selects the existing RND project; partial
configuration is rejected. Unconfigured authentication fails closed. A dependency
outage returns 503 rather than being misreported as a missing address or route.

| Route | Request | Result |
| --- | --- | --- |
| `GET /v1/places/search` | One `q`, 3–200 characters, explicitly submitted | `status: success` or `no_match`; `candidates` with `label`, numeric `lat`/`lon`, `source`; `attribution` |
| `GET /v1/places/reverse` | One finite `lat` (-90..90) and `lon` (-180..180) | Same candidate shape; marker coordinates are preserved by the client |
| `POST /v1/routes/plan` | JSON `origin: {lat, lon}`, `destination: {lat, lon}`, `mode`, `departureTime` with UTC offset | `success`, `no_route` or `unsupported`, plus `itineraries` |

Errors: 400 invalid fields/duplicate parameters; 401 rejected session; 503 dependency
failure, timeout or capacity. Known backoff/capacity responses contain
`retryAfterSeconds` and `Retry-After`. Responses use `Cache-Control: no-store`;
provider-result caching belongs to the bounded server cache.

Modes are `pedestrian`, `bike`, `car`, `bus`, `train`, requesting OTP WALK, BICYCLE,
CAR, BUS or RAIL respectively. Cycling is not accelerated/relabelled walking.
Plane is unsupported. A missing street graph or expired transit schedule does not
produce invented routes.

Itineraries contain `id`, `durationSeconds`, `distanceMeters`, `timingBasis`
(`estimated`, `scheduled`, `realtime`) and `legs`. Legs contain `mode`,
`durationSeconds`, `distanceMeters`, ISO-offset `departureTime`/`arrivalTime`,
encoded-polyline `geometry`, optional `routeName`, and `realtime`. Geometry/timing
must be connected and valid. Previews preserve manually entered trip times; route
geometry is temporary draft data, not a new persisted trip field. No live road
traffic is claimed.

`GET /health/traveltracker` contains aggregate queue/cache/request counters only.
Launcher polling uses this private endpoint and never triggers Nominatim requests.
Ready means Ktor is running, not that a particular provider/feed/route is available.

## Configure the Windows server and Dev Tunnel

1. Update the portable server using the supplied update ZIP/helper or a complete
   replacement package. Follow `backend/launcher/SERVER_GUIDE.md` in RNDbackend.
   Stop shared services and exit the old launcher before replacing JARs. Preserve
   the previous package and `%LOCALAPPDATA%/RNDTransitServer` configuration/imports.
2. Open **TravelTracker** in the server window. While stopped, set TravelTracker's
   Supabase HTTPS project origin and **publishable** key if different from RNDTransit.
   No database password, service-role key or user password belongs in these settings.
3. Keep the existing Microsoft Dev Tunnel publishing Ktor's API port (normally
   18088). OTP and bridge ports remain private. Start and wait for public health
   verification, then copy **Public HTTPS**.
4. On the development PC, from the TravelTracker root, supply that base address:

```powershell
$env:Services__Routing = 'https://YOUR-TUNNEL-18088.YOUR-REGION.devtunnels.ms'
dotnet run --file scripts/StartSupabaseDev.cs
```

For a persistent development setting instead:

```powershell
dotnet user-secrets set 'Services:Routing' 'https://YOUR-TUNNEL-18088.YOUR-REGION.devtunnels.ms' --project src/TravelTracker.Frontend
```

Use the actual **base URL**, without `/v1/...`. Environment values override user
secrets; the normal launcher inherits `Services__Routing`. The default is
`http://localhost:18088`, which is suitable only when Ktor runs on the same machine.
Remote URLs require HTTPS. Align backend TravelTracker Auth settings with the
frontend's Auth project. Do not disable certificate checks to resolve tunnel errors.

The server stores `nominatimUrl` and `nominatimUserAgent` in its existing local
`server.properties`; the launcher passes `NOMINATIM_URL` and `NOMINATIM_USER_AGENT`.
Defaults are `https://nominatim.openstreetmap.org` and
`TravelTracker/1.0 (+https://github.com/NewWaveOwl/TravelTracker)`.
The TravelTracker Auth fields become `TRAVELTRACKER_SUPABASE_URL` and
`TRAVELTRACKER_SUPABASE_KEY`. Change settings while stopped, then restart.

## Verification limits

Routine tests use controlled provider/identity fixtures and do not consume the public
geocoder quota. They cover shared spacing/caching/backoff, actual HTTP engine
no-retry behavior, cancellation/disconnection, no-match/outage, mode selection,
route geometry, UI draft retention and shared launcher ownership. Live Nominatim
address quality, target-PC graph mode/date coverage, authenticated tunnel journeys
and a real server update/rollback require the user's configured environment.

## References

- [Nominatim policy](https://operations.osmfoundation.org/policies/nominatim/)
- [Search](https://nominatim.org/release-docs/latest/api/Search/)
- [Reverse lookup and nearest-object limitations](https://nominatim.org/release-docs/latest/api/Reverse/)
- [OTP mode selection](https://docs.opentripplanner.org/api/dev-2.x/graphql-gtfs/types/PlanModesInput)
- [Supabase verified user lookup](https://supabase.com/docs/reference/javascript/auth-getuser)
