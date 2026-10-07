# Data Layer

The original [SQL schema](../src/Database/schema.sql) and [TravelTracker.Data repository interfaces](../src/TravelTracker.Data/Repositories) from `master` are preserved unchanged. This shared project contains schema/contracts, with no `DbContext` or concrete persistence implementation yet. The active WebApi has its own in-memory repositories, described below.

## Schema

Enums: `TransportMode` (`car`, `bus`, `train`, `pedestrian`, `plane`), `TripType` (`one_way`, `round_trip`).

| Table | Columns |
|---|---|
| `Users` | `Id` bigint identity PK, `Uuid` unique, `UserName` unique, `Password`, `Address` |
| `Accommodations` | `Id` bigint identity PK, `Uuid` unique, `Name`, `Address` |
| `Attractions` | `Id` bigint identity PK, `Uuid` unique, `Name`, `Address` |
| `Trips` | `Id` bigint identity PK, `Uuid` unique, `UserId` FK -> `Users`, `Transport`, `Type`, `DeparturePlaceId` FK -> `Accommodations`, `DestinationPlaceId` FK -> `Attractions` |

No tables yet for `UserSettings`, `TripLegs`, `Stays`, `Visits`, `TicketImports`, route points, scheduled transport routes or intermediate stops.

Note: `Trips` stores one leg directly on the row (one transport, one departure, one destination). `ITripRepository.ReplaceLegsAsync` already assumes multiple legs per trip, so the schema needs a `TripLegs` table before that method can be implemented.

## Repository interfaces

| Preserved interface | Accessibility / ID type | Declared methods |
|---|---|---|
| [IUserRepository](../src/TravelTracker.Data/Repositories/IUserRepository.cs) | internal / `Guid` | `GetByIdAsync`, `GetByEmailAsync`, `GetAllAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `ChangeStatusAsync` |
| [IAccomadationRepository](../src/TravelTracker.Data/Repositories/IAccomadationRepository.cs) | internal / `Guid` | `GetByIdAsync`, `GetAllAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` |
| [IAttractionRepository](../src/TravelTracker.Data/Repositories/IAttractionRepository.cs) | public / `Guid` | `GetAllAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync` |
| [ITransportModeRepository](../src/TravelTracker.Data/Repositories/ITransportModeRepository.cs) | public / `Guid` | `GetAllAsync`, `GetByIdAsync`, `GetByCodeAsync`, `AddAsync`, `UpdateAsync` |
| [ISettingsRepository](../src/TravelTracker.Data/Repositories/ISettingsRepository.cs) | public / `Id64` | `GetByUserIdAsync`, `UpsertAsync` |
| [ITripRepository](../src/TravelTracker.Data/Repositories/ITripRepository.cs) | public / `Id64` | `GetAllByUserIdAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `ReplaceLegsAsync` |

`IUserRepository` and `IAccomadationRepository` are `internal`, no public path out of `TravelTracker.Data` for them yet.

The existing `IAccomadationRepository`/`Accomodation` spellings and all signatures are kept. User/accommodation create/update methods return the saved entity; attraction/transport add/update, settings upsert and trip replace-legs methods return task completion. Missing Trips/Settings models remain an integration gap, rather than evidence of working persistence.

## Ownership

Users/Accommodations: Christel. Transport/Attractions: Artiom. Trips/Settings: James.


## Implemented combined API repositories

The combined TravelTracker.WebApi does not reference TravelTracker.Data or the SQL schema above. Attraction, TransportMode and RoutePoint each have a repository interface and a singleton in-memory implementation owning a ConcurrentDictionary of immutable records. Services are scoped; no separate store adapter or database context is required. Synchronous lookup/insertion performs no external I/O.

Fixed seeds load once per host. Created records survive subsequent requests and disappear on restart. The Transport catalogue supports all six codes, including bike; the legacy SQL enum is unchanged. Route-point sourceId is optional metadata, not a verified foreign key. There are no updates, scheduled-route writes or stop-time calculations in the reduced demonstration. [Current evidence](milestone-2/implementation/validation.md).

## Planned trip-point and route persistence

The [relationship design](trip-points-and-routes.md) separates reusable locations and transport entries from scheduled journeys and ordered trip occurrences. A later reviewed migration would add route points, scheduled routes with start/end point references, ordered stop occurrences, and trip legs/point snapshots. Stops need their own arrival/departure data because the same physical point can occur on many journeys. A future trip save must commit its itinerary and snapshots atomically within the Trips store. No such migration or SQL change is included in this documentation update.
