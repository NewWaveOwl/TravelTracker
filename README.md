# TravelTracker

TravelTracker is a .NET 10 backend for a travel-planning application. The project aims to bring journeys, accommodation stays and attraction visits together in one itinerary, with trip history and a map view.

**Attractions now runs independently on port 5104 with PostgreSQL persistence and creator-private/admin-shared access.** Transport entries and Route Points remain in `TravelTracker.Api` on port 5105 with in-memory storage. Both hosts provide Development Swagger. Full itinerary planning and the traveller-facing interface remain under development.

## Current capabilities

| Area | Current functionality |
| --- | --- |
| Attractions | Create, list (with case-insensitive name search), retrieve and partially update attractions with a name, address, coordinates, estimated visit duration, optional description and optional rating. |
| Transport entries | Create and retrieve named transport entries using `car`, `bus`, `train`, `pedestrian`, `bike` or `plane`. |
| Route points | Create and retrieve reusable locations of type `accommodation`, `attraction` or `transport_stop`. |
| Validation | Validate required fields, supported codes and types, coordinate ranges, ratings and visit duration. |
| API authentication | Validate bearer tokens and require separate catalogue read/write permissions. Health remains anonymous. |
| API exploration | Use one Swagger page to inspect and execute all implemented endpoints. |
| Health | Check the API’s status and service identity through `/health`. |

New records receive public UUIDv7 identifiers. The identity system separates public identifiers from internal numeric keys.

Attractions records survive restarts in its service-owned PostgreSQL database. Transport/Route Point changes remain in memory and reset on restart. Protected requests need a validated access token; Attractions also requires a configured live Users access endpoint. See [independent Attractions startup, privacy rules and PostgreSQL testing](docs/attractions-service.md). Automated tests use isolated signing keys, Users HTTP stubs and disposable PostgreSQL databases.

## Project status

TravelTracker currently provides the catalogue and location foundation for future trip planning.

The following features are not yet available through the running application:

- User registration, interactive sign-in, account standing and account management.
- Accommodation management as a separate resource.
- Trips, ordered itinerary legs, history, settings and map views.
- Scheduled journeys, departure and arrival times, and stop timing.
- Collection listing, PATCH and DELETE endpoints for transport entries and route points, and DELETE endpoints for every resource.
- Persistent database storage for the remaining service hosts.
- A traveller-facing frontend.

User and accommodation models and repository contracts exist. Trip/TripLeg domain rules are implemented, but their HTTP/persistence features remain unfinished. UserSettings and TripMapPoint remain placeholders.

A route point describes a reusable location. A transport entry describes a transport option. Neither currently represents a scheduled journey.

## Technology and structure

- .NET 10 and ASP.NET Core.
- Swagger/OpenAPI through Swashbuckle.
- PostgreSQL persistence for Attractions; in-memory repositories for Transport/Route Points.
- PostgreSQL through EF Core/Npgsql (owner decision, 2026-10-28), implemented for Attractions; other service persistence remains separate work.
- xUnit and ASP.NET Core test hosts.
- GitHub Actions for pull-request testing.

| Project | Responsibility |
| --- | --- |
| `src/TravelTracker.Attractions.Api` | Independent attraction HTTP contracts, application rules and live Users access checks. |
| `src/TravelTracker.Attractions.Data` | PostgreSQL context, migrations, repositories and application-issued identities. |
| `src/TravelTracker.Attractions.Domain` | Attraction model, identifiers and invariants without web/EF dependencies. |
| `src/TravelTracker.Security` | Shared bearer validation and OpenAPI security infrastructure. |
| `src/TravelTracker.Api` | Remaining Transport/Route Points startup, controllers and services. |
| `src/TravelTracker.Data` | Repositories, sample data and identity generation. |
| `src/TravelTracker.Domain` | Shared models and identifier types. |
| `tests/TravelTracker.Api.Tests` | Remaining host, shared authentication, trip-domain and identity tests. |
| `tests/TravelTracker.Attractions.Api.Tests` | Attraction HTTP, PostgreSQL durability/privacy and real-bearer integration tests. |

