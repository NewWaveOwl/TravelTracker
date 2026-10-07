# Attractions and scheduled transport routes

## Human-approved scope

On 2026-10-06 the human requested runnable Attractions and Transport services, following LAB_07's controller -> service -> repository flow and the repository's coding/testing skills. The reviewed Attractions contract remains [feature #15](../issue-9/feature-spec.md). The human revised transport to reusable typed route points, transport entries with mode/name/description, and specific routes with departure, arrival and locations. A route is one scheduled journey in this implementation; there is no separate journey/template entity.

Both APIs target .NET 10 and own independent seeded in-memory stores. Attractions runs on 5104 and Transport on 5103. Data survives requests and resets on service restart. This is a local M1 demonstration, without authentication, booking, live routing, SQL or attraction suggestions. It does not implement the other owners' services.

## Transport representation and endpoints

All resources have server-generated GUIDs. JSON uses camelCase. Creates return 201 with the saved representation and a usable detail Location; reads return 200; unknown valid IDs return 404; malformed IDs, JSON, unknown fields/client IDs and invalid values return 400 validation problem details.

| Resource | Input fields | Routes under /api/v1 |
| --- | --- | --- |
| Transport entry | code, name, optional description | GET/POST /transport-modes; GET /transport-modes/{id} |
| Route point | type, name, optional address, lat, lon, optional sourceId | GET/POST /route-points; GET /route-points/{id} |
| Scheduled route | name, transportModeId, startPointId, destinationPointId, departureAt, arrivalAt, boardingMinutes, optional stops | GET/POST /transport-routes; GET /transport-routes/{id} |

`ITransportModeService` manages transport catalogue entries. `code` is one of car, bus, train, pedestrian, bike or plane. `name` identifies an entry, such as Express Bus; multiple entries may share a code. Bike is the human's addition to the original catalogue. Walking uses pedestrian. Names are trimmed and required; descriptions are nullable. This extends the shared scaffold's earlier single-mode catalogue, while leaving that unfinished scaffold's contracts untouched.

Point `type` is accommodation, attraction or transport_stop. Names and valid coordinates are required. Omitted address becomes an empty string; explicit null address is invalid. Optional sourceId identifies a catalogue item as metadata; it is not verified by cross-service database access or a required network call. Points contain location snapshots and do not become Trips' saved itinerary endpoints automatically.

Scheduled routes reference existing points and transport entries in Transport's store. The service saves immutable copies of the transport and point details so a later catalogue edit cannot move an existing route. Lists have deterministic name/GUID ordering. Mode-specific routing algorithms, speeds and inferred schedules are outside scope. All modes use the same input-time validation; direct journeys have no intermediate stops, while trains/buses can have several.

## Stop and timing contract

Each intermediate stop supplies pointId, arrivalAt, departureAt, boardingMinutes and canLeaveVehicle. Its position in the request array defines order. The start and destination are separate from intermediate stops.

- Every supplied timestamp must include an explicit UTC offset or Z. Store and return UTC instants. No local time is guessed; the future Trips editor remains responsible for time-zone selection and ambiguous local times.
- Final arrival is strictly after origin departure. Each intermediate arrival is strictly after the previous departure; stop departure is at or after its arrival and strictly before final arrival. Do not silently reorder stops.
- Boarding/preparation minutes are required nonnegative integers. They are manually supplied for finding a bike, boarding or security. Origin boardingStartsAt is departureAt minus boardingMinutes and must be representable.
- Stop boarding time must fit its downtime. Downtime is stop departure minus arrival. Potential free minutes are downtime minus boarding minutes only when canLeaveVehicle is true; otherwise zero. A large dwell alone does not establish sightseeing availability.
- Route elapsedMinutes includes travel and stop dwell. totalMinutes also includes origin preparation. Neither value claims moving time or real-world operational accuracy.
- Validate the complete request before inserting anything. Missing references return 404 and leave the route store unchanged.

Future attraction suggestions must fit travel out, visit, onward travel and buffers and remain subject to human review. This implementation only exposes potential windows.

## Ownership update

Transport owns transport entries, reusable point snapshots and individual scheduled routes. Trips continues to own the complete itinerary, accommodation-to-attraction/round-trip rules, inter-route connections, visit conflicts, trip dates and saving the whole trip atomically. Integration must use public HTTP contracts; no service reads another service's database. Recording route metadata does not broaden Trips' initial allowed endpoints to airports or stations.

## Implementation decisions and acceptance

The exact resource names, fields, status behavior, immutable snapshots and validation above are implementation decisions derived from the human's approved model. They are not invented interview answers.

Keep one operation per production implementation file, using partial controllers/services to retain LAB_07's familiar structure. The human later requested one test file per API and at most 30 discovered xUnit cases across both APIs. Use /** ... */ comments with @param/@return. Register scoped services/repositories and thread-safe singleton stores. Program contains startup/composition only.

Verify Attractions' complete existing acceptance table, and Transport's catalogue/point/route POST -> GET behavior, Location headers, mode/type validation, reference errors, multiple stops, offset normalization, overnight travel, preparation boundaries, dwell/free-window calculations, invalid atomic writes and state reset. Use focused xUnit and WebApplicationFactory tests, plus actual independent processes on 5103/5104. Recheck the full solution and report baseline failures separately.

The existing shared solution fails before this work at Id64.cs:14 (CS1503). Missing Trip/UserSettings/read-model definitions are also documented scaffold gaps. These APIs deliberately have no references to that scaffold; isolated passing checks are not a claim that the shared solution builds.
