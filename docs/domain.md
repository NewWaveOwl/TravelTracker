# Domain Model

Business entities for TravelTracker (`TravelTracker.Domain`).

## Id64

`Id64` (`Types/Id64.cs`): 64 random bits, prints as 16 hex chars, matches `bigint` on Postgres/MSSQL. The project's preferred ID type going forward. Older models below still use `Guid`.

## Entities

| Entity | Fields | ID type |
|---|---|---|
| `User` | `Username`, `Email`, `Address?`, `Role`, `Status`, `CreatedAt`, `UpdatedAt?` | `Guid` |
| `Accomodation` | `Name`, `Address` | `Guid` |
| `Attraction` | `Name`, `Address`, `Latitude`, `Longitude`, `EstimatedVisitMinutes` | `Guid` |
| `TransportMode` | `Code`, `DisplayName` | `Guid` |

`UserRole`: `User`, `Administrator`.
`UserStatus`: `Active`, `Deactivated`, `Banned`.

## Not implemented yet

The following gaps refer to the original shared Domain scaffold, not the independent service-owned records below.

Referenced by repository/service interfaces, no model file yet: `Trip`, `TripLeg`, `UserSettings`, `TripMapPoint` (read-model), `Stay`, `Visit`, `TicketImport` (future).

## Business rules

- One-way trip: accommodation -> attraction. Round trip: adds a return leg, attraction -> accommodation.
- Legs sequence from 1 with no gaps; each leg starts where the last one ended; arrival after departure.
- Transport modes: car, bus, train, pedestrian, bike, plane. Mixed transport = multiple legs with different modes.
- Banned user: can sign in and read own data, can't write. Deactivated/deleted: can't sign in.

## Implemented standalone records

The independent APIs own these immutable records and use GUIDs as explicitly approved for this demonstration. They have no references to the unfinished shared Domain project.

| Record | Owning project | Meaning |
| --- | --- | --- |
| AttractionRecord | TravelTracker.Attractions.Api | Name, optional description/manual rating, address, coordinates, visit minutes |
| TransportModeRecord | TravelTracker.Transport.Api | A named transport entry: ID, code, name, description; several entries may share a code |
| RoutePointRecord | TravelTracker.Transport.Api | Typed accommodation/attraction/transport_stop location and optional source GUID |
| TransportRouteRecord | TravelTracker.Transport.Api | One scheduled route with transport and point snapshots, departure/arrival, preparation and ordered stops |
| TransportStopRecord | TravelTracker.Transport.Api | Point snapshot, arrival/departure, preparation and permission to leave the vehicle |

Bike and pedestrian use the same supplied-time validation. No speed, live route, inherited vehicle hierarchy or travel-time estimate is inferred. Actual UTC instants belong to scheduled routes and stop timings; reusable points and transport entries do not store a particular journey's dates. Trips' future aggregate remains separate. [Contract](milestone-2/implementation/specification.md).
