# Independent Settings service (#42)

The Settings service owns its Domain, Data and Api projects and its PostgreSQL database. It runs on port **5107**, does not read another service's database, and stores one preference row per account, keyed by the validated `(iss, sub)` pair. It implements the caller's own preferences only: theme, default map view, preferred transport mode and time zone.

## Start independently

Install the .NET 10 SDK and run PostgreSQL with a dedicated `traveltracker_settings` database. Configure the service through environment variables or local secret storage; do not commit connection strings with credentials or service tokens.

```powershell
$env:ConnectionStrings__Settings = 'Host=localhost;Database=traveltracker_settings'
$env:Database__ApplyMigrations = 'true'
$env:Authentication__Bearer__Authority = 'https://your-provider.example/tenant'
$env:Authentication__Bearer__Issuer = 'https://your-provider.example/tenant'
$env:Authentication__Bearer__Audience = 'traveltracker-api'
$env:Users__InternalBaseUrl = 'https://localhost:5101'
$env:Users__ServiceAccessToken = '<backend access token with users.access>'
dotnet run --project src/TravelTracker.Settings.Api --launch-profile http
```

The `http` launch profile selects Development and binds `http://localhost:5107`; Swagger is at `/swagger`. `GET /health` is anonymous and returns `{"status":"ok","service":"Settings"}`; it does not test PostgreSQL or Users connectivity. The host starts without Users or a database: Users is contacted only per protected request, and a missing or unreachable Users configuration fails protected requests closed with `503`.

The built binary can also be started directly, from any working directory: `dotnet <output>/TravelTracker.Settings.Api.dll`. The host sets its content root to the application folder (`ApiHostBuilder` in `TravelTracker.Security`), so `appsettings.json` and `appsettings.Development.json` load regardless of the current directory. Development settings contain placeholder identity values only, so protected calls return `401` until real provider values and tokens are supplied.

`Database:ApplyMigrations` defaults to false. When enabled, the host applies the committed EF migration at startup. For explicit migration tooling run `dotnet ef database update --project src/TravelTracker.Settings.Data` with `ConnectionStrings__Settings` set.

