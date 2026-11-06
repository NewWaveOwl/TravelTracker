# Data Layer

[TravelTracker.Attractions.Data](../src/TravelTracker.Attractions.Data) and [TravelTracker.Accommodations.Data](../src/TravelTracker.Accommodations.Data) own their PostgreSQL/EF Core contexts, migrations and repositories. [TravelTracker.Data](../src/TravelTracker.Data) retains historical unused in-memory helpers and other unfinished service contracts. The historical [SQL schema](../src/Database/schema.sql) is not the applied migration history of either independent service. See the [Attractions](attractions-service.md) and [Accommodations](accommodations-service.md) startup and test instructions.

## Selected database

**PostgreSQL is the selected database**, confirmed by the repository owner on 2026-10-28. EF Core persistence uses the `Npgsql.EntityFrameworkCore.PostgreSQL` provider. This resolves database decision 3 in [Milestone 3 contracts](milestone-3-contracts.md) for #35, #36, #38 and the persistence work in #41. Provider package versions must be compatible with the project's .NET/EF Core version; no PostgreSQL server version has been selected here.

Keep each service's database ownership separate. Domain models remain free of EF Core/Npgsql dependencies; provider configuration, mappings and migrations belong in Data and API composition. Generate and verify migrations against PostgreSQL, and use isolated PostgreSQL databases for relational persistence tests. EF InMemory is the default test-only provider for application/domain/HTTP/security tests; run `dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"` without PostgreSQL. Tests marked `Category=PostgreSQL` retain real constraints, migrations, locking, rollback and restart checks with `TRAVELTRACKER_TEST_POSTGRES` (CREATEDB). EF InMemory and SQLite do not substitute for that relational evidence. Production remains PostgreSQL; no runtime in-memory switch is added. Connection strings and credentials come from environment-specific configuration and must not be committed or logged.

