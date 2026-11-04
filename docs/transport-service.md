# Independent Transport service (#36)

`TravelTracker.Transport.Api` owns Transport entries and Route Points on port 5105. Api references only its own Data/Domain and shared Security; Data references its own Domain and EF/Npgsql. No other service host or database is required. `TravelTracker.Api` is now a library of remaining unimplemented service interfaces, with no runnable catalogue endpoints. Shared Trip domain snapshots remain in the historical Domain project; they are not persisted Transport entities.

## Startup and migrations

Supply `ConnectionStrings__Transport` through local environment configuration or user secrets, pointing at a dedicated PostgreSQL database. Supply `Authentication__Bearer__Authority`, `Issuer`, and `Audience` for the chosen provider as described in [authentication](authentication.md). Never commit credentials.

Set `Database__ApplyMigrations=true` to apply the committed Transport migrations at startup. Alternatively use `dotnet ef database update --project src/TravelTracker.Transport.Data` with the connection configured. `Database__SeedExamples=true` optionally inserts the original six mode examples (including bike) and three location examples into empty resource tables. Stable sample UUIDs are preserved. Initialization never clears saved data; seed operations are transactional. Only one service instance may initialize/issue identities at a time.

Run `dotnet run --project src/TravelTracker.Transport.Api --launch-profile http`. `/health` is anonymous and returns service `TravelTracker.Transport`; Development Swagger is available at `/swagger`. Health reports host liveness, not database readiness. The Blazor client's Transport URL remains `http://localhost:5105`; its existing saved sample lookup stays compatible when examples are enabled. Other hosts can remain stopped.

## HTTP selection and security

Existing POST and GET-by-ID routes and camelCase DTOs remain unchanged. New collection routes return saved records as JSON arrays:

- `GET /api/v1/transport-modes?code=bike&name=bike`: optional exact lowercase supported mode and trimmed case-insensitive name substring.
- `GET /api/v1/route-points?type=transport_stop&name=station`: optional exact lowercase location type and the same name filtering.

Omitted filters return every saved record; no matches return `200 []`. Order is ordinal case-insensitive name, then public UUID. Repeated filter parameters or unsupported nonempty code/type return 400. All detail/collection GET operations use named `CatalogueRead` (`catalogue.read`); POST uses `CatalogueWrite` (`catalogue.write`). Missing/invalid bearer tokens return 401, insufficient scopes 403. Existing create validation rejects unsupported code/type, blank names and out-of-range coordinates before storage. Unknown write fields retain the previous framework behavior (ignored). SourceId remains unverified GUID metadata with no cross-service lookup or foreign key. Transport entries and locations carry no schedule, departure/arrival or boarding times. Account-standing integration is separate Users scope; no new identity/ownership policy is inferred here.

Internal bigint keys are application-issued, never exposed in HTTP, and rebuilt above the largest key in both tables after restart. Public UUIDs remain stable. This allocator supports one Transport process; multiple writers require a separate node-aware allocation change. PostgreSQL checks enforce supported codes/types, nonblank names and coordinate ranges; public UUIDs are unique per resource. Storage conflicts return 409, known constraint failures 400, transient outages 503, unexpected storage errors 500, without provider details.

## Verification

`tests/TravelTracker.Transport.Api.Tests` owns moved HTTP/bearer tests and new selection/persistence tests. Application tests replace only storage with isolated EF InMemory; production has no in-memory provider switch. Signed RSA JWTs go through the production bearer handler and discovery stub. Tests cover issuer/audience/signature/expiry and correct/incorrect scopes; anonymous health remains explicit.

Run `dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"`. For migrations, constraints, failed-write emptiness and host restart persistence, set `TRAVELTRACKER_TEST_POSTGRES` to an isolated PostgreSQL instance with CREATEDB, then run with `--filter "Category=PostgreSQL"`. Each relational test creates and drops only its randomly named disposable database. PostgreSQL tests fail when configuration is absent; they never fall back to InMemory.

Frontend Transport selection UI is tracked by #44; this issue preserves the client URL/detail DTO but does not claim that future journey complete.