The historical SQL schema in `src/Database/schema.sql` is not applied automatically. Attractions uses its committed EF migrations; other hosts still have no database persistence.

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

Start the API in Development to inspect health and Swagger; Development-only placeholder identity settings let it start without an external provider:

```powershell
dotnet run --project src/TravelTracker.Attractions.Api --launch-profile http
```

The Development placeholders point to `identity.example.invalid`, which is not a running identity provider. Requests carrying a token cannot complete discovery until these values are replaced. Health and Swagger work without a token. Protected operations need a real provider-issued token; override the placeholders with the provider's settings before using them:

```powershell
$env:Authentication__Bearer__Authority = 'https://your-provider.example/tenant'
$env:Authentication__Bearer__Issuer = 'https://your-provider.example/tenant'
$env:Authentication__Bearer__Audience = 'traveltracker-api'
```

Use the provider's exact issuer from its discovery document; it need not equal the authority URL. See [API authentication](docs/authentication.md) for scope, algorithm and clock-tolerance settings. Production has no provider defaults and rejects missing or insecure configuration at startup.

Open [Swagger](http://localhost:5104/swagger). The `http` launch profile runs Attractions in Development and enables Swagger. Its public health response does not assert PostgreSQL/Users readiness. Start the remaining host separately on 5105 when needed.

## API endpoints

Attractions base URL: `http://localhost:5104`; Transport/Route Points: `http://localhost:5105`. Both expose `/health`; Attractions identifies itself as `Attractions`. Configure PostgreSQL/migrations and the live Users boundary using the [service guide](docs/attractions-service.md).

| Resource | Method | Route |
| --- | --- | --- |
| Health | GET | `/health` |
| Attractions | POST | `/api/v1/attractions` |
| Attractions | GET | `/api/v1/attractions` (optional `name` filter) |
| Attractions | GET | `/api/v1/attractions/{id}` |
| Attractions | PATCH | `/api/v1/attractions/{id}` |
| Transport entries | POST | `/api/v1/transport-modes` |
| Transport entries | GET | `/api/v1/transport-modes/{id}` |
| Route points | POST | `/api/v1/route-points` |
| Route points | GET | `/api/v1/route-points/{id}` |

In Swagger, select **Authorize** and enter an access token for this API. Reads require `catalogue.read`; writes require `catalogue.write`. Then expand an endpoint, select **Try it out**, enter the required values and select **Execute**. `/health` is anonymous. Missing or invalid tokens return `401`; missing permissions return `403`.

Successful creation returns `201 Created`, the saved record and a `Location` header. Use the returned identifier to retrieve the record. Unknown identifiers return `404 Not Found`; invalid request input returns `400 Bad Request`.

See the [API documentation](docs/api.md) and [HTTP examples](src/TravelTracker.Api/requests.http) for request bodies and response details.

## Testing

Run the full test suite from the repository root:

```powershell
dotnet test TravelTracker.Tests.slnx --configuration Release
```

The suite covers health, attraction and transport operations, route points, validation, concurrent identity generation, and real bearer-token validation and scope policies. Attraction query coverage includes rejection of repeated `name` parameters and compatibility of single-value literal name search. Tests configure isolated signing keys and require no external identity provider. Attraction persistence tests require PostgreSQL; they never substitute SQLite/EF InMemory.

Contracts for the planned multi-service architecture are in [milestone 3 contracts](docs/milestone-3-contracts.md).

Tests create isolated application hosts automatically. PostgreSQL suites require `TRAVELTRACKER_TEST_POSTGRES` with permission to create disposable test databases; see the service guide. GitHub Actions supplies an isolated PostgreSQL service and runs both test projects.

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
