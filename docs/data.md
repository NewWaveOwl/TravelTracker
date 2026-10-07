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

## Implemented standalone stores

TravelTracker.Attractions.Api and TravelTracker.Transport.Api own independent thread-safe singleton stores, with scoped repository/service adapters. These do not use the SQL schema above or reference TravelTracker.Data. The legacy SQL enum still omits bike; the implemented Transport catalogue supports all six approved mode codes.

Records and saved route snapshots are immutable. Reads take collection snapshots. Attractions' compound PATCH applies and validates a candidate under one lock, so invalid updates leave the original intact and concurrent changes to different fields retain both values. Transport validates a complete route and resolves all references before a single insertion. The current Transport API has no catalogue update/delete operations that could invalidate those references mid-write.

Seeds are fixed and loaded once per host/process. Created/updated state survives requests and is discarded on restart. Source IDs on route points are metadata, not foreign keys into another owner's database; future Trips integration validates relevant external catalogue references over HTTP. [Verification evidence](milestone-2/implementation/validation.md).
