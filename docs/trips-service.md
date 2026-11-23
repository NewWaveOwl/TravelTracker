# Independent Trips service (#41)

The Trips service creates and replaces complete itineraries and returns them only to their owners. It is hosted by `TravelTracker.Trips.Api`, stores its trips in its own PostgreSQL database (`TravelTracker.Trips.Data`), and runs on port **5108**. The trip domain rules live in `TravelTracker.Trips.Domain`, the repository port and PostgreSQL implementation in `TravelTracker.Trips.Data`, and the reference validator and save and read services in `TravelTracker.Api/Trips` (from #40).

## Project layout and dependency direction

| Project | References | Owns |
| --- | --- | --- |
| `TravelTracker.Trips.Domain` | none (plain SDK) | `Trip`, `TripLeg`, `TripPlace`, `TripTransport`, `TripTime`, `TripError`, `TripValidationException`, `TripMapPoint`; the itinerary rules |
| `TravelTracker.Trips.Data` | `TravelTracker.Trips.Domain` only | EF Core context, mappings, migration, `ITripRepository` and `EfTripRepository` |
| `TravelTracker.Api` (library) | `TravelTracker.Trips.Domain`, `TravelTracker.Trips.Data`, `TravelTracker.Data`, `TravelTracker.Domain` | reference clients, validator, save and read services |
| `TravelTracker.Trips.Api` (host) | `TravelTracker.Api`, `TravelTracker.Trips.Data`, `TravelTracker.Security` | HTTP controllers, composition, exception mapping |

Dependency direction stays Api to Data to Domain. `TravelTracker.Data` no longer contains any trip type.

It does not read the catalogue services' tables. Before saving, it asks the Accommodations, Attractions and Transport services over HTTP, forwarding the caller's token, and asks Users for live account standing with a backend credential.

History lists the caller's saved trips from PostgreSQL, including trips saved before the list endpoint was added. Deleting trips, individual leg writes and separate map-point resources remain future work. The complete trip representation includes the ordered place and transport snapshots needed by Map View.

## Start independently

Run PostgreSQL with a dedicated `traveltracker_trips` database. Supply secrets through environment variables or user secrets, never through committed settings.

```powershell
$env:ConnectionStrings__Trips = 'Host=localhost;Database=traveltracker_trips'
$env:Database__ApplyMigrations = 'true'
$env:Authentication__Bearer__Authority = 'https://your-provider.example/tenant'
$env:Authentication__Bearer__Issuer = 'https://your-provider.example/tenant'
$env:Authentication__Bearer__Audience = 'traveltracker-api'
$env:TripReferences__UsersServiceAccessToken = '<backend access token with users.access>'
dotnet run --project src/TravelTracker.Trips.Api --launch-profile http
```

The host validates its configuration when it starts, so it refuses to start without `ConnectionStrings:Trips`, a valid bearer authority, and the `TripReferences` section with an HTTPS Users address and a backend credential. It does not contact the catalogue services, Users or PostgreSQL to start, and the health endpoint calls none of them. `Database:ApplyMigrations` applies the committed migration at startup; leave it false when a migration tool manages the schema. The startup log records the configured migration flag, the upstream base addresses and the reference timeout, never the credential.

`dotnet ef` must run against the data project, because the design-time factory lives there:

```powershell
dotnet ef database update --project src/TravelTracker.Trips.Data --startup-project src/TravelTracker.Trips.Data
```

Swagger is available at `/swagger` in the Development environment.

## Routes

| Method and path | Scope | Result |
| --- | --- | --- |
| `GET /health` | Anonymous | `200` with `{"status":"ok","service":"Trips"}`. Liveness only. |
| `POST /api/v1/trips` | `trips.write` and `catalogue.read` | `201` with the saved trip and `Location: /api/v1/trips/{tripId}`. |
| `GET /api/v1/trips` | `trips.read` | `200` with an array of the caller's trip summaries (`tripId`, `name`, `start`, `end`), ordered by latest start first. |
| `GET /api/v1/trips/{tripId}` | `trips.read` | `200` for the caller's own trip. |
| `PUT /api/v1/trips/{tripId}` | `trips.write` and `catalogue.read` | `200` with the updated trip. The identifier and owner are preserved. |
| `POST /api/v1/trips/{tripId}/ticket-imports` | `trips.write` | Proposes editable PDF ticket fields for the active owner; no trip write. |
| `GET /api/v1/trips/{tripId}/ticket-imports/{importId}/document` | `trips.read` | Returns the owner's private, unexpired original PDF. |
| `DELETE /api/v1/trips/{tripId}/ticket-imports/{importId}` | `trips.write` | Removes the active owner's temporary original. |

Requests and responses use camelCase JSON and public GUIDs only. The owner comes from the validated token (`iss` and `sub`); the body cannot name an owner, and unknown properties are refused with `400`.

Ticket imports use a raw `application/pdf` body with an optional display-only `X-Ticket-File-Name` header. They are bounded to 10 MiB and 10 pages, parsed on the Trips server, and retained only in private process memory for 15 minutes. The saved-trip **Tickets & bookings** entry opens `/trips/{tripId}/ticket-import` for editable review. This draft-only flow does not persist attachments or replace an itinerary. See [PDF ticket drafts](ticket-import.md) for its exact contract, supported input and security boundaries.

### Minimal draft and save fields (#105)

This contract implements #105 without changing existing leg payloads. Browser drafts may be incomplete; a service save requires the following:

| Field | Save rule |
| --- | --- |
| `name` | Required, nonblank, at most 200 characters. |
| `timeZone` | Required, recognized IANA or Windows zone ID. |
| `start`, `end` | Required offset-bearing times; `end >= start` by instant. Their entered offsets are retained, including UTC boundaries on earlier saved trips. Equal boundaries are valid for an itinerary without journeys. |
| `isRoundTrip` | Optional, defaults to `false`. Stored as traveller intent; it imposes no return, category or visit requirement. |
| `legs` | Required array, possibly empty, at most 100 entries. Supplied legs need consecutive sequences, connected endpoints, a visible transport mode, positive duration, no overlap and times within trip bounds. |
| `stops` | Optional ordered array of `{ "placeId": "<public GUID>", "type": "<supported type>" }`. Omitted or null derives the list from legs. With no legs, zero or one stop is valid. With legs, an explicit list must equal the first departure followed by each destination, by place ID and type. |

All supported types (`accommodation`, `attraction`, `transport_stop`) may be an endpoint or the only stop. No category, visit or closed loop is needed. Two or more stops require connecting legs. The service does not fabricate a self-leg, mode, visit, dates or offsets to fill gaps. The response adds `stops` containing authoritative snapshots in occurrence order; existing `legs` fields are preserved. A legacy request that supplies only legs continues to work. A legacy stored trip derives stops from its leg snapshots until it is next saved.

The smallest empty saved trip is:

```json
{
  "name": "Weekend ideas",
  "timeZone": "Europe/Paris",
  "start": "2026-06-01T08:00:00+02:00",
  "end": "2026-06-01T08:00:00+02:00",
  "legs": []
}
```

For a single-stop plan add `"stops": [{ "placeId": "<catalogue id>", "type": "transport_stop" }]` to that body. The same shape supports an accommodation or attraction. The ID is checked against its owning service with the caller's token; caller-supplied coordinates, snapshots and owner fields are not accepted. Latitude must be -90..90 and longitude -180..180 in every resolved snapshot. Invalid or incomplete upstream data fails closed with `503`; invalid submitted references/shapes/times return field errors without a write.

Both `trips.write` and `catalogue.read` remain required even for an empty itinerary. Live active Users standing is always checked. Replacement still requires an owned trip and its current `If-Match` revision before catalogue resolution. Another subject, issuer or administrator cannot replace a private trip. Parent, stops, legs and revision are committed atomically; a refusal or failed write leaves the stored aggregate unchanged.

The requester confirmed retaining the required basics and adding optional stop references in chat on 2026-11-22. This additive contract is prepared for review on `allow-flexible-trip-saves`; it does not record teammate approval. A different teammate still reviews the PR before integration, as requested by #105.

### Create and replace body

Send all trip fields and every leg when replacing a trip. The route identifier selects the trip to edit.
Successful reads, creates, and replacements include a strong `ETag` header. Send that tag as
`If-Match` on replacement. When the tag is stale, reopen the trip and review the changes before saving.

```json
{
  "name": "Paris weekend",
  "timeZone": "Europe/Paris",
  "start": "2026-06-01T08:00:00+02:00",
  "end": "2026-06-01T12:00:00+02:00",
  "isRoundTrip": false,
  "legs": [
    {
      "sequence": 1,
      "from": { "placeId": "<accommodation id>", "type": "accommodation" },
      "to": { "placeId": "<attraction id>", "type": "attraction" },
      "transportModeId": "<transport mode id>",
      "departure": "2026-06-01T09:00:00+02:00",
      "arrival": "2026-06-01T10:00:00+02:00"
    }
  ]
}
```

`type` is `accommodation`, `attraction` or `transport_stop`. `timeZone` is an IANA or Windows zone id. Each journey time carries an offset valid for that zone. Impossible local journey times are rejected; an ambiguous local time needs an explicitly valid offset. Trip boundaries retain their submitted offsets and are compared as instants; they are not silently normalized to the display zone.

### Status mapping

| Outcome | Status | Stored |
| --- | --- | --- |
| Created | `201` | Trip, legs and snapshots, in one transaction |
| Replaced | `200` | Updated trip, legs and snapshots, in one transaction |
| Malformed or unknown field, missing required field, unknown time zone | `400` | No |
| Itinerary breaks a retained rule (for example overlapping legs, invalid zone offsets or inconsistent stops) | `400` with field errors | No |
| Referenced place or transport mode absent, or private to someone else | `404` with `errors` keyed by field | No |
| Banned, deactivated or unknown account (create) | `403` | No |
| Valid token but missing scope | `403` | No |
| No or invalid bearer token; catalogue rejected the forwarded token | `401` | No |
| Users or a catalogue service unavailable or unconfirmed (fail closed) | `503` | No |
| Storage write failed (rolled back) | `503` | No |
| Trip absent or owned by someone else (read or replace), including administrators | `404` | No change |
| Existing owned trip is missing `If-Match` (replace) | `428` | No change |
| `If-Match` does not match the current revision (replace) | `412` | No change |
| Banned owner reads own trip | `200` | Not applicable |
| Deactivated or unknown account (read) | `403` | Not applicable |
| Malformed trip identifier | `400` | Not applicable |

The `404` for a reference uses one message for absent and private places, so the response does not reveal which it was.

## Checks before a save

1. The validated caller identity and the forwarded access token must be present.
2. The request is bound and its time zone, stop references and leg entries are checked. For replacements, action-level field errors are returned after the standing check.
3. Account standing is read live from Users with the backend credential, which holds `users.access`. Only an `active` account may save. The caller's own token is never sent to Users.
4. A replacement checks ownership and the supplied revision before looking up catalogue records. The database write checks both again. Place types are checked before calling the catalogue.
5. Every referenced place, including a lone stop, is read from its owning service with the caller's token: accommodations, attractions and transport stops (route points), and transport modes used by legs. A private record owned by someone else appears absent. Empty itineraries perform no catalogue lookup.
6. The trip rules run on the snapshot values, and the trip is saved only when every check passes.

Each refusal and each fail-closed path is logged with its reason, without tokens, identities or upstream response bodies.

## Persistence

The `Trips` table holds the trip, its owner (issuer and subject), its time zone id, and its start and end instants with the offsets the traveller entered. The `TripLegs` table holds the ordered legs. Each leg row stores snapshots of its departure place, arrival place and transport mode, so later catalogue edits never change a stored trip. The additive `TripStops` table retains ordered place snapshots, including a lone stop with no leg. Existing trips without stop rows derive their stops from their saved legs. A save writes the trip row and every stop and leg row in one explicit transaction. A replacement updates the owned trip row only at the expected revision and advances that revision before replacing its stops and legs. A failure leaves the previous revision and all snapshots intact. Apply the new migration before running the updated API; earlier applied migrations are preserved.

Instants are written in UTC because Npgsql refuses a non-zero offset for `timestamptz`; the entered offsets are stored beside them and restored on read. Owner scoping is part of every read query, so another owner's trip is indistinguishable from an absent one.

## Tests

| Project | What it proves | Requirement |
| --- | --- | --- |
| `tests/TravelTracker.Trips.Domain.Tests` | Empty/single-stop plans, all supported endpoint categories, ordering, connections, coordinates and time zone offsets | None |
| `tests/TravelTracker.Api.Tests` | Reference validation, save service and logs | None |
| `tests/TravelTracker.Trips.Api.Tests` | Real signed-JWT validation, scope matrix, status mapping, ownership, standing rules, fail-closed paths, public DTO shape, health | None (owner-aware in-memory store and scripted upstream) |
| `tests/TravelTracker.Trips.Data.Tests` | PostgreSQL migration, replacement, atomic rollback when a leg write fails, restart preservation with offsets, owner scoping, database check constraints | `TRAVELTRACKER_TEST_POSTGRES` with CREATEDB; tests are marked `Category=PostgreSQL` |

```powershell
dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"
$env:TRAVELTRACKER_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=postgres;Username=postgres'
dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category=PostgreSQL"
```

EF InMemory and the in-memory store do not prove migration, constraint, transaction or restart behavior; those are verified only by the PostgreSQL group.

### Verification for #105 (2026-11-22)

The requester confirmed keeping the existing required trip basics before integration. Changes are on `allow-flexible-trip-saves`, based on the previously validated `41d4f50` working tree. A different teammate still needs to review the PR. No teammate approval or remote CI result is claimed here.

| Test group | Baseline | Final discovered / executed / passed | Failed / skipped |
| --- | --- | --- | --- |
| All application/domain/HTTP tests (`Category!=PostgreSQL`) | 1,179 passed | 1,298 / 1,298 / 1,298 | 0 / 0 |
| Trips Domain | 7 passed | 56 / 56 / 56 | 0 / 0 |
| Trips HTTP | 120 passed | 182 / 182 / 182 | 0 / 0 |
| Frontend | 410 passed | 418 / 418 / 418 | 0 / 0 |
| All PostgreSQL tests (`Category=PostgreSQL`) | Not run before edits; local instance was not configured | 81 / 81 / 81 | 0 / 0 |
| Trips PostgreSQL (included above) | 7 existing cases | 23 / 23 / 23 | 0 / 0 |

The application suite added 119 cases; Trips persistence added 16 PostgreSQL cases. Evidence lives in [pure domain tests](../tests/TravelTracker.Trips.Domain.Tests/TripTests.cs), [HTTP regression cases](../tests/TravelTracker.Trips.Api.Tests/FlexibleTripSaveTests.cs), [PostgreSQL migration/atomicity tests](../tests/TravelTracker.Trips.Data.Tests/TripRepositoryPostgreSqlTests.cs), [planner cases](../tests/TravelTracker.Frontend.Tests/TripPlannerTests.cs) and [form page cases](../tests/TravelTracker.Frontend.Tests/NewTripPageTests.cs). Tests cover all nine supported endpoint combinations, empty and single-stop plans, both round-trip flags, private references and cross-user requests, invalid data, unchanged rejected replacements, migration of legacy UTC boundaries, and a concurrent replacement between split read queries.

Validation commands:

```powershell
dotnet restore TravelTracker.slnx
dotnet build TravelTracker.slnx --configuration Release --no-restore --verbosity quiet
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category!=PostgreSQL" --logger "trx;LogFilePrefix=issue105-final"
# With TRAVELTRACKER_TEST_POSTGRES pointing only at an isolated local PostgreSQL instance:
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category=PostgreSQL" --logger "trx;LogFilePrefix=issue105-postgresql-final"
git diff --check
```

The solution build completed with **0 warnings and 0 errors**. PostgreSQL verification used a temporary PostgreSQL 17.12 process bound to loopback, with each test creating and dropping its own database; shared development data was not used. The additive migration SQL was reviewed and EF reported no pending model changes. Local TRX files are ignored under each test project's `TestResults/`, using `issue105-baseline`, `issue105-final` and `issue105-postgresql-final` prefixes.

A disposable Kestrel smoke host exercised the production Trips API, signed JWT validation and actual PostgreSQL repository with scripted Users/catalogue responses. It verified empty/single-stop creates (`201`), an attraction-to-accommodation journey with round-trip intent (`200` replacement), own reads (`200`), cross-owner reads/replacements (`404`), reversed dates (`400`) leaving the body and ETag unchanged, single-stop-to-empty replacement and reloading from a new database context. Frontend cases exercise the real protected HTTP stack in component tests; a live Auth0/browser session and deployment were not tested.

Read-only agent review found and resolved a legacy UTC-boundary read regression and a multiplied-row query before final checks; no unresolved findings remain. One earlier targeted API run hit the unchanged ticket parser concurrency test's five-second wait; the complete targeted rerun and final full application suite passed. The required human teammate review remains separate from agent review.

## Frontend (#47)

The Blazor client (`TravelTracker.Frontend`) talks to the Trips, Accommodations, Attractions and Transport services over HTTP only. Configure their addresses under `Services` (Accommodations 5106, Trips 5108 by default).

| Route | What it does |
| --- | --- |
| `/trip`, `/new-trip` and `/map` | Share a planner draft, including map markers and connections. Trip offers Create new and Open saved; append a trip ID to open a saved itinerary for editing. Existing links remain valid. |
| `/new-trip/form` | Form-only trip creation with basics, itinerary cards, optional review and a visible save action. Open it from the standard planner. |
| `/trips/{tripId}` | Reopens a saved trip from the service. `?saved=true` only adds a "Trip saved." line once the trip itself has loaded. |

### Visible save action and editor shortcut (#119)

**Save trip** stays available in the workspace action bar on Map, Trip and the
step-by-step form, including narrow screens and collapsed Map tools. It uses
the same Trips create/replace operation as review. Ctrl+S and Cmd+S activate
that same button within the editor, including from a field being edited;
the current field value is committed before saving. The shortcut registration
is removed when the editor closes. Outside the editor, the browser keeps its
normal save-page shortcut.

The action shows saving, confirmed-save and error feedback. It blocks another
submission while the first request is pending, and a failed request keeps the
draft available for correction or retry. Empty and single-stop plans use the
[minimal save contract](#minimal-draft-and-save-fields-105). Unconfirmed marker
details and new catalogue entries still need the existing explicit review and
confirmation steps; the shortcut does not invent a journey or automatically
write catalogue records.

In Map View, **Save map draft** stores a browser copy and keeps the undo history
and unsaved-changes warning. Restoring a browser draft brings its edits back into
the planner. Signed-out drafts stay in the current tab's session. Signed-in
drafts stay in this browser under the account that saved them. When a signed-in
draft receives a saved trip link, its browser copy is also available from that
link. Dragging a pin in Map View review returns to editing
and records an unsaved change.

Map View keeps essential trip settings beside the itinerary. **Map tools**
opens or closes the stop/connection editor without dropping the current edit
buffer or selection. Marker and transport controls reopen this panel, and its
slide transition respects reduced-motion preferences. Select a marker to edit
its name, address and coordinates; confirm applies the draft and Cancel leaves
the original place intact. Changed saved places are resolved through **Save
catalogue entries** before saving the trip, so reopening uses the confirmed
location snapshot. Transport stops are selected from the shared catalogue.
Hand-placed transport pins can be kept in a browser draft. Before saving the
trip, select each draft pin and choose a saved stop from the search results.
Replacing a pin preserves its connections. Trip review checks draft transport
modes against the shared catalogue before saving new places. If a mode is
missing, the editor offers saved modes for the affected connections. An
administrator can add shared modes and route points in the Transport
workspace. Switching editor views asks before discarding unconfirmed
marker fields. Selecting or editing a marker preserves the map camera; opening
a different saved trip fits its stops. **Fit stops** remains an explicit
navigation action within a trip. Time inputs show minutes,
and only changed times are normalized to minute precision.

Creating a new trip confirms before discarding unsaved work. Opening another
saved trip replaces the draft only after its read succeeds. While a saved-trip
URL is loading, the previous trip's private fields and editing controls are
hidden; a failed load restores the previous draft's own URL and displays an
error. History and pasted saved links use these same loading rules.

History (`/history` and Trip → **Open saved**) reads the owner's existing summary
list over HTTP. Its responsive cards show names, date ranges, planned status
and explicit **Open trip** links. Search, inclusive overlapping date ranges,
status filters, ordering and ten-item pages are applied locally to that list;
the service contract is unchanged. Default ordering puts completed trips first,
newest first, followed by current/upcoming trips in start order. Loading, access
denial, empty history, no filter matches and retryable read failures have
separate messages. Account changes clear both summaries and filter state.

In the form-only planner:

- Places are picked from their owning catalogue and sent by public id: accommodations (`AccommodationsClient`), attractions (`AttractionsClient`) and transport stops (`TransportClient`, route points of type `transport_stop`). Any supported type may start or end a leg. A new leg starts where the previous one ended, and the traveller can change that, which shows the disconnected-leg error instead of repairing it. Empty itineraries are allowed; Map View also retains a single selected stop on save and reopen. With the optional `catalogue.write` grant, the accommodation picker also offers "Add a new accommodation": a private place saved through the Accommodations service, then selected by the id the service confirms. Shared places stay on the accommodation workspace (#46).
- Times are entered as local times in the trip time zone. The client attaches the zone's offset. A time that does not exist (clocks skip forward) or happens twice (clocks go back) is refused with a message; the client never picks an offset. The service stays authoritative and its field errors (`legs[1].arrival`) are shown on the matching field.
- The draft lives only in memory. A failed save (validation, permission, network or unavailable dependency) keeps every entry and shows what happened. The save button is disabled while a request is in flight and the handler ignores a second click, so one click sequence creates at most one request. The create has no idempotency key, so after a network failure the outcome is unknown; the message says so.
- Leaving with entries asks first (in-app dialog for links, the browser's own prompt for closing the tab). The draft is dropped when the signed-in identity changes.
- Open **History** to recover a saved trip link. Stays and visits, scheduled routes, and deletion remain outside these planner flows.

Verified by `tests/TravelTracker.Frontend.Tests` (`TripDraftTests`, `NewTripPageTests`, `TripsContractTests`), which drive the real pages through the real protected HTTP stack against in-process services. They are not a run against live hosts or a live Auth0 tenant.

### Verification for #119 (2026-11-22)

The change is on `ticket-import-and-flexible-trip-saves`, alongside #105 and
#109. The save UI and shortcut leave the public Trips API unchanged.

| Check | Discovered / executed / passed | Failed / skipped |
| --- | --- | --- |
| Application baseline at `e70ba63` | 1,298 / 1,298 / 1,298 | 0 / 0 |
| Final application suite | 1,310 / 1,310 / 1,310 | 0 / 0 |
| Final PostgreSQL group, isolated local PostgreSQL 17 | 81 / 81 / 81 | 0 / 0 |
| Standalone JavaScript shortcut tests | 14 / 14 / 14 | 0 / 0 |

Restore and the full Release build passed with zero warnings and zero errors.
The frontend grew from 418 to 430 passing cases; no existing cases were removed.
Commands run from the repository root:

```powershell
dotnet restore TravelTracker.slnx
dotnet build TravelTracker.slnx --configuration Release --no-restore
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category!=PostgreSQL" --logger "trx;LogFilePrefix=issue119-final"
# TRAVELTRACKER_TEST_POSTGRES configured for an isolated instance with CREATEDB:
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category=PostgreSQL" --logger "trx;LogFilePrefix=issue119-postgresql"
node --test tests/TravelTracker.Frontend.Tests/Browser/tripSaveShortcut.test.mjs
```

[Planner tests](../tests/TravelTracker.Frontend.Tests/TripPlannerTests.cs) cover
direct saves of empty and single-stop plans, visible controls outside collapsed
Map tools, blocked pending marker/catalogue changes, pending and failed saves,
and shortcut registration/cleanup. [Form tests](../tests/TravelTracker.Frontend.Tests/NewTripPageTests.cs)
cover saving from each editable step, validation feedback, failure retention,
retry and locked fields while a request is pending.
[Shortcut tests](../tests/TravelTracker.Frontend.Tests/Browser/tripSaveShortcut.test.mjs)
execute the production JavaScript against an isolated DOM event fixture: Ctrl+S
and Cmd+S, current-field change before button activation, native-event
cancellation, held/repeated keys, disabled controls, dialogs, other targets,
navigation cleanup and preservation of Undo.

Native Chrome verification used the running Blazor frontend on local Kestrel
with a test-only authenticated session and controlled HTTP service responses.
Both the visible button and Ctrl+S saved empty and single-stop plans; the
single-stop request retained one attraction, zero legs and round-trip intent.
Typing a name and pressing Ctrl+S without leaving the field saved the latest
text. A held response showed **Saving trip**; repeated Ctrl+S presses produced
one request. A forced 503 kept the name and dates, and retry succeeded. Save
remained visible in desktop and 390–392 px mobile emulation, including after
scrolling. After navigating to History, Ctrl+S opened the native **Save As**
dialog, which was cancelled without writing a file.

Read-only agent review finished with no remaining findings. A human teammate
must still review the PR; no PR review, remote CI result, live Auth0/backend
run, physical mobile-device run or native macOS run is claimed. Cmd+S is covered
by the JavaScript event tests. TRX files and the disposable browser harness stay
in ignored local output; the verification summary and regression tests are
committed for review.

### PR #138 integration verification (2026-11-22)

[PR #138](https://github.com/NewWaveOwl/TravelTracker/pull/138) integrates the
three feature commits with `milestone_4` at `6302ab9`. A merge commit preserves
the published feature history. Conflict resolution retains Supabase Auth,
profile-picture security, canonical floating panels, the date-range picker,
shared reference validation, name/leg limits and the database revision check.
It also retains private PDF drafts, optional stop snapshots and one editor
save-shortcut registration.

| Check | Target baseline | Integrated discovered / executed / passed | Failed / skipped |
| --- | --- | --- | --- |
| Application/domain/HTTP | 1,430 passed | 1,679 / 1,679 / 1,679 | 0 / 0 |
| PostgreSQL relational | 68 passed | 85 / 85 / 85 | 0 / 0 |
| Browser JavaScript interaction | 41 passed | 55 / 55 / 55 | 0 / 0 |

Both Release solution builds passed with zero warnings and errors. The
integrated frontend has 625 passing cases, Trips HTTP 196, shared API 78 and
Trips Domain 59. Target tests were retained; obsolete category and UI-selector
expectations were updated to the approved contract and current controls.
Additional integration cases cover restricted accounts before stop-shape
errors, exact name/leg limits and upgrades from both existing migration states.

```powershell
dotnet restore TravelTracker.slnx
dotnet restore TravelTracker.Tests.slnx
dotnet build TravelTracker.slnx --configuration Release --no-restore
dotnet build TravelTracker.Tests.slnx --configuration Release --no-restore
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category!=PostgreSQL" --logger "trx;LogFilePrefix=pr138-integrated-final"
# TRAVELTRACKER_TEST_POSTGRES configured for isolated local PostgreSQL:
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category=PostgreSQL" --logger "trx;LogFilePrefix=pr138-integrated-postgresql"
node --test tests/TravelTracker.Frontend.Tests/TripSaveKeyboardTests.mjs tests/TravelTracker.Frontend.Tests/TripMapCanvasKeyboardTests.mjs tests/TravelTracker.Frontend.Tests/MovablePanelTests.cjs tests/TravelTracker.Frontend.Tests/TripDateRangePickerKeyboardTests.mjs tests/TravelTracker.Frontend.Tests/Browser/tripSaveShortcut.test.mjs
dotnet run --file scripts/SmokeHosts.cs
```

All six service hosts independently returned health `200`. The Supabase Auth
hook SQL checks passed in a fresh disposable database (nine permission checks
plus execution by the hook role), then rolled back and the database was dropped.
The marked PostgreSQL tests and loopback HTTP checks used an isolated local
PostgreSQL instance; no shared Supabase data or hosted configuration changed.

The integrated Kestrel API check used signed test JWTs and real PostgreSQL trip
persistence with scripted account/catalogue responses. Empty/single-stop
creates, flexible replacement, owner isolation, invalid-date rejection,
unchanged rejected writes and context restart all passed. PDF extraction
returned `201` with 12 fields, the owner downloaded the original (`200`), a
different owner received `404`, malformed PDF returned `422`, and cleanup
returned `204`. Import success and failure left the trip body and ETag intact.

Published migrations remain unchanged. The combined model has no pending EF
changes; reviewed upgrade SQL adds the revision constraint and stop table
without destructive operations. Read-only integration review has no remaining
findings. Human teammate review remains required. Remote CI status is tracked
on the PR; local results do not establish live Supabase, physical-device or
native macOS behavior. The native Chrome checks above predate integration;
the integrated frontend was verified by component/HTTP and JavaScript tests.

## Limits

- Trip time zone ids are resolved on the host that reads them; a stored id that the host's time zone database no longer knows fails the read with a server error.
- Place and transport snapshots are copied from the owning services at save time; a catalogue outage after saving does not affect a stored trip.
- The Trips host does not cache account standing. Each save and each detail read asks Users.
