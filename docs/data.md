# Data Layer

The original [SQL schema](../src/Database/schema.sql) and all six existing [repository interfaces](../src/TravelTracker.Data/Repositories) from master are unchanged. TravelTracker.Data now implements the original Attraction/TransportMode interfaces and the new RoutePoint interface using process-lifetime memory. No DbContext or SQL persistence implementation exists yet.

## Schema

API bearer authentication adds no tables, token storage or signing keys to Data. Future subject-to-account mapping must use the validated `(iss, sub)` pair; it is not currently implemented. See [API authentication](authentication.md).

Enums: `TransportMode` (`car`, `bus`, `train`, `pedestrian`, `plane`), `TripType` (`one_way`, `round_trip`).

| Table | Columns |
|---|---|
| `Users` | `Id` bigint PK (app-issued), `Uuid` unique, `UserName` unique, `Password`, `Address` |
| `Accommodations` | `Id` bigint PK (app-issued), `Uuid` unique, `Name`, `Address` |
| `Attractions` | `Id` bigint PK (app-issued), `Uuid` unique, `Name`, `Address` |
| `Trips` | `Id` bigint PK (app-issued), `Uuid` unique, `UserId` FK -> `Users`, `Transport`, `Type`, `DeparturePlaceId` FK -> `Accommodations`, `DestinationPlaceId` FK -> `Attractions` |

No tables yet for `UserSettings`, `TripLegs`, `Stays`, `Visits`, `TicketImports`, route points, scheduled transport routes or intermediate stops.

Note: `Trips` stores one leg directly on the row (one transport, one departure, one destination). `ITripRepository.ReplaceLegsAsync` already assumes multiple legs per trip, so the schema needs a `TripLegs` table before that method can be implemented.

## Repository interfaces

| Preserved interface | Accessibility / ID type | Declared methods |
|---|---|---|
| [IUserRepository](../src/TravelTracker.Data/Repositories/IUserRepository.cs) | internal / `Guid` | `GetByIdAsync`, `GetByEmailAsync`, `GetAllAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `ChangeStatusAsync` |
| [IAccomadationRepository](../src/TravelTracker.Data/Repositories/IAccomadationRepository.cs) | internal / `Guid` | `GetByIdAsync`, `GetAllAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` |
| [IAttractionRepository](../src/TravelTracker.Data/Repositories/IAttractionRepository.cs) | public / `Guid` | `GetAllAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync` |
| [ITransportModeRepository](../src/TravelTracker.Data/Repositories/ITransportModeRepository.cs) | public / `Guid` | `GetAllAsync`, `GetByIdAsync`, `GetByCodeAsync`, `AddAsync`, `UpdateAsync` |
| [ISettingsRepository](../src/TravelTracker.Data/Repositories/ISettingsRepository.cs) | public / `Guid` (public id) | `GetByUserIdAsync`, `UpsertAsync` |
| [ITripRepository](../src/TravelTracker.Data/Repositories/ITripRepository.cs) | public / `Guid` (public id) | `GetAllByUserIdAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `ReplaceLegsAsync` |

`IUserRepository` and `IAccomadationRepository` are `internal`, no public path out of `TravelTracker.Data` for them yet.

The existing `IAccomadationRepository`/`Accomodation` spellings and all signatures are kept. User/accommodation create/update methods return the saved entity; attraction/transport add/update, settings upsert and trip replace-legs methods return task completion. Trips/Settings referenced types now have empty partial declarations for compilation; their data contracts and persistence remain unfinished.

## Ownership

Users/Accommodations: Christel. Transport/Attractions: Artiom. Trips/Settings: James.


## Implemented in-memory repositories

[InMemoryAttractionRepository](../src/TravelTracker.Data/Repositories/InMemoryAttractionRepository.cs) and [InMemoryTransportModeRepository](../src/TravelTracker.Data/Repositories/InMemoryTransportModeRepository.cs) implement the existing interfaces using the existing Domain entities. [InMemoryRoutePointRepository](../src/TravelTracker.Data/Repositories/InMemoryRoutePointRepository.cs) implements the new location contract. Api registers these thread-safe repositories as singletons and services as scoped.

Attraction/transport reads and writes copy mutable entities so callers cannot alter stored values without a repository operation. RoutePoint is immutable. Completed Tasks honor the original asynchronous signatures; memory operations perform no external I/O. Internal collection/update/delete methods required by the original contracts are implemented. Controllers expose GET-by-ID and POST for transport entries and route points; attractions also expose GET list with name filter and PATCH.

[Seeds](../src/TravelTracker.Data/SeedData) load once per repository instance. Created records survive requests and reset on restart. All six transport codes, including bike, are supported in memory; the SQL enum is unchanged. SourceId is unverified metadata rather than a database foreign key. Scheduling/timing calculations remain future work.

## Planned trip-point and route persistence

The [relationship design](trip-points-and-routes.md) separates reusable locations and transport entries from scheduled journeys and ordered trip occurrences. A later reviewed migration would add route points, scheduled routes with start/end point references, ordered stop occurrences, and trip legs/point snapshots. Stops need their own arrival/departure data because the same physical point can occur on many journeys. A future trip save must commit its itinerary and snapshots atomically within the Trips store. No such migration or SQL change is included in this documentation update.
