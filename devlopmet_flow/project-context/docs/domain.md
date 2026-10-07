# Domain Model

[TravelTracker.Domain](https://github.com/NewWaveOwl/TravelTracker/tree/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Domain) owns the business models used by the API and Data projects. Existing names, property types and identifiers are preserved.

## Existing entities

| Entity | Existing fields | Owner / integration addition |
| --- | --- | --- |
| [User](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Domain/Models/User.cs) | UserId (Guid), Username, Email, Address?, Role, Status, CreatedAt, UpdatedAt? | Christel; unchanged |
| [Accomodation](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Domain/Models/Accomodation.cs) | Id (Guid), Name, Address | Christel; unchanged, original spelling retained |
| [Attraction](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Domain/Models/Attraction.cs) | AttractionId (Guid), Name, Address, Latitude, Longitude, EstimatedVisitMinutes | Artiom; adds optional Description and Rating |
| [TransportMode](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Domain/Models/TransportMode.cs) | TransportModeId (Guid), Code, DisplayName | Artiom; adds optional Description |

[UserRole](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Domain/Models/UserRole.cs): User = 0, Administrator = 1.
[UserStatus](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Domain/Models/UserStatus.cs): Active = 0, Deactivated = 1, Banned = 2.
User defaults to User role, Active status and the current UTC creation time.

## Identifiers

[Id64](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Domain/Types/Id64.cs) stores 64 random bits, prints 16 hex characters and has long/ulong conversions. Settings/trip interfaces use it; current catalogue entities use GUIDs. The constructor now explicitly converts New() to long before delegating, fixing the original CS1503 without changing its public signatures.

## New location model

[RoutePoint](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Domain/Models/RoutePoint.cs) is an immutable shared record: RoutePointId (Guid), Type, Name, Address, Latitude, Longitude, SourceId?. Supported types are accommodation, attraction and transport_stop. SourceId is optional unverified metadata. A point has no schedule times.

## HTTP representations

The Api [response DTOs](https://github.com/NewWaveOwl/TravelTracker/tree/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Api/Contracts) preserve the agreed JSON contract. AttractionRecord maps the original Latitude/Longitude to lat/lon; TransportModeRecord maps DisplayName to name; RoutePointRecord similarly maps coordinates. These DTOs describe HTTP responses, while storage/services use the Domain entities. Existing model property names are retained.

## Unfinished feature types

[Trip](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Domain/Models/Trip.cs), [TripLeg](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Domain/Models/TripLeg.cs), [UserSettings](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Domain/Models/UserSettings.cs) and [TripMapPoint](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/src/TravelTracker.Domain/Models/TripMapPoint.cs) are empty partial declarations needed by the existing interfaces. They supply no fields, validation, persistence or endpoints. James's Trips/Settings feature contracts still need definition before implementation. Stay, Visit and TicketImport remain roadmap concepts.

## Planned trip rules

- One-way trip: accommodation to attraction; round trip includes a return leg.
- Legs are ordered, connected, and advance in time without sequence gaps.
- Supported transport codes are car, bus, train, pedestrian, bike and plane.
- Trips retain location/transport snapshots and enforce ownership/privacy and account status.

The [trip-point/transport/route design](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/docs/trip-points-and-routes.md) distinguishes reusable locations/entries from scheduled journeys and ordered trip occurrences. These trip/schedule rules are future work; current endpoints are POST and GET by ID for the three catalogue resources.
