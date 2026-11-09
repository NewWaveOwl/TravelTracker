# Independent Trips service (#41)

The Trips service saves a complete itinerary and returns it only to its owner. It is hosted by `TravelTracker.Trips.Api`, stores its trips in its own PostgreSQL database (`TravelTracker.Trips.Data`), and runs on port **5108**. The trip domain rules live in `TravelTracker.Trips.Domain`, the repository port and PostgreSQL implementation in `TravelTracker.Trips.Data`, and the reference validator and save and read services in `TravelTracker.Api/Trips` (from #40).

## Project layout and dependency direction

| Project | References | Owns |
| --- | --- | --- |
| `TravelTracker.Trips.Domain` | none (plain SDK) | `Trip`, `TripLeg`, `TripPlace`, `TripTransport`, `TripTime`, `TripError`, `TripValidationException`, `TripMapPoint`; the itinerary rules |
| `TravelTracker.Trips.Data` | `TravelTracker.Trips.Domain` only | EF Core context, mappings, migration, `ITripRepository` and `EfTripRepository` |
| `TravelTracker.Api` (library) | `TravelTracker.Trips.Domain`, `TravelTracker.Trips.Data`, `TravelTracker.Data`, `TravelTracker.Domain` | reference clients, validator, save and read services |
| `TravelTracker.Trips.Api` (host) | `TravelTracker.Api`, `TravelTracker.Trips.Data`, `TravelTracker.Security` | HTTP controllers, composition, exception mapping |

Dependency direction stays Api to Data to Domain. `TravelTracker.Data` no longer contains any trip type.

It does not read the catalogue services' tables. Before saving, it asks the Accommodations, Attractions and Transport services over HTTP, forwarding the caller's token, and asks Users for live account standing with a backend credential.

Not implemented: trip history listing (`GET /trips`), editing, replacing legs, deleting, map points and Settings. Those remain future work.

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
| `GET /api/v1/trips/{tripId}` | `trips.read` | `200` for the caller's own trip. |

Requests and responses use camelCase JSON and public GUIDs only. The owner comes from the validated token (`iss` and `sub`); the body cannot name an owner, and unknown properties are refused with `400`.

### Create body

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

`type` is `accommodation`, `attraction` or `transport_stop`. `timeZone` is an IANA or Windows zone id. Each time carries its offset, which must be valid for the zone; the domain rejects an impossible or ambiguous local time.

### Status mapping

| Outcome | Status | Stored |
| --- | --- | --- |
| Saved | `201` | Trip, legs and snapshots, in one transaction |
| Malformed or unknown field, missing required field, unknown time zone | `400` | No |
| Itinerary breaks a trip rule (for example a one-way trip not ending at an attraction) | `400` with field errors | No |
| Referenced place or transport mode absent, or private to someone else | `404` with `errors` keyed by field | No |
| Banned, deactivated or unknown account (create) | `403` | No |
| Valid token but missing scope | `403` | No |
| No or invalid bearer token; catalogue rejected the forwarded token | `401` | No |
| Users or a catalogue service unavailable or unconfirmed (fail closed) | `503` | No |
| Storage write failed (rolled back) | `503` | No |
| Trip absent or owned by someone else (read), including administrators | `404` | Not applicable |
| Banned owner reads own trip | `200` | Not applicable |
| Deactivated or unknown account (read) | `403` | Not applicable |
| Malformed trip identifier | `400` | Not applicable |

The `404` for a reference uses one message for absent and private places, so the response does not reveal which it was.

## Checks before a save

1. The validated caller identity and the forwarded access token must be present.
2. The time zone and place types are checked before any outbound call.
3. Account standing is read live from Users with the backend credential, which holds `users.access`. Only an `active` account may save. The caller's own token is never sent to Users.
4. Every place is read from its owning service with the caller's token: accommodations, attractions and transport stops (route points), and transport modes. A private record owned by someone else appears absent.
5. The trip rules run on the snapshot values, and the trip is saved only when every check passes.

Each refusal and each fail-closed path is logged with its reason, without tokens, identities or upstream response bodies.

## Persistence

The `Trips` table holds the trip, its owner (issuer and subject), its time zone id, and its start and end instants with the offsets the traveller entered. The `TripLegs` table holds the ordered legs. Each leg row stores snapshots of its departure place, arrival place and transport mode, so later catalogue edits never change a stored trip. A save writes the trip row and every leg row in one explicit transaction; a failure at any row rolls the whole trip back.

Instants are written in UTC because Npgsql refuses a non-zero offset for `timestamptz`; the entered offsets are stored beside them and restored on read. Owner scoping is part of every read query, so another owner's trip is indistinguishable from an absent one.

## Tests

| Project | What it proves | Requirement |
| --- | --- | --- |
| `tests/TravelTracker.Trips.Domain.Tests` | Itinerary rules: ordering, connections, endpoints, time zone offsets | None |
| `tests/TravelTracker.Api.Tests` | Reference validation, save service and logs | None |
| `tests/TravelTracker.Trips.Api.Tests` | Real signed-JWT validation, scope matrix, status mapping, ownership, standing rules, fail-closed paths, public DTO shape, health | None (owner-aware in-memory store and scripted upstream) |
| `tests/TravelTracker.Trips.Data.Tests` | PostgreSQL migration, atomic rollback when a leg write fails, restart preservation with offsets, owner scoping, database check constraints | `TRAVELTRACKER_TEST_POSTGRES` with CREATEDB; tests are marked `Category=PostgreSQL` |

```powershell
dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"
$env:TRAVELTRACKER_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=postgres;Username=postgres'
dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category=PostgreSQL"
```

EF InMemory and the in-memory store do not prove migration, constraint, transaction or restart behavior; those are verified only by the PostgreSQL group.

## Frontend (#47)

The Blazor client (`TravelTracker.Frontend`) talks to the Trips, Accommodations, Attractions and Transport services over HTTP only. Configure their addresses under `Services` (Accommodations 5106, Trips 5108 by default).

| Route | What it does |
| --- | --- |
| `/new-trip` | Three steps: basics (name, time zone, start and end, one-way or round trip), itinerary (one card per leg), review and save. |
| `/trips/{tripId}` | Reopens a saved trip from the service. `?saved=true` only adds a "Trip saved." line once the trip itself has loaded. |

- Places are picked from their owning catalogue and sent by public id: accommodations (`AccommodationsClient`), attractions (`AttractionsClient`) and transport stops (`TransportClient`, route points of type `transport_stop`). The first leg starts at an accommodation; a new leg starts where the previous one ended, and the traveller can change that, which shows the disconnected-leg error instead of repairing it. With the optional `catalogue.write` grant, the accommodation picker also offers "Add a new accommodation": a private place saved through the Accommodations service, then selected by the id the service confirms. Shared places stay on the accommodation workspace (#46).
- Times are entered as local times in the trip time zone. The client attaches the zone's offset. A time that does not exist (clocks skip forward) or happens twice (clocks go back) is refused with a message; the client never picks an offset. The service stays authoritative and its field errors (`legs[1].arrival`) are shown on the matching field.
- The draft lives only in memory. A failed save (validation, permission, network or unavailable dependency) keeps every entry and shows what happened. The save button is disabled while a request is in flight and the handler ignores a second click, so one click sequence creates at most one request. The create has no idempotency key, so after a network failure the outcome is unknown; the message says so.
- Leaving with entries asks first (in-app dialog for links, the browser's own prompt for closing the tab). The draft is dropped when the signed-in identity changes.
- Not included, as agreed for the minimum slice: stays and visits, scheduled routes, the map, history listing, editing and deleting. `/history` says that listing is not available yet.

Verified by `tests/TravelTracker.Frontend.Tests` (`TripDraftTests`, `NewTripPageTests`, `TripsContractTests`), which drive the real pages through the real protected HTTP stack against in-process services. They are not a run against live hosts or a live Auth0 tenant.

## Limits

- Trip history, edit, replace-legs, delete, map points and Settings are not implemented.
- Trip time zone ids are resolved on the host that reads them; a stored id that the host's time zone database no longer knows fails the read with a server error.
- Place and transport snapshots are copied from the owning services at save time; a catalogue outage after saving does not affect a stored trip.
- The Trips host does not cache account standing. Each save and each detail read asks Users.
