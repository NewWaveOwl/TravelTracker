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

Referenced by repository/service interfaces, no model file yet: `Trip`, `TripLeg`, `UserSettings`, `TripMapPoint` (read-model), `Stay`, `Visit`, `TicketImport` (future).

## Business rules

- One-way trip: accommodation -> attraction. Round trip: adds a return leg, attraction -> accommodation.
- Legs sequence from 1 with no gaps; each leg starts where the last one ended; arrival after departure.
- Transport modes: car, bus, train, pedestrian, plane. Mixed transport = multiple legs with different modes.
- Banned user: can sign in and read own data, can't write. Deactivated/deleted: can't sign in.