PostgreSQL is implemented for the independent Attractions, Accommodations, Users and Transport services. Transport/Route Points share their Transport service database; see [Transport service](transport-service.md). The existing SQL schema remains a historical foundation, separate from each service's migrations. See [Npgsql's provider documentation](https://www.npgsql.org/efcore/).

## Schema

API bearer authentication adds no token storage or signing keys to Data. The independent `TravelTracker.Users.Data` now persists accounts keyed by the validated `(iss, sub)` pair in its own PostgreSQL database; it does not use the historical Users table below. See [API authentication](authentication.md) and [Users setup](first-administrator-setup.md).

## Users persistence

`TravelTracker.Users.Data` references its own Domain and EF Core/Npgsql only. Its committed migrations create account, permanent bootstrap-consumption and administrator-audit records. Public IDs are application-issued UUIDv7 values. Role changes and bootstrap share a transaction advisory lock, recheck live actor/target standing and roles, protect the last active administrator, and save the role plus actor/target/before/after/reason audit atomically. Listing also checks live administrator authority within the database transaction. First-use provisioning creates an active traveller and preserves existing standing and roles. The account row stores editable profile fields separately from verified provider email. A filtered unique index enforces case-insensitive username uniqueness. Profile writes check active standing and use an optimistic concurrency token.

Use a separate `ConnectionStrings:Users`; the Attractions service consumes the authenticated HTTPS access contract and never reads Users tables. Production uses PostgreSQL. Users HTTP tests use an isolated test repository; Marked PostgreSQL tests exercise migrations, profile persistence, concurrency, permanent bootstrap consumption and audit rollback against disposable databases.

## Accommodations persistence

`TravelTracker.Accommodations.Data` references its service-owned Domain and EF Core/Npgsql. Its initial migration creates the `Accommodations` catalogue table with a UUID primary key, name, address, decimal coordinates, validated owner issuer/subject and `IsShared`. Database checks enforce nonblank names, coordinate ranges and nonblank owner identity. The repository applies private/shared visibility predicates in PostgreSQL before materializing search or detail results. The Accommodations database is separate from the historical shared schema and every other service database. Configure `ConnectionStrings:Accommodations`; migrations are opt-in at startup through `Database:ApplyMigrations`.

Enums: `TransportMode` (`car`, `bus`, `train`, `pedestrian`, `plane`), `TripType` (`one_way`, `round_trip`).

| Table | Columns |
|---|---|
| `Users` | `Id` bigint PK (app-issued), `Uuid` unique, `UserName` unique, `Password`, `Address` |
| `Accommodations` | `Id` bigint PK (app-issued), `Uuid` unique, `Name`, `Address` |
| `Attractions` | `Id` bigint PK (app-issued), `Uuid` unique, `Name`, `Address` |
| `Trips` | `Id` bigint PK (app-issued), `Uuid` unique, `UserId` FK -> `Users`, `Transport`, `Type`, `DeparturePlaceId` FK -> `Accommodations`, `DestinationPlaceId` FK -> `Attractions` |

No tables yet for `TripLegs`, `Stays`, `Visits`, `TicketImports`, route points, scheduled transport routes or intermediate stops.

Note: `Trips` stores one leg directly on the row (one transport, one departure, one destination). `ITripRepository.ReplaceLegsAsync` already assumes multiple legs per trip, so the schema needs a `TripLegs` table before that method can be implemented.

## Repository interfaces

| Preserved interface | Accessibility / ID type | Declared methods |
|---|---|---|
| [IUserRepository](../src/TravelTracker.Data/Repositories/IUserRepository.cs) | internal / `Guid` | `GetByIdAsync`, `GetByEmailAsync`, `GetAllAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `ChangeStatusAsync` |
| [IAccomadationRepository](../src/TravelTracker.Data/Repositories/IAccomadationRepository.cs) | internal / `Guid` | `GetByIdAsync`, `GetAllAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` |
| [IAccommodationRepository](../src/TravelTracker.Accommodations.Data/Repositories/IAccommodationRepository.cs) | public / `Guid` | `GetVisibleAsync`, `GetVisibleByIdAsync`, `AddAsync` |
| [IAttractionRepository](../src/TravelTracker.Attractions.Data/Repositories/IAttractionRepository.cs) | public / `Guid` | `GetAllAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync` |
| [ITransportModeRepository](../src/TravelTracker.Transport.Data/Repositories/ITransportModeRepository.cs) | public / `Guid` | `GetAllAsync`, `GetByIdAsync`, `GetByCodeAsync`, `AddAsync`, `UpdateAsync` |
| [ITripRepository](../src/TravelTracker.Data/Repositories/ITripRepository.cs) | public / `Guid` (public id) | `GetAllByUserIdAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `ReplaceLegsAsync` |

`IUserRepository` and `IAccomadationRepository` are `internal`, no public path out of `TravelTracker.Data` for them yet.

The existing `IAccomadationRepository`/`Accomodation` spellings and all signatures are kept. User/accommodation create/update methods return the saved entity; attraction/transport add/update, trip replace-legs methods return task completion. Trip referenced types now have empty partial declarations for compilation; their data contracts and persistence remain unfinished.

## Ownership

Users/Accommodations: Christel. Transport/Attractions: Artiom. Trips/Settings: James.


## Implemented service repositories

Attractions owns its EF context, mappings and migration history. Accommodations owns `AccommodationsDbContext`, its mapping, initial migration and visibility-scoped repository. Transport owns `TransportDbContext`, `EfTransportModeRepository`, `EfRoutePointRepository` and the initial Transport migration. All use PostgreSQL/Npgsql. Accommodation rows store their public UUID directly as the primary key with owner/visibility and coordinate constraints. Transport's dedicated tables store internal app-issued bigint keys and unique public UUIDs, enforce supported mode/type and coordinate constraints, and return detached domain values. Seeds preserve the existing sample public identities and cover all six modes, including bike. SourceId is unverified metadata with no foreign key. See [Transport startup, allocation limits and relational testing](transport-service.md).

Historical in-memory repositories in `TravelTracker.Data` remain unused by runnable hosts; shared Trip domain tests still use the historical identity/model foundation. They do not represent production persistence.

## Planned trip-point and route persistence

The [relationship design](trip-points-and-routes.md) separates reusable locations and transport entries from scheduled journeys and ordered trip occurrences. A later reviewed migration would add route points, scheduled routes with start/end point references, ordered stop occurrences, and trip legs/point snapshots. Stops need their own arrival/departure data because the same physical point can occur on many journeys. A future trip save must commit its itinerary and snapshots atomically within the Trips store. No such migration or SQL change is included in this documentation update.
