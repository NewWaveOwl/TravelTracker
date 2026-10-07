# Domain Model

The original [TravelTracker.Domain](../src/TravelTracker.Domain) models from the shared `master` branch are preserved alongside the current WebApi records. Their names, fields and identifiers have not been rewritten. See [main-code preservation and relationships](trip-points-and-routes.md).

## Id64

[Id64](../src/TravelTracker.Domain/Types/Id64.cs): 64 random bits, prints as 16 hex chars, and has `long`/`ulong` conversions. Settings/trip interfaces use it; the existing models below still use `Guid`. Its existing constructor error prevents the full solution from building. This documentation change leaves that source untouched.

## Entities

| Preserved entity | Fields | Owner |
|---|---|---|
| [User](../src/TravelTracker.Domain/Models/User.cs) | `UserId` (Guid), `Username`, `Email`, `Address?`, `Role`, `Status`, `CreatedAt`, `UpdatedAt?` | Christel |
| [Accomodation](../src/TravelTracker.Domain/Models/Accomodation.cs) | `Id` (Guid), `Name`, `Address` | Christel |
| [Attraction](../src/TravelTracker.Domain/Models/Attraction.cs) | `AttractionId` (Guid), `Name`, `Address`, `Latitude`, `Longitude`, `EstimatedVisitMinutes` | Artiom |
| [TransportMode](../src/TravelTracker.Domain/Models/TransportMode.cs) | `TransportModeId` (Guid), `Code`, `DisplayName` | Artiom |

[UserRole](../src/TravelTracker.Domain/Models/UserRole.cs): `User = 0`, `Administrator = 1`.
[UserStatus](../src/TravelTracker.Domain/Models/UserStatus.cs): `Active = 0`, `Deactivated = 1`, `Banned = 2`.

`User` defaults to the User role, Active status and the current UTC creation time. The original `Accomodation` spelling is retained for compatibility with collaborators' code.

## Not implemented yet

The following gaps refer to the original shared Domain scaffold, not the combined API records below.

Referenced by the preserved service/repository interfaces, with no model file yet: `Trip`, `TripLeg`, `UserSettings`, `TripMapPoint` (read-model). `Stay`, `Visit` and `TicketImport` are additional roadmap concepts. James owns Trips/Settings; their existing contracts are documented in [API](api.md) and [Data](data.md).

## Planned business rules

- One-way trip: accommodation -> attraction. Round trip: adds a return leg, attraction -> accommodation.
- Legs sequence from 1 with no gaps; each leg starts where the last one ended; arrival after departure.
- Transport modes: car, bus, train, pedestrian, bike, plane. Mixed transport = multiple legs with different modes.
- Banned user: can sign in and read own data, can't write. Deactivated/deleted: can't sign in.


## Implemented combined API records

These immutable records belong to TravelTracker.WebApi and use the human-approved GUID contracts. They have no references to the unfinished shared Domain project.

| Record | Owning project | Fields |
| --- | --- | --- |
| [AttractionRecord](../src/TravelTracker.WebApi/Models/AttractionRecord.cs) | TravelTracker.WebApi | AttractionId, Name, Description, Rating, Address, Lat, Lon, EstimatedVisitMinutes |
| [TransportModeRecord](../src/TravelTracker.WebApi/Models/TransportModeRecord.cs) | TravelTracker.WebApi | TransportModeId, Code, Name, Description |
| [RoutePointRecord](../src/TravelTracker.WebApi/Models/RoutePointRecord.cs) | TravelTracker.WebApi | RoutePointId, Type, Name, Address, Lat, Lon, SourceId |

Route points can represent accommodation, attraction or transport_stop locations. Entries and points have no departure/arrival dates. Scheduled journeys, intermediate-stop timings, boarding time and future attraction suggestions remain future work after the human reduced the implementation to POST and GET by ID. [Current contract](milestone-2/implementation/specification.md).

The WebApi records are separate contracts: `Name` does not rename the original transport's `DisplayName`, and `Lat`/`Lon` do not rename the original attraction's `Latitude`/`Longitude`. No adapter connects the two sets yet. A future trip uses ordered occurrences of route points and selects transport routes for its legs; the [relationship design](trip-points-and-routes.md) explains that integration without changing the preserved models.
