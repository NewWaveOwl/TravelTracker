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

Ordinary location entry (#120) uses the interactive map or a submitted address in
Attractions, Accommodations, Transport points and the trip editor. Coordinates stay
under the address in small gray text; optional numeric adjustments are inside
closed Advanced details. Embedded pickers use a textarea and explicit Search
button so address lookup cannot submit a surrounding save form. Unknown addresses
never silently select the map's initial center. Use confirms the private picker
buffer; Cancel preserves the parent form and existing saved coordinates.

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

Missing routing Auth configuration, or a session issued by another Auth project,
returns `503 authentication_unavailable` with setup guidance. An unverified JWT
issuer can only reject the request; a matching issuer still requires the configured
Supabase `/auth/v1/user` verification. A Routing-only 401 no longer revokes the app's
session. Expired/missing local sessions still require sign-in, and a 401 from the
app's own APIs retains the existing session-revocation behavior.

For the four routing/address paths, the frontend recognizes only the
`authentication_unavailable` code from a bounded 4 KiB JSON 503 body and shows its
own fixed setup guidance. Backend diagnostic messages are never forwarded.
Connection/time-limit failures explain the service/tunnel checks and re-enable
retry; provider failures keep manual planning available. The eight-second client
deadline is unchanged. See [startup error recovery](milestone-4/startup-error-recovery.md)
for the separate database migrations and server Auth settings.

| Route | Request | Result |
| --- | --- | --- |
| `GET /v1/places/search` | One `q`, 3–200 characters, explicitly submitted | `status: success` or `no_match`; `candidates` with `label`, numeric `lat`/`lon`, `source`; `attribution` |
| `GET /v1/places/reverse` | One finite `lat` (-90..90) and `lon` (-180..180) | Same candidate shape; marker coordinates are preserved by the client |
| `GET /v1/routes/capabilities` | No query parameters; same protected session | Router version when known, loaded feeds/global service range, per-mode support and attribution |
| `POST /v1/routes/plan` | JSON `origin: {lat, lon}`, `destination: {lat, lon}`, `mode`, `departureTime` with UTC offset, optional IANA `timeZone` | `success`, `no_route` or `unsupported`, plus `itineraries`, normalized `timeZone` and `requestedDepartureTime` |

Errors: 400 invalid fields/duplicate parameters; 401 rejected session; 503 dependency
failure, timeout or capacity. Known backoff/capacity responses contain
`retryAfterSeconds` and `Retry-After`. Responses use `Cache-Control: no-store`;
provider-result caching belongs to the bounded server cache.

Modes are `pedestrian`, `bike`, `car`, `bus`, `train`, requesting OTP WALK, BICYCLE,
CAR, BUS or RAIL respectively. Cycling is not accelerated/relabelled walking.
Plane is unsupported. A missing street graph or expired transit schedule does not
produce invented routes.

Capabilities (#106) are read from the running OTP instance and cached for 30
seconds. `modes` entries contain `code`, `status` (`available`, `unsupported`,
`unknown`), `basis` and optional `reason`. `router_schema` means the direct mode is
supported by the router's schema; `loaded_routes` means the transit mode appears
in its loaded feeds. Neither guarantees a route for every point/date. The response
also identifies the router, lists feed IDs/modes and reports attribution.

`coverage` includes `timeZone`, inclusive `startDate`/`endDate` when known, exact
`serviceStart` and `serviceEndExclusive`, `serviceRangeBasis: global_union`, feeds
and bounds. Global dates are a union across feeds; per-feed dates and street
bounds remain null when the router does not expose them. Missing version/time-zone
metadata stays unknown. No public provider request or route calculation is made
by this metadata operation. See [real five-mode verification](milestone-4/otp-mode-verification.md)
for exact local OTP/Java/data hashes and sampled journeys.

Walking/cycling previews (#107) retain the selected endpoint order and actual OTP
WALK/BICYCLE geometry, distances and durations. A supplied `timeZone` must be a
recognized IANA ID whose valid offset at the requested local departure matches
`departureTime`. Impossible daylight-saving times are rejected; either explicit
valid offset can resolve a repeated time. Omitted or JSON-null zone preserves
the original four-field caller contract. Requests never fall back to the server's
local zone. The response keeps the requested offset-aware departure and zone;
provider timestamps retain their actual instants.

Invalid points fail before provider I/O. Returned paths must reach the selected
endpoints in order, contain valid geometry/counts and connected timestamps, and
have finite distances and durations consistent with those timestamps. Waiting
between transit legs belongs to the itinerary span. Known no-route outcomes
remain distinct from malformed-provider, timeout and outage failures. No fallback
straight line or invented travel time replaces a failed route.

Itineraries contain `id`, `durationSeconds`, `distanceMeters`, `timingBasis`
(`estimated`, `scheduled`, `realtime`), actual `departureTime`/`arrivalTime`,
`transferCount`, `initialWaitSeconds`, `elapsedFromRequestedDepartureSeconds` and
`legs`. Duration spans the first departure through final arrival; elapsed time
also includes waiting between the requested instant and that first departure.
Legs contain `mode`, `durationSeconds`, `distanceMeters`, ISO-offset effective
`departureTime`/`arrivalTime`, original scheduled times, encoded-polyline `geometry`,
optional route name/ID/headsign, timing basis and `realtime`. Geometry/timing
must be connected and valid. Previews preserve manually entered trip times; route
geometry is temporary draft data, not a new persisted trip field. No live road
traffic is claimed.

Car/transit previews (#108) use actual OTP CAR, BUS or RAIL routing. Successful
routes include `coverage` when metadata is available and `reason: null`.
Unsupported mode/dataset returns no itinerary with `unsupported_mode` or
`mode_not_in_dataset`. Actual router no-route errors distinguish
`outside_service_period`, `location_not_found`, `no_stops_in_range` and `no_route`.
The global service-date union alone never rejects a journey; the router decides
whether that selected date has service. Optional metadata failure leaves coverage
unknown and does not falsely declare a mode unsupported.

Leg `from`/`to` contain `name`, numeric `lat`/`lon` and optional `stopId`, using
reported provider positions or verified geometry endpoints when absent.
`intermediateStops` uses the same shape: null means not supplied, whereas an empty
array means no reported intermediate stops. Transfer counts retain the provider's
count where present and otherwise distinguish interlined vehicle legs. Walking
access/egress, stop names and scheduled/effective times stay visible separately;
estimated street times never become live road-traffic claims. The shared backend
uses a dedicated TravelTracker query; the existing Android generation query is
unchanged.

## Planner controls and explicit acceptance (#127)

Open **Map tools → Transport**. The client automatically reads current capabilities
and saved transport entries before enabling supported modes. It preselects the
leg's palette transport even when the departure is not yet set. Retry/Refresh
routing options remains available after a failure or a server configuration change.
Choose origin, destination, departure date/time and Walk/Bike/Car/Bus/Train, then
Find routes. Alternatives show duration/distance, actual departure/arrival and
initial waiting; available transit details include walking legs, stops and
transfers. Times display in the trip zone. Supported Windows zone IDs are
translated to IANA for the request without changing the saved trip zone.
Opening a selected pin without an address also requests one reverse-address
suggestion. Choosing a searched address previews the new position for attractions,
accommodations and transport stops; Apply/Use location confirms the move.

Selecting a candidate draws a preview. Confirm applies the chosen saved mode and
exact provider departure/arrival together, after rechecking the current draft,
marker edits, endpoint order and exact catalogue identifier. Only adjacent stops
can receive a connection; other pairs may be previewed but require an explicit
itinerary reorder before confirmation. Cancel, outdated requests and provider
failure preserve the itinerary. Confirmation supports Undo and does not itself
save a catalogue record or trip.

Confirmed geometry remains within the current session and survives a matching
Save response. Changing positions/mode/times, opening a different trip, signing
out or starting a new draft invalidates it. Existing trip persistence stores the
mode and times, not provider geometry; reopened connection lines remain labelled
approximate until another preview is requested. See the [update/run commands](milestone-4/server-routing-update.md).

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

If you are signed in but address lookup reports a project/settings error, stop the
shared server, open **TravelTracker window → TravelTracker settings**, and copy
**both** public values from the frontend's launch configuration on the development
PC:

```powershell
$travelTrackerSettings = Get-Content -Raw .traveltracker-launch.json | ConvertFrom-Json
$travelTrackerSettings | Select-Object SupabaseUrl, PublishableKey
```

Use `SupabaseUrl` for the server's TravelTracker Supabase URL and `PublishableKey`
for its TravelTracker publishable key. Keep the existing RNDTransit project
settings. Restart the shared server after saving; restart the frontend after code
updates. Sign out and back in once if an older Routing401 already revoked the local
session. The public key is not a service-role key, database password or service
account password. Environment overrides of the frontend's Auth project must match
the server too.

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
