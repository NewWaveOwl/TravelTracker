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


## Implemented combined API repositories

The combined TravelTracker.WebApi does not reference TravelTracker.Data or the SQL schema above. Attraction, TransportMode and RoutePoint each have a repository interface and a singleton in-memory implementation owning a ConcurrentDictionary of immutable records. Services are scoped; no separate store adapter or database context is required. Synchronous lookup/insertion performs no external I/O.

Fixed seeds load once per host. Created records survive subsequent requests and disappear on restart. The Transport catalogue supports all six codes, including bike; the legacy SQL enum is unchanged. Route-point sourceId is optional metadata, not a verified foreign key. There are no updates, scheduled-route writes or stop-time calculations in the reduced demonstration. [Current evidence](milestone-2/implementation/validation.md).
