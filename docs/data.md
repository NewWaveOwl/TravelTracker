# Data Layer

[TravelTracker.Attractions.Data](../src/TravelTracker.Attractions.Data) owns the implemented PostgreSQL/EF Core context, migrations, attraction repository and identity allocator. [TravelTracker.Data](../src/TravelTracker.Data) retains Transport/Route Point in-memory stores and the other unfinished service contracts. The historical [SQL schema](../src/Database/schema.sql) is not the applied migration history of Attractions. See [migration/startup/ownership and test instructions](attractions-service.md).

## Selected database

**PostgreSQL is the selected database**, confirmed by the repository owner on 2026-10-28. EF Core persistence uses the `Npgsql.EntityFrameworkCore.PostgreSQL` provider. This resolves database decision 3 in [Milestone 3 contracts](milestone-3-contracts.md) for #35, #36 and the persistence work in #41. Provider package versions must be compatible with the project's .NET/EF Core version; no PostgreSQL server version has been selected here.

Keep each service's database ownership separate. Domain models remain free of EF Core/Npgsql dependencies; provider configuration, mappings and migrations belong in Data and API composition. Generate and verify migrations against PostgreSQL, and use isolated PostgreSQL databases for relational persistence tests. SQLite and EF InMemory are not substitutes for that evidence. Connection strings and credentials come from environment-specific configuration and must not be committed or logged.

PostgreSQL is now implemented for the independent Attractions service. The remaining Transport/Route Points host still uses in-memory repositories. The existing SQL schema remains a historical foundation, separate from Attractions migrations. See [Npgsql's provider documentation](https://www.npgsql.org/efcore/).

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
| [IAttractionRepository](../src/TravelTracker.Attractions.Data/Repositories/IAttractionRepository.cs) | public / `Guid` | `GetAllAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync` |
| [ITransportModeRepository](../src/TravelTracker.Data/Repositories/ITransportModeRepository.cs) | public / `Guid` | `GetAllAsync`, `GetByIdAsync`, `GetByCodeAsync`, `AddAsync`, `UpdateAsync` |
| [ISettingsRepository](../src/TravelTracker.Data/Repositories/ISettingsRepository.cs) | public / `Guid` (public id) | `GetByUserIdAsync`, `UpsertAsync` |
| [ITripRepository](../src/TravelTracker.Data/Repositories/ITripRepository.cs) | public / `Guid` (public id) | `GetAllByUserIdAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `ReplaceLegsAsync` |

`IUserRepository` and `IAccomadationRepository` are `internal`, no public path out of `TravelTracker.Data` for them yet.

The existing `IAccomadationRepository`/`Accomodation` spellings and all signatures are kept. User/accommodation create/update methods return the saved entity; attraction/transport add/update, settings upsert and trip replace-legs methods return task completion. Trips/Settings referenced types now have empty partial declarations for compilation; their data contracts and persistence remain unfinished.

## Ownership

Users/Accommodations: Christel. Transport/Attractions: Artiom. Trips/Settings: James.


## Implemented in-memory repositories

[EfAttractionRepository](../src/TravelTracker.Attractions.Data/Repositories/EfAttractionRepository.cs) uses the request-scoped AttractionsDbContext. Its `Attractions` table stores application-issued `Id` (bigint), unique `PublicId` (uuid), name/address/description, numeric coordinates/rating, visit duration, owner issuer/subject and shared visibility. These persistence-only identity/ownership fields do not expand the public response DTO. Transactions lock the target row for PATCH and roll back rejected candidates; database constraints enforce coordinate/rating/duration/name/ownership invariants. The migration is under `src/TravelTracker.Attractions.Data/Migrations`.

[InMemoryTransportModeRepository](../src/TravelTracker.Data/Repositories/InMemoryTransportModeRepository.cs) and [InMemoryRoutePointRepository](../src/TravelTracker.Data/Repositories/InMemoryRoutePointRepository.cs) retain thread-safe singleton stores in the remaining host. Its application services remain scoped.

Attraction reads return detached domain copies; EF rows do not escape Data. Transport reads/writes copy mutable entities; RoutePoint is immutable. Transport memory methods return completed Tasks, while Attractions uses cancellable asynchronous PostgreSQL I/O. Internal collection/update/delete methods remain internal contracts; controllers expose GET-by-ID and POST for Transport/Route Points, and create/detail/list/name-search/PATCH for Attractions.

[Seeds](../src/TravelTracker.Data/SeedData) load once per repository instance. Created records survive requests and reset on restart. All six transport codes, including bike, are supported in memory; the SQL enum is unchanged. SourceId is unverified metadata rather than a database foreign key. Scheduling/timing calculations remain future work.

## Planned trip-point and route persistence

The [relationship design](trip-points-and-routes.md) separates reusable locations and transport entries from scheduled journeys and ordered trip occurrences. A later reviewed migration would add route points, scheduled routes with start/end point references, ordered stop occurrences, and trip legs/point snapshots. Stops need their own arrival/departure data because the same physical point can occur on many journeys. A future trip save must commit its itinerary and snapshots atomically within the Trips store. No such migration or SQL change is included in this documentation update.