The Users service must accept the backend service token (see [Accommodations Users boundary](accommodations-service.md#users-access-boundary), which uses the same contract).

## Routes

| Method and path | Scope | Result |
| --- | --- | --- |
| `GET /health` | Anonymous | `200` liveness and service identity. |
| `GET /api/v1/settings` | `settings.read` | `200` the caller's saved preferences, or the defaults before the first save, with `ETag`. Nothing is written. |
| `PUT /api/v1/settings` | `settings.write` | `200` the saved preferences with the new `ETag`; `400` field errors; `412` stale precondition; `428` missing precondition. |

There is no user identifier in any route or body. Ownership comes only from the validated bearer token. Unknown JSON members, including `userId`, `subject`, `ownerSubject` and `ownerIssuer`, are rejected with `400`. Paths other than the two above, including `/api/v1/settings/{id}`, match no route and return `404` for every caller.

Every protected request is authenticated (`401` for a missing or invalid bearer token), then checked for the exact scope (`403` before body validation), then checked against live Users standing:

- deactivated or unknown accounts receive `403` for reads and writes;
- banned accounts may read their own preferences and receive `403` for writes;
- a Users outage, timeout or invalid response receives `503` and nothing is written.

Standing is never cached and is not read from token claims. A token with only `catalogue.*` scopes is refused for settings routes.

## ETag and If-Match

Every successful settings response carries a strong `ETag` that names the saved revision, for example `"3"`. A caller that has never saved sees the defaults with `"0"`; the first save advances the revision to `"1"`, and every accepted save adds one.

A replace must send the `ETag` it last read in `If-Match`. The server applies the request in this order:

1. The body shape is checked first: missing fields, wrong JSON types and unknown members return `400`.
2. A `PUT` without an `If-Match` header returns `428 Precondition Required` (RFC 6585). This is settled behaviour: a missing precondition is a different mistake from a failed one, so it is never answered with `412`. Nothing is saved.
3. The field values are validated. Invalid values return `400` with field errors, even when the `If-Match` is stale.
4. The `If-Match` must be a strong entity tag naming the stored revision. A stale revision, a weak tag (`W/"3"`), a bare number, `*`, or any other malformed value returns `412 Precondition Failed`. Nothing is saved and the stored settings are unchanged.

The revision check and the write are one guarded statement (the revision is a concurrency token), so two tabs that race with the same `ETag` cannot both succeed. The second save receives `412` and the client should reload and review. Missing-header and malformed-header behaviour is deliberate: `428` tells a client it forgot the precondition, while `412` tells it the precondition failed.

## Preference fields

| Field | Type | Default | Accepted values |
| --- | --- | --- | --- |
| `theme` | string | `system` | `system`, `light`, `dark` |
| `defaultMapLatitude` | decimal | `0` | -90 to 90 |
| `defaultMapLongitude` | decimal | `0` | -180 to 180 |
| `defaultMapZoom` | integer | `2` | 0 to 20 |
| `preferredTransportMode` | string | `car` | `car`, `bus`, `train`, `pedestrian`, `bike`, `plane` |
| `timeZone` | string | `UTC` | a time zone identifier known to the host (IANA name, for example `America/Montreal`) |

`PUT` replaces every field, so all six fields are required. Field errors use the JSON field name as the key in the problem details `errors` object. The response body does not contain the revision; it is carried only in the `ETag`.

## Persistence

The service uses only PostgreSQL through EF Core/Npgsql. The `UserSettings` table has a composite primary key on `OwnerIssuer` and `OwnerSubject`, a `Revision` column (`>= 1`, concurrency token), and database check constraints that mirror the theme, transport, coordinate, zoom and owner rules (`CK_UserSettings_*`). A first save inserts revision 1. A unique-key collision from two racing first saves is reported as stale (`412`). Later saves overwrite every column of the caller's row and advance its revision in one statement.

## Tests

- Domain rules: `tests/TravelTracker.Settings.Domain.Tests` (no database).
- HTTP contract, scope matrix, real bearer validation, ownership, standing, Users outage, ETag/If-Match ordering, unknown routes and the shared fallback: `tests/TravelTracker.Settings.Api.Tests` (EF InMemory, signed test tokens, stubbed Users boundary).
- Concurrent If-Match compare-and-set: `SettingsConcurrentWritePostgreSqlTests` (`Category=PostgreSQL`). Each of 20 iterations releases two writers behind a `Barrier` with the same `If-Match`, once for an update (`"1"`) and once for a first save (`"0"`), and asserts exactly one `200` and one `412` with the stored row equal to the winner's. The Users stub is lock-guarded because the race reaches it from two threads.
- PostgreSQL migration, constraints, per-account persistence, revision compare-and-set and restart durability: `tests/TravelTracker.Settings.Data.Tests` and the restart tests in `tests/TravelTracker.Settings.Api.Tests`, both marked `Category=PostgreSQL`. They require `TRAVELTRACKER_TEST_POSTGRES` with permission to create and drop disposable databases.

## Shared behaviour changes

These changes are in `TravelTracker.Security` and therefore apply to every API host:

- Requests that match no route now return `404` for anonymous callers and for callers with any scope. Before, the fallback catalogue policy answered `401` or `403` first. Endpoints that declare no policy are still guarded by the fallback (`catalogue.read`), and `/health` stays anonymous.
- Every API host builds its host with `ApiHostBuilder.Create(args)`, which anchors the content root to the application folder instead of the working directory.

## Not implemented

- Time-zone validation uses the host's time zone database; Windows-only identifiers are not translated to IANA names.
- No `If-Match: *` or weak-tag support; those are refused with `412` by design.

See the [API overview](api.md), [Domain model](domain.md) and [Milestone 3 contracts](milestone-3-contracts.md) for the wider context.
