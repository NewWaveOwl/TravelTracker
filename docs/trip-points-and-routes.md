# Trip points, transport entries and routes

## Current implementation

Attractions now owns an independent Api/Data/Domain service on 5104 with PostgreSQL and private/shared access. Transport entries and Route Points remain in TravelTracker.Api on 5105 with in-memory stores. HTTP DTOs preserve name/lat/lon aliases. Trip/TripLeg domain rules exist, but trip/settings HTTP and persistence features remain unfinished. See [independent Attractions startup and access rules](attractions-service.md).

[Domain](domain.md), [API](api.md) and [Data](data.md) describe current models and contracts. The relationships and timing rules below are future product design.

## Meaning of each concept

| Concept | Meaning | Dates and relationships |
| --- | --- | --- |
| RoutePoint (implemented) | A reusable named location, with type accommodation, attraction or transport_stop, address and coordinates | No journey dates. Optional sourceId is unverified catalogue metadata. |
| Trip point (planned) | A trip's ordered use of a RoutePoint, such as its start, a reached attraction or a transfer location | Trips may use the same location more than once and keep a saved location snapshot. No TripPoint class or endpoint exists yet. |
| Transport entry (implemented) | A named catalogue entry with code car, bus, train, pedestrian, bike or plane and optional description | Reusable across journeys; has no departure or arrival date. |
| Transport route (planned) | One scheduled journey using one transport entry, a start RoutePoint and a destination RoutePoint | Own departure/arrival and boarding/preparation time; optional ordered intermediate stops. |
| Route stop (planned) | One occurrence of a RoutePoint during a route | Arrival/departure, boarding minutes and permission to leave the vehicle. |
| Trip and TripLeg (planned) | The complete itinerary and one ordered selected journey within it | Trips owns user/privacy, route connections, trip dates, stays, visits and an atomic save. |

A transport entry is not a route, and a location is not a schedule. Multiple routes can use the same transport entry or location. Repeated visits to a location are separate occurrences rather than new physical places.

## Relationships

```mermaid
erDiagram
    TRIP ||--o{ TRIP_POINT : contains
    ROUTE_POINT ||--o{ TRIP_POINT : used_as
    TRIP ||--o{ TRIP_LEG : contains
    TRIP_LEG }o--|| TRANSPORT_ROUTE : selects
    TRANSPORT_ENTRY ||--o{ TRANSPORT_ROUTE : used_by
    ROUTE_POINT ||--o{ TRANSPORT_ROUTE : starts
    ROUTE_POINT ||--o{ TRANSPORT_ROUTE : ends
    TRANSPORT_ROUTE ||--o{ ROUTE_STOP : contains
    ROUTE_POINT ||--o{ ROUTE_STOP : located_at
```

The diagram describes future integration. Only RoutePoint, transport entries and Attractions have current GET-by-ID/POST endpoints. Scheduling and Trips behavior remain future work. These are logical relationships, not deployed SQL foreign keys.

A future scheduled-route contract would identify the transport entry, start/destination route points and each intermediate stop's point. The Trips service would select the route for a leg, associate that leg with its ordered trip-point occurrences, and save the chosen transport/location details as snapshots. The trip would stay readable if a catalogue changed or became unavailable. Final field names, ID conversions between the GUID catalogue and Id64 Trips contracts, and cross-owner validation still need review before implementation.

Accommodation and attraction points can carry a sourceId for the corresponding place catalogue. Transport-stop points can stand alone. The current API merely stores that optional GUID; it does not verify a source record or keep its fields synchronized. An accommodation point therefore does not create an Accomodation entity, and an attraction point does not create an Attraction entity.

## Planned timing rules

Times belong to a specific journey or point occurrence. Creating a transport entry such as a bus or bike does not schedule a journey.

- A scheduled route supplies departure/arrival times and nonnegative boarding/preparation minutes. Preparation applies to every mode: reaching a bus, station/airport checks, or finding and preparing a bike. Exact values are entered rather than inferred from the mode.
- Intermediate stops are ordered, have arrival/departure times inside the route, and can repeat a location. Arrival must not follow departure; stop times and route legs must advance without overlap.
- The traveller must allow preparation before departure. The future editor follows the trip time-zone rules in [Design](design.md) and must clarify ambiguous local times.
- A stop's downtime is departure minus arrival. Potential free time subtracts preparation when leaving the vehicle is allowed. A future attraction suggestion must also fit travel out, the visit, return and buffers. Nothing is added automatically.

These rules are documented for later implementation; the current GET/POST catalogue performs no timing calculations.

## Example

- P1: accommodation, Hotel.
- P2: transport_stop, Station.
- P3: attraction, Museum.
- T1: bus, Example Bus.
- R1 (future): uses T1, starts at P1, ends at P3, with P2 as an intermediate stop; departure, arrival and preparation belong to R1 and its stop.

A future trip can select R1 as an itinerary leg and add visits/stays around it. Its global accommodation-to-attraction or round-trip rules remain owned by Trips. A transport-stop point can be a route endpoint or intermediate transfer; it does not automatically become the entire trip's start or destination.

## API and persistence boundaries

Resource controllers remain separate while sharing http://localhost:5104. Catalogue storage is in-memory and resets on restart. The existing Api/Data/Domain projects in [TravelTracker.slnx](../TravelTracker.slnx) form the implementation: Api references Data and Domain, Data references Domain. Api is the only web host. Attraction/Transport use the original entities and interface signatures; response DTOs retain the agreed JSON fields.

The main SQL Trips row currently stores one departure/destination directly; ordered trip legs, route references, point snapshots and intermediate stop occurrences need a later reviewed database migration. The original SQL mode enum also lacks bike, which the current in-memory catalogue supports. The SQL schema is not used by the in-memory catalogue; persistence requires a later reviewed migration.

See [API contracts](api.md), [domain records](domain.md), [data status](data.md) and [validation record](https://github.com/NewWaveOwl/TravelTracker/blob/milestone_2_AI_and_development_resource/devlopmet_flow/atiom_developmet_flow/implementation/validation.md).
