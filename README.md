# TravelTracker

TravelTracker is a .NET 10 backend for a travel-planning application. The project aims to bring journeys, accommodation stays and attraction visits together in one itinerary, with trip history and a map view.

The current implementation provides a working API for **attractions, transport entries and route points**. These resources run in one application and can be explored through Swagger. Full itinerary planning and the traveller-facing interface remain under development.

## Current capabilities

| Area | Current functionality |
| --- | --- |
| Attractions | Create and retrieve attractions with a name, address, coordinates, estimated visit duration, optional description and optional rating. |
| Transport entries | Create and retrieve named transport entries using `car`, `bus`, `train`, `pedestrian`, `bike` or `plane`. |
| Route points | Create and retrieve reusable locations of type `accommodation`, `attraction` or `transport_stop`. |
| Validation | Validate required fields, supported codes and types, coordinate ranges, ratings and visit duration. |
| API exploration | Use one Swagger page to inspect and execute all implemented endpoints. |
| Health | Check the API’s status and service identity through `/health`. |

New records receive public UUIDv7 identifiers. The identity system separates public identifiers from internal numeric keys.

Repositories contain sample data and store changes **in memory**. Created records remain available until the API restarts; restarting restores the sample data. No database setup or external service is required to run the current API.

## Project status

TravelTracker currently provides the catalogue and location foundation for future trip planning.

The following features are not yet available through the running application:

- User registration, authentication and account management.
- Accommodation management as a separate resource.
- Trips, ordered itinerary legs, history, settings and map views.
- Scheduled journeys, departure and arrival times, and stop timing.
- Collection listing, search, PATCH and DELETE endpoints.
- Persistent database storage.
- A traveller-facing frontend.

User and accommodation models and repository contracts exist. `Trip`, `TripLeg`, `UserSettings` and `TripMapPoint` are placeholder declarations supporting existing interfaces; their runtime features are unfinished.

A route point describes a reusable location. A transport entry describes a transport option. Neither currently represents a scheduled journey.

## Technology and structure

- .NET 10 and ASP.NET Core.
- Swagger/OpenAPI through Swashbuckle.
- In-memory repositories.
- xUnit and ASP.NET Core test hosts.
- GitHub Actions for pull-request testing.

| Project | Responsibility |
| --- | --- |
| `src/TravelTracker.Api` | Application startup, controllers, HTTP contracts and services. |
| `src/TravelTracker.Data` | Repositories, sample data and identity generation. |
| `src/TravelTracker.Domain` | Shared models and identifier types. |
| `tests/TravelTracker.Api.Tests` | HTTP integration tests and identity-generation tests. |

The SQL schema in `src/Database/schema.sql` is a foundation for future persistence. The current API does not use it to store records.

## Getting started

Install the **.NET 10 SDK**, then clone the repository:

```powershell
git clone https://github.com/NewWaveOwl/TravelTracker.git
cd TravelTracker
```

Build the solution:

```powershell
dotnet build TravelTracker.slnx --configuration Release
```

Start the API:

```powershell
dotnet run --project src/TravelTracker.Api --launch-profile http
```

Open [Swagger](http://localhost:5104/swagger). The `http` launch profile runs the application in Development and enables Swagger.

## API endpoints

Base URL: `http://localhost:5104`

| Resource | Method | Route |
| --- | --- | --- |
| Health | GET | `/health` |
| Attractions | POST | `/api/v1/attractions` |
| Attractions | GET | `/api/v1/attractions/{id}` |
| Transport entries | POST | `/api/v1/transport-modes` |
| Transport entries | GET | `/api/v1/transport-modes/{id}` |
| Route points | POST | `/api/v1/route-points` |
| Route points | GET | `/api/v1/route-points/{id}` |

In Swagger, expand an endpoint, select **Try it out**, enter the required values and select **Execute**.

Successful creation returns `201 Created`, the saved record and a `Location` header. Use the returned identifier to retrieve the record. Unknown identifiers return `404 Not Found`; invalid request input returns `400 Bad Request`.

See the [API documentation](docs/api.md) and [HTTP examples](src/TravelTracker.Api/requests.http) for request bodies and response details.

## Testing

Run the full test suite from the repository root:

```powershell
dotnet test TravelTracker.Tests.slnx --configuration Release
```

The suite contains **62 test cases** covering health, attraction and transport operations, route points, validation and concurrent identity generation. Attraction query coverage includes rejection of repeated `name` parameters and compatibility of single-value literal name search.

Tests create isolated application hosts automatically, so a manually running API is unnecessary. GitHub Actions runs the test solution for pull requests.

## Documentation

- [Product design and roadmap](docs/design.md)
- [API contracts](docs/api.md)
- [Domain models](docs/domain.md)
- [Data layer and SQL schema](docs/data.md)
- [Trip points, transport and route design](docs/trip-points-and-routes.md)
- [Contribution workflow](CONTRIBUTING.md)

Design documents include planned features as well as implemented behavior. The capabilities and status sections above describe what the current application supports.

## Development records

Milestone 2 planning, AI evidence, reviews, validation and reflections are maintained on the `milestone_2_AI_and_development_resource` branch:

- [AI evidence](https://github.com/NewWaveOwl/TravelTracker/tree/milestone_2_AI_and_development_resource/AI_evdence)
- [Development flow](https://github.com/NewWaveOwl/TravelTracker/tree/milestone_2_AI_and_development_resource/devlopmet_flow)

Repository guidance is available in the [coding skill](.github/skills/code/SKILL.md) and [testing skill](.github/skills/testing/SKILL.md).
