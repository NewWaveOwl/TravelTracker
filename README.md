# TravelTracker

TravelTracker is a .NET 10 backend for a travel-planning application. The project aims to bring journeys, accommodation stays and attraction visits together in one itinerary, with trip history and a map view.

**Accommodations now runs independently on port 5106 with PostgreSQL persistence and private/shared access.** Attractions runs on port 5104 and Transport entries/Route Points on port 5105; all three hosts provide Development Swagger and own separate PostgreSQL stores. Full itinerary planning and the traveller-facing interface remain under development.

## Current capabilities

| Area | Current functionality |
| --- | --- |
| Attractions | Create, list (with case-insensitive name search), retrieve and partially update attractions with a name, address, coordinates, estimated visit duration, optional description and optional rating. |
| Accommodations | Create private or administrator-shared lodging records, search visible records by name and retrieve visible details. |
| Transport entries | Create, list and retrieve named transport entries using `car`, `bus`, `train`, `pedestrian`, `bike` or `plane`. |
| Route points | Create, list and retrieve reusable locations of type `accommodation`, `attraction` or `transport_stop`. |
| Validation | Validate required fields, supported codes and types, coordinate ranges, ratings and visit duration. |
| API authentication | Validate bearer tokens and require separate catalogue read/write permissions. Health remains anonymous. |
| API exploration | Use each independent service's Development Swagger page to inspect and execute its endpoints. |
| Health | Check the API’s status and service identity through `/health`. |

New records receive public UUIDv7 identifiers. The identity system separates public identifiers from internal numeric keys.

Accommodations, Attractions, Users, Transport entries and Route Points survive restarts in their respective service-owned PostgreSQL databases. Protected catalogue requests need a validated access token and a configured live Users access endpoint. See the [Accommodations](docs/accommodations-service.md), [Attractions](docs/attractions-service.md) and [Transport](docs/transport-service.md) guides. Automated application tests use isolated EF InMemory stores, signing keys and Users HTTP stubs; relational tests use disposable PostgreSQL databases.

## Project status

TravelTracker currently provides the catalogue and location foundation for future trip planning.

The following features are not yet available through the running application:

