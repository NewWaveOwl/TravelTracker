# Independent Accommodations service (#38)

The Accommodations service owns its Domain, Data and Api projects and its PostgreSQL database. It runs on port **5106** and does not read the historical shared accommodation table or another service's database. Its current scope is create, name search and detail; update, archive and delete are not implemented.

## Start independently

Install the .NET 10 SDK and run PostgreSQL with a dedicated `traveltracker_accommodations` database. Configure the service through environment variables or local secret storage; do not commit connection strings containing credentials or service tokens.

```powershell
$env:ConnectionStrings__Accommodations = 'Host=localhost;Database=traveltracker_accommodations'
$env:Database__ApplyMigrations = 'true'
$env:Authentication__Bearer__Authority = 'https://your-provider.example/tenant'
$env:Authentication__Bearer__Issuer = 'https://your-provider.example/tenant'
$env:Authentication__Bearer__Audience = 'traveltracker-api'
$env:Users__InternalBaseUrl = 'https://localhost:5101'
$env:Users__ServiceAccessToken = '<backend access token with users.access>'
dotnet run --project src/TravelTracker.Accommodations.Api --launch-profile http
```

The `http` launch profile selects Development and binds `http://localhost:5106`; Swagger is available at `/swagger`. The anonymous `GET /health` returns `{"status":"ok","service":"Accommodations"}`. Health is a liveness response and does not test PostgreSQL or Users connectivity. `Database:ApplyMigrations` defaults to false; enabling it applies committed EF migrations at startup. For explicit migration tooling, set `ConnectionStrings__Accommodations` and run `dotnet ef database update --project src/TravelTracker.Accommodations.Data` with a compatible EF Core 10 `dotnet-ef` tool.

Protected routes need an access token valid for this API and the exact catalogue scope. Set the bearer authority, exact issuer and audience to the identity provider's values. Health and Development Swagger work without a provider connection; protected calls fail authentication until valid provider configuration and tokens are supplied.

## Users access boundary

Every protected Accommodations request checks the caller's validated `(iss, sub)` against the live Users service over HTTPS. Configure `Users:InternalBaseUrl` and `Users:ServiceAccessToken` as above. The backend token must carry `users.access`; its JWT subject must be in the Users API's `UsersAccess:AllowedServiceSubjects` allowlist. Keep `users.access` off the frontend client. The service posts the validated identity to `/internal/users/identity`, then reads `/internal/users/{userId}/access`; it does not read Users tables or trust role/status values from the caller's token. Users lookup failures or invalid configuration fail closed with `503`. Deactivated or unknown users receive `403`.

The lookup has a five-second timeout and disables redirects/cookies. The service does not cache account standing. A live Users tenant and service credential were not verified as part of local service implementation; configure and verify those deployment settings before deploying.

## Routes and visibility

| Method and path | Scope | Result |
| --- | --- | --- |
| `GET /health` | Anonymous | `200` liveness and service identity. |
| `POST /api/v1/accommodations` | `catalogue.write` | `201` created record and `Location`; validation/unknown fields `400`; missing scope `403`; shared create by non-admin `403`; Users outage `503`. |
| `GET /api/v1/accommodations?name=...` | `catalogue.read` | `200` visible records, optionally filtered by literal case-insensitive name substring; repeated `name` is `400`. |
| `GET /api/v1/accommodations/{id}` | `catalogue.read` | `200` visible detail; malformed GUID `400`; absent or another user's private record `404`. |

The response fields are `accommodationId`, `name`, `address`, `lat`, `lon` and `visibility`. POST accepts `name`, `address`, `lat`, `lon` and optional `visibility`. The server generates the UUIDv7 and gets owner identity, role and standing from trusted service-side sources; those fields and client-supplied IDs are not accepted. Name is trimmed and required. Latitude must be within -90..90 and longitude within -180..180. Omitted address defaults to an empty string; explicit null is invalid. Visibility defaults to `private`; only an active administrator may create `shared` records.

Active users can search/read shared records and their own private records. Banned users may read only their own private records and cannot create. Other users' private records are omitted from search and look like `404` in detail, including for administrators. A valid bearer without the route's required scope receives `403`; no/invalid bearer receives `401`. Scope checks happen before request validation. Every resource endpoint has an explicit policy; health remains public.

## Persistence and tests

The service uses only PostgreSQL/Npgsql in production. `Database:ApplyMigrations` applies the service's initial migration; it does not run `EnsureCreated` or seed sample records. Use a separate database and connection for this service.

Domain and API behavior tests are in `tests/TravelTracker.Accommodations.Domain.Tests` and `tests/TravelTracker.Accommodations.Api.Tests`. Relational tests are marked `Category=PostgreSQL` in `tests/TravelTracker.Accommodations.Data.Tests` and require `TRAVELTRACKER_TEST_POSTGRES` with permission to create and drop disposable test databases. EF InMemory tests do not prove migration, constraint or restart behavior.

See the [HTTP contract](api.md#accommodation-fields-and-access), [Domain model](domain.md) and [Data layer](data.md) for layer details.
