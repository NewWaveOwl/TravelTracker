# Domain Model

[TravelTracker.Domain](../src/TravelTracker.Domain) owns the shared business models used by the API and Data projects. Existing names, property types and identifiers are preserved. Trip models live in the independent [TravelTracker.Trips.Domain](../src/TravelTracker.Trips.Domain) project, which has no project or package references, matching the other services' Domain projects.

Bearer validation belongs to the API, not Domain. The authenticated identity is the validated `(iss, sub)` pair. The independent Users service now maps that identity to a public UUIDv7 account and enforces live standing; the original shared `User` model below remains unchanged. See [authentication boundaries](authentication.md) and [Users setup](first-administrator-setup.md).

`TravelTracker.Users.Domain` owns `UserAccount`: public ID, validated issuer/subject, optional verified provider email, editable username, contact email and address, server-owned `traveller`/`administrator` role, `active`/`banned`/`deactivated` standing, creation time and last profile update time. Ordinary provisioning creates an active traveller with blank profile fields. First-administrator authorization and later role-change authorization are coordinated by the Users API with transactional persistence checks; the domain model does not trust token role claims or profile request fields.

Attractions now owns its model and identity primitives in `TravelTracker.Attractions.Domain`, independently of the remaining shared Domain. Ownership is the validated issuer/subject pair; shared/private visibility is server-enforced. The HTTP DTO omits owner and internal key fields. Invariants validate names, coordinate/rating ranges and nonnegative visit duration without EF or HTTP dependencies. See [the independent service contract](attractions-service.md).

`TravelTracker.Accommodations.Domain` owns the independent `Accommodation` model. It carries a public UUIDv7, name, address, decimal latitude/longitude, server-assigned validated issuer/subject owner, and private/shared flag. Pure invariants reject an empty ID, missing owner, blank name, null address and out-of-range coordinates. The legacy shared `Accomodation` remains unchanged; the independent service does not reuse it.

## Existing entities

| Entity | Existing fields | Owner / integration addition |
| --- | --- | --- |
| [User](../src/TravelTracker.Domain/Models/User.cs) | UserId (`EntityId<User>`), Username, Email, Address?, Role, Status, CreatedAt, UpdatedAt? | Christel; unchanged |
| [Accomodation](../src/TravelTracker.Domain/Models/Accomodation.cs) | Id (`EntityId<Accomodation>`), Name, Address | Christel; unchanged, original spelling retained |
| [Attraction](../src/TravelTracker.Attractions.Domain/Models/Attraction.cs) | AttractionId (`EntityId<Attraction>`), Name, Address, Latitude, Longitude, EstimatedVisitMinutes | Artiom; adds optional Description and Rating |
| [TransportMode](../src/TravelTracker.Domain/Models/TransportMode.cs) | TransportModeId (`EntityId<TransportMode>`), Code, DisplayName | Artiom; adds optional Description |

[UserRole](../src/TravelTracker.Domain/Models/UserRole.cs): User = 0, Administrator = 1.
[UserStatus](../src/TravelTracker.Domain/Models/UserStatus.cs): Active = 0, Deactivated = 1, Banned = 2.
User defaults to User role, Active status and the current UTC creation time.

## Identifiers

[EntityId](../src/TravelTracker.Domain/Types/EntityId.cs) pairs an internal [Id64](../src/TravelTracker.Domain/Types/Id64.cs) database key with a public uuidv7. Id64 is `internal` (visible to Data only), so the Api cannot name or expose it; only the Data layer's `EntityIdGenerator` creates identities. Ids are app-issued and sequential, with no database identity or sequence.

## New location model

[RoutePoint](../src/TravelTracker.Domain/Models/RoutePoint.cs) is an immutable shared record: RoutePointId (`EntityId<RoutePoint>`), Type, Name, Address, Latitude, Longitude, SourceId?. Supported types are accommodation, attraction and transport_stop. SourceId is optional unverified metadata. A point has no schedule times.

## HTTP representations

The Api [response DTOs](../src/TravelTracker.Transport.Api/Contracts) preserve the agreed JSON contract. AttractionRecord maps the original Latitude/Longitude to lat/lon; TransportModeRecord maps DisplayName to name; RoutePointRecord similarly maps coordinates. These DTOs describe HTTP responses, while storage/services use the Domain entities. Existing model property names are retained.

## Unfinished feature types

The independent [Settings domain](../src/TravelTracker.Settings.Domain/Models/UserSettings.cs) is the only `UserSettings` type; it defines the preference model, defaults and validation. The shared [TripMapPoint](../src/TravelTracker.Trips.Domain/Models/TripMapPoint.cs) (in TravelTracker.Trips.Domain) is an empty partial declaration needed by the existing interfaces. It supplies no fields, persistence or endpoints. Stay, Visit and TicketImport remain roadmap concepts; stay/visit invariants are deferred.

## Trip rules (implemented, pure domain in TravelTracker.Trips.Domain)

[Trip](../src/TravelTracker.Trips.Domain/Models/Trip.cs) and [TripLeg](../src/TravelTracker.Trips.Domain/Models/TripLeg.cs) are immutable. `Trip.Create` / `WithLegs` throw [TripValidationException](../src/TravelTracker.Trips.Domain/Models/TripValidationException.cs) listing every [TripError](../src/TravelTracker.Trips.Domain/Models/TripError.cs) (field path plus message), so a rejected edit leaves the old trip unchanged. Enforced: first leg starts at an accommodation; one-way ends at an attraction; round trip returns to its start and visits an attraction; legs are sequenced 1..n, connected, arrive after departing, do not overlap, and sit inside the trip start/end. Leg times carry an offset that must match the trip time zone; [TripTime.Resolve](../src/TravelTracker.Trips.Domain/Models/TripTime.cs) rejects impossible local times and requires an explicit offset for ambiguous ones. Nothing is corrected silently. Trip ownership, ids and persistence are not part of this layer.

## Planned trip rules

- One-way trip: accommodation to attraction; round trip includes a return leg.
- Legs are ordered, connected, and advance in time without sequence gaps.
- Supported transport codes are car, bus, train, pedestrian, bike and plane.
- Trips retain location/transport snapshots and enforce ownership/privacy and account status.

The [trip-point/transport/route design](trip-points-and-routes.md) distinguishes reusable locations/entries from scheduled journeys and ordered trip occurrences. These trip/schedule rules are future work; Transport and Route Points expose create/detail/collection selection; Attractions also supports PATCH.

Transport owns dedicated service domain models and identity types in `src/TravelTracker.Transport.Domain`; the historical shared RoutePoint is retained for existing Trip snapshots and does not cross the new service HTTP boundary.