- Account status management and a frontend profile editor. The independent Users service supports first-sign-in provisioning, self-profile reads and updates over HTTP, live standing, verified first-administrator setup and administrator role management. See [Users contracts](docs/api.md#users-contracts).
- Accommodation update/archive management and frontend lodging screens.
- Trips, ordered itinerary legs, history and map views. Settings preferences are available through the independent Settings service ([docs/settings-service.md](docs/settings-service.md)).
- Scheduled journeys, departure and arrival times, and stop timing.
- PATCH and DELETE endpoints for transport entries and route points, and DELETE endpoints for every resource.
- Persistent storage and endpoints for Trips.
- Complete traveller-facing feature screens (the Blazor navigation and HTTP foundation is in `src/TravelTracker.Frontend`).

The historical shared user and `Accomodation` models remain for compatibility. The independent Accommodations service owns its own model, PostgreSQL store and HTTP endpoints. Trip/TripLeg domain rules are implemented, but their HTTP/persistence features remain unfinished. The independent Settings service implements `UserSettings`; the shared placeholder and legacy settings interfaces were removed. TripMapPoint remains a placeholder.

A route point describes a reusable location. A transport entry describes a transport option. Neither currently represents a scheduled journey.

## Technology and structure

- Auth0 for hosted sign-in and logout, using the ASP.NET Core OpenID Connect handler with authorization-code flow and PKCE in the Blazor server. The APIs validate bearer access tokens. See [Auth0 setup and verification](docs/frontend-auth0.md).
- .NET 10 and ASP.NET Core.
- Swagger/OpenAPI through Swashbuckle.
- Independent PostgreSQL persistence for Accommodations, Attractions, Users and Transport/Route Points.
- PostgreSQL through EF Core/Npgsql (owner decision, 2026-10-28), implemented for Accommodations, Attractions, Users, Transport and Settings; Trips persistence remains future work.
- xUnit and ASP.NET Core test hosts.
- GitHub Actions for pull-request testing.

| Project | Responsibility |
| --- | --- |
| `src/TravelTracker.Accommodations.Api` | Independent lodging HTTP contracts, application rules and live Users access checks on port 5106. |
| `src/TravelTracker.Accommodations.Data` | PostgreSQL context, migrations and visibility-scoped repository. |
| `src/TravelTracker.Accommodations.Domain` | Accommodation model and location/ownership invariants without web/EF dependencies. |
| `src/TravelTracker.Attractions.Api` | Independent attraction HTTP contracts, application rules and live Users access checks. |
| `src/TravelTracker.Attractions.Data` | PostgreSQL context, migrations, repositories and application-issued identities. |
| `src/TravelTracker.Attractions.Domain` | Attraction model, identifiers and invariants without web/EF dependencies. |
| `src/TravelTracker.Security` | Shared bearer validation and OpenAPI security infrastructure. |
| `src/TravelTracker.Settings.Api` | Independent own-settings HTTP contract, live Users standing checks and startup on port 5107. |
| `src/TravelTracker.Settings.Data` | Settings-owned PostgreSQL context, migrations and per-account repository. |
| `src/TravelTracker.Settings.Domain` | Preference model, defaults and validation rules without web/EF dependencies. |
| `src/TravelTracker.Users.Api` | Independent HTTPS account provisioning, bootstrap, administrator role management and service access contracts. |
| `src/TravelTracker.Users.Data` | Service-owned PostgreSQL accounts, migrations, transactional role changes and administrator audit. |
| `src/TravelTracker.Users.Domain` | Account identity, role and standing model without web/EF dependencies. |
| `src/TravelTracker.Transport.Api` | Independent Transport/Route Points HTTP operations and startup on 5105. |
| `src/TravelTracker.Transport.Data` | Transport-owned PostgreSQL context, migrations and repositories. |
| `src/TravelTracker.Transport.Domain` | Transport entities and identity types. |
| `src/TravelTracker.Api` | Unimplemented service interfaces (library, no web host). |
| `src/TravelTracker.Data` | Repositories, sample data and identity generation. |
| `src/TravelTracker.Domain` | Shared models and identifier types. |
| `tests/TravelTracker.Api.Tests` | Shared trip-domain and historical identity tests. |
| `tests/TravelTracker.Accommodations.Domain.Tests` | Accommodation model validation and ownership invariants. |
| `tests/TravelTracker.Accommodations.Data.Tests` | PostgreSQL migration, visibility queries and persistence tests. |
| `tests/TravelTracker.Accommodations.Api.Tests` | Accommodation HTTP contracts, bearer scope and privacy tests. |
| `tests/TravelTracker.Settings.Domain.Tests` | Settings defaults and preference validation rules. |
| `tests/TravelTracker.Settings.Data.Tests` | PostgreSQL settings migration, per-account persistence and constraint tests. |
| `tests/TravelTracker.Settings.Api.Tests` | Settings HTTP contract, scope matrix, signed bearer, ownership, standing and PostgreSQL restart tests. |
| `tests/TravelTracker.Transport.Api.Tests` | Independent Transport HTTP, signed bearer, selection and PostgreSQL tests. |
| `tests/TravelTracker.Attractions.Api.Tests` | Attraction HTTP, PostgreSQL durability/privacy and real-bearer integration tests. |
| `tests/TravelTracker.Users.Tests` | Account domain, real-bearer HTTP and PostgreSQL bootstrap/role/audit tests. |

The historical SQL schema in `src/Database/schema.sql` is not applied automatically. Accommodations, Attractions, Transport, Users and Settings use their committed EF migrations and separate service-owned databases.

## Getting started

The independent Blazor Web App lives in [src/TravelTracker.Frontend](src/TravelTracker.Frontend). Run `dotnet run --project src/TravelTracker.Frontend --launch-profile http` and open http://localhost:5100. It runs in interactive Server mode and calls the services over HTTP from the server, so no browser CORS or proxy setup is needed; service URLs live under `Services` in its `appsettings.json` (Attractions 5104, Transport 5105, Settings 5107). It provides the Figma-based shell, shared form/status displays and public service health checks; Auth0 sign-in and a protected Transport read are on the account page once the `Auth0` settings are provided (see [Auth0 setup](docs/frontend-auth0.md)); trip saving remains a separate feature. Its tests are in `tests/TravelTracker.Frontend.Tests`.

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
| Accommodations | GET | `/api/v1/accommodations` (optional `name` filter) |
| Accommodations | GET | `/api/v1/accommodations/{id}` |
| Accommodations | POST | `/api/v1/accommodations` |

In Swagger, select **Authorize** and enter an access token for this API. Reads require `catalogue.read`; writes require `catalogue.write`. Then expand an endpoint, select **Try it out**, enter the required values and select **Execute**. `/health` is anonymous. Missing or invalid tokens return `401`; missing permissions return `403`.

Successful creation returns `201 Created`, the saved record and a `Location` header. Use the returned identifier to retrieve the record. Unknown identifiers return `404 Not Found`; invalid request input returns `400 Bad Request`.

See the [API documentation](docs/api.md) and [HTTP examples](src/TravelTracker.Transport.Api/requests.http) for request bodies and response details.

Accommodations runs at `http://localhost:5106` with its own Swagger page. Its public `/health` endpoint needs no token. Protected reads require `catalogue.read`; creation requires `catalogue.write` and a configured live Users check. See [Accommodations startup and access rules](docs/accommodations-service.md).

## Testing

Run application, domain and HTTP tests from the repository root (no PostgreSQL needed):

```powershell
dotnet build TravelTracker.Tests.slnx --configuration Release
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --filter "Category!=PostgreSQL"
```

CI runs these commands plus the PostgreSQL group. Warnings fail the build (`Directory.Build.props`), and a boundary test fails if Domain gains references or Data becomes a web project. CI uploads the `.trx` results as a `test-results` artifact even when tests fail.

The suite covers health, accommodation/attraction/transport operations, route points, validation, concurrent identity generation, project dependency boundaries, and real bearer-token validation and scope policies. Catalogue tests cover private/shared visibility and literal name search. Tests configure isolated signing keys and require no external identity provider. Application/domain/HTTP tests use EF InMemory and need no PostgreSQL. Relational tests marked `Category=PostgreSQL` require real PostgreSQL for constraints, migrations, locking, rollback and restart.

Contracts for the planned multi-service architecture are in [milestone 3 contracts](docs/milestone-3-contracts.md).

Tests create isolated application hosts automatically. PostgreSQL suites require `TRAVELTRACKER_TEST_POSTGRES` with permission to create disposable test databases; see the service guide. Run `--filter "Category=PostgreSQL"` for that group or omit the filter for the complete gate. GitHub Actions supplies PostgreSQL and runs both groups.

## Documentation

- [Auth0 sign-in, application registration and verification](docs/frontend-auth0.md)
- [Product design and roadmap](docs/design.md)
- [API contracts](docs/api.md)
- [Accommodations startup, privacy and PostgreSQL tests](docs/accommodations-service.md)
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

## Attraction workspace (#43)

Open `/attractions` in the Blazor frontend. Search visible attractions by name, view saved details, create private attractions and edit permitted records using the independent secured service. Results retain the server's name ordering and display as two cards per row on wide screens.

Configure the frontend's Auth0 application and API grant with `catalogue.read catalogue.write` for write-capable users, and set `Auth0:Scope` to `openid profile email catalogue.read catalogue.write`. Sign in again after changing grants. Read-only sessions can browse without a write form; the API still enforces live account standing, ownership and write permissions.

**Choose on map** opens the supplied Montreal screenshot as a static preview with editable latitude/longitude. Use applies a validated coordinate draft; Cancel leaves the form location intact. Live OpenStreetMap tiles, address lookup and pin placement are future integration work. The optional visit duration ± field calculates a planning range but is explicitly not saved by the existing API.

See [implementation and verification](docs/issue-43-implementation.md).

## First administrator setup

The independent Users service now supplies the one-time, verified-identity administrator setup, current account profile and live Attractions access lookup. See [first administrator setup](docs/first-administrator-setup.md) for configuration, HTTPS, PostgreSQL, verification and scope limits. The API also supports editing the signed-in account profile. Ordinary sign-in provisions an active traveller with blank profile fields.

## Administrator user management

An active Users-service administrator can open **Administrator Panel → All users** from the burger menu or account page. The list shows registered TravelTracker accounts, their email, role and standing. Promote/demote requires an explicit confirmation and a written audit reason; the server refuses stale requests, inactive promotions and removal of the last active administrator. Changes are persisted with an actor/target/before/after/reason audit in the same PostgreSQL transaction. These application roles do not grant Auth0 catalogue scopes or access to private trips.

First authenticated profile access verifies an unregistered sign-in through the configured provider and creates an ordinary traveller account. Token role claims and request fields cannot select the initial role. Auth0 directory accounts that have never registered with TravelTracker do not appear automatically.

For Transport startup, set `ConnectionStrings__Transport`, apply migrations and optionally enable stable examples before running the service. See [Transport startup and selection](docs/transport-service.md).

## Transport and destination selection (#44)

Open **New Trip** (`/new-trip`) and sign in with `catalogue.read`. Choose a saved transport directly from its card; no transport search or filter step is required. Selection opens the location map, where **Departure** and **Destination** choose the two saved endpoints. Accommodation, attraction and transport-stop locations can also be selected from a list or filtered by name/type. Add ordered choices to the draft transport chain; the next entry can start from the preceding destination. Its green panels, pale-red selection and circle/line tracker follow the selected Version 1 design. The map reuses the Attractions screenshot with interactive saved markers. Selected endpoints remain visible through filtering and view changes. Marker placement is illustrative; the list remains available for overlapping markers. There are no live street tiles or address lookup.

Sessions granted `catalogue.write` can create transport entries and destinations. New locations require valid coordinates and a confirmed server-issued public ID before selection. Pending inputs remain available after rejected or failed saves. A separate selected-route summary contains optional travel and change-after-this-transport duration fields in minutes. These are traveller-entered planning estimates retained in the draft and shown in the tracker, not catalogue schedules or calculated route estimates. The existing trip form remains a placeholder: the selection callback retains public catalogue records and entered durations in the page draft, without saving a trip. See [implementation and verification](docs/issue-44-implementation.md) and the [implementation plan](docs/issue-44-plan.md).
