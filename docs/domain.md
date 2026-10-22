# Domain Model

[TravelTracker.Domain](../src/TravelTracker.Domain) owns the business models used by the API and Data projects. Existing names, property types and identifiers are preserved.

## Existing entities

| Entity | Existing fields | Owner / integration addition |
| --- | --- | --- |
| [User](../src/TravelTracker.Domain/Models/User.cs) | UserId (`EntityId<User>`), Username, Email, Address?, Role, Status, CreatedAt, UpdatedAt? | Christel; unchanged |
| [Accomodation](../src/TravelTracker.Domain/Models/Accomodation.cs) | Id (`EntityId<Accomodation>`), Name, Address | Christel; unchanged, original spelling retained |
| [Attraction](../src/TravelTracker.Domain/Models/Attraction.cs) | AttractionId (`EntityId<Attraction>`), Name, Address, Latitude, Longitude, EstimatedVisitMinutes | Artiom; adds optional Description and Rating |
| [TransportMode](../src/TravelTracker.Domain/Models/TransportMode.cs) | TransportModeId (`EntityId<TransportMode>`), Code, DisplayName | Artiom; adds optional Description |

[UserRole](../src/TravelTracker.Domain/Models/UserRole.cs): User = 0, Administrator = 1.
[UserStatus](../src/TravelTracker.Domain/Models/UserStatus.cs): Active = 0, Deactivated = 1, Banned = 2.
User defaults to User role, Active status and the current UTC creation time.

## Identifiers

[EntityId](../src/TravelTracker.Domain/Types/EntityId.cs) pairs an internal [Id64](../src/TravelTracker.Domain/Types/Id64.cs) database key with a public uuidv7. Id64 is `internal` (visible to Data only), so the Api cannot name or expose it; only the Data layer's `EntityIdGenerator` creates identities. Ids are app-issued and sequential, with no database identity or sequence.

## New location model

[RoutePoint](../src/TravelTracker.Domain/Models/RoutePoint.cs) is an immutable shared record: RoutePointId (`EntityId<RoutePoint>`), Type, Name, Address, Latitude, Longitude, SourceId?. Supported types are accommodation, attraction and transport_stop. SourceId is optional unverified metadata. A point has no schedule times.

## HTTP representations

The Api [response DTOs](../src/TravelTracker.Api/Contracts) preserve the agreed JSON contract. AttractionRecord maps the original Latitude/Longitude to lat/lon; TransportModeRecord maps DisplayName to name; RoutePointRecord similarly maps coordinates. These DTOs describe HTTP responses, while storage/services use the Domain entities. Existing model property names are retained.

## Unfinished feature types

[UserSettings](../src/TravelTracker.Domain/Models/UserSettings.cs) and [TripMapPoint](../src/TravelTracker.Domain/Models/TripMapPoint.cs) are empty partial declarations needed by the existing interfaces. They supply no fields, persistence or endpoints. Stay, Visit and TicketImport remain roadmap concepts; stay/visit invariants are deferred.

## Trip rules (implemented, pure domain)

[Trip](../src/TravelTracker.Domain/Models/Trip.cs) and [TripLeg](../src/TravelTracker.Domain/Models/TripLeg.cs) are immutable. `Trip.Create` / `WithLegs` throw [TripValidationException](../src/TravelTracker.Domain/Models/TripValidationException.cs) listing every [TripError](../src/TravelTracker.Domain/Models/TripError.cs) (field path plus message), so a rejected edit leaves the old trip unchanged. Enforced: first leg starts at an accommodation; one-way ends at an attraction; round trip returns to its start and visits an attraction; legs are sequenced 1..n, connected, arrive after departing, do not overlap, and sit inside the trip start/end. Leg times carry an offset that must match the trip time zone; [TripTime.Resolve](../src/TravelTracker.Domain/Models/TripTime.cs) rejects impossible local times and requires an explicit offset for ambiguous ones. Nothing is corrected silently. Trip ownership, ids and persistence are not part of this layer.

## Planned trip rules

- One-way trip: accommodation to attraction; round trip includes a return leg.
- Legs are ordered, connected, and advance in time without sequence gaps.
- Supported transport codes are car, bus, train, pedestrian, bike and plane.
- Trips retain location/transport snapshots and enforce ownership/privacy and account status.

The [trip-point/transport/route design](trip-points-and-routes.md) distinguishes reusable locations/entries from scheduled journeys and ordered trip occurrences. These trip/schedule rules are future work; current endpoints are POST and GET by ID for the three catalogue resources.
