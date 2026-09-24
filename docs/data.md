# Data Layer

Persistence for TravelTracker: `src/Database/schema.sql` plus repository interfaces in `TravelTracker.Data`. No `DbContext` or implementation yet, schema and interfaces only.

## Schema

Enums: `TransportMode` (`car`, `bus`, `train`, `pedestrian`, `plane`), `TripType` (`one_way`, `round_trip`).

| Table | Columns |
|---|---|
| `Users` | `Id` bigint identity PK, `Uuid` unique, `UserName` unique, `Password`, `Address` |
| `Accommodations` | `Id` bigint identity PK, `Uuid` unique, `Name`, `Address` |
| `Attractions` | `Id` bigint identity PK, `Uuid` unique, `Name`, `Address` |
| `Trips` | `Id` bigint identity PK, `Uuid` unique, `UserId` FK -> `Users`, `Transport`, `Type`, `DeparturePlaceId` FK -> `Accommodations`, `DestinationPlaceId` FK -> `Attractions` |

No tables yet for `UserSettings`, `TripLegs`, `Stays`, `Visits`, `TicketImports`.

Note: `Trips` stores one leg directly on the row (one transport, one departure, one destination). `ITripRepository.ReplaceLegsAsync` already assumes multiple legs per trip, so the schema needs a `TripLegs` table before that method can be implemented.

## Repository interfaces

| Interface | Accessibility | ID type |
|---|---|---|
| `IUserRepository` | internal | `Guid` |
| `IAccomadationRepository` | internal | `Guid` |
| `IAttractionRepository` | public | `Guid` |
| `ITransportModeRepository` | public | `Guid` |
| `ISettingsRepository` | public | `Id64` |
| `ITripRepository` | public | `Id64` |

`IUserRepository` and `IAccomadationRepository` are `internal`, no public path out of `TravelTracker.Data` for them yet.

## Ownership

Users/Accommodations: Christel. Transport/Attractions: Artiom. Trips/Settings: James.
