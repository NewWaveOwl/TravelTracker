# Supabase PostgreSQL migration verification

## Repository audit

The database provider and identity provider are independent settings. This audit
starts from `d666a26`. The bootstrap SQL creates the six service databases and
revokes public database access. The owning services apply their committed EF
migrations through `Database:ApplyMigrations`. The launcher sets that flag only
with `--apply-migrations` and disables example seeding. The legacy
`src/Database/schema.sql` is not part of this procedure.

| Database | Port | Committed migrations in order |
| --- | ---: | --- |
| `traveltracker_users` | 5101 HTTPS | `20261104170259_InitialUsersBootstrap`, `20261104171950_AdministratorRoleAudit`, `20261105140414_UserProfilePersistence`, `20261105152600_SeparateVerifiedEmail`, `20261105175417_StatusConcurrencyToken`, `20261122215928_ProfilePicture` |
| `traveltracker_attractions` | 5104 | `20261028152611_InitialAttractions` |
| `traveltracker_transport` | 5105 | `20261104184119_InitialTransport` |
| `traveltracker_accommodations` | 5106 | `20261105185817_InitialAccommodations` |
| `traveltracker_settings` | 5107 | `20261106165131_InitialSettings` |
| `traveltracker_trips` | 5108 | `20261106194443_InitialTrips`, `20261109022113_TripRevision`, `20261109120000_TripRevisionCheck`, `20261123004432_TripStops` |

All six migration chains and model snapshots are present. A read-only database
catalog query on 2026-11-23 confirmed the six service database names exist; their
complete applied migration histories still need a per-database inventory. The
bootstrap SQL does not provision runtime grants. The launcher supplies the same configured
database role to every service, so its normal path can verify a limited stack
role, but cannot demonstrate one-role-per-service isolation. Hosts already
accept separate `ConnectionStrings:<Service>` values when run independently.

The source inventory above includes the profile-picture and ordered-stop
migrations as of 2026-11-23. During that day's error investigation, the project's
PostgreSQL logs reported `relation "UserPictures" does not exist` and
`relation "TripStops" does not exist`. Those failures establish missing tables,
not a verified migration history or completed repair. Follow the inventory steps
below and the [recovery commands](milestone-4/startup-error-recovery.md); do not
recreate the service databases. The Supabase SQL Editor/MCP connection to the
default `postgres` database is not an inventory of the six separate service
databases.

## Inventory and migration

1. In an authorized Supabase staging project, use the Session pooler on port
   5432 with the dashboard CA and hostname verification. Verify the target
   project's six database names before running any bootstrap or migration.
   Bootstrap only missing databases. Back up existing databases first.
2. Connect to each database with `psql -X` using `sslmode=verify-full` and the
   downloaded `sslrootcert`. Run the queries below and compare the complete
   history with the table. A missing history table in an existing nonempty
   database, unexpected migration IDs, or a gap in the sequence needs review
   before applying migrations. An up-to-date database needs no migration run.
3. For a reviewed pending migration, run the [maintainer launcher
   command](../README.md#initialize-a-new-supabase-project-maintainer) with the
   migration role in staging and capture the applied IDs. The databases migrate
   separately. If any host fails, compare all six histories again before retrying
   so the operator can tell which changes already committed.
4. Restart normally with the runtime role. Run the same inventory and confirm
   that startup and API smoke writes did not add migration history rows.

```sql
SELECT current_database();
SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";
```

## Full-stack smoke and restart

Start all six hosts and the frontend with the [Supabase dev
launcher](../README.md#run-the-full-app). Check each API's `/health` plus the
frontend page on port 5100. Health is liveness evidence. Use an authorized
staging traveller, the current [API contracts](api.md), and unique fixture
labels for each run. Capture the status codes and a local fixture manifest of
returned IDs and expected values. Keep tokens, credentials and personal data
out of evidence. Use a staging administrator for protected Transport writes.

| Service | Authorized verification |
| --- | --- |
| Users | `GET /api/v1/users/me` for initial provisioning, then `PATCH` username, contact email and address and reload. |
| Attractions | `POST /api/v1/attractions`, `GET` by ID, and `PATCH` an editable field. |
| Transport | Create a mode and route point through `POST /api/v1/transport-modes` and `POST /api/v1/route-points`, then read both by ID. These resources have no update route. |
| Accommodations | `POST /api/v1/accommodations` and `GET` by ID. There is no update route. |
| Settings | `GET /api/v1/settings`, then `PUT` with the returned `If-Match` validator and read the new settings. |
| Trips | Create a trip referencing the staged catalogue records, `GET /api/v1/trips/{id}`, then `PUT` with its current `If-Match` validator and read the new values. |

Stop the entire stack with Ctrl+C. Start normally without
`--apply-migrations`, obtain a fresh token for the same traveller, and repeat
all reads against the saved fixture manifest. Verify a second traveller cannot
read the private trip or private catalogue records and receives their own
profile and settings. Include one request without a bearer token to confirm
the protected route returns 401.

## Auth0 identity and ownership

Use the same validated Auth0 issuer and subject to verify an existing account
before and after any PostgreSQL-only move. The Supabase launcher selects Supabase
Auth, so use the existing [Auth0 compatibility
configuration](frontend-auth0.md) against the staged database connections for
this check. Compare the public account ID, role, standing, profile, settings,
private catalogue records and trips. Verify a different identity with a
matching email gains no access to those records.

If moving sign-in to Supabase Auth is separately approved, follow the
[identity cutover](supabase-auth.md#move-existing-users-without-losing-their-private-data).
Review the explicit issuer/subject mapping and preview
[`MigrateSupabaseIdentity.sql`](../scripts/MigrateSupabaseIdentity.sql) before
applying it. Transport has no private owner mapping. Keep the Auth0 identities
and rollback access until the new owner and cross-user checks pass. This audit
does not run a mapping or move any user data.

## TLS, runtime privileges and failure checks

- **TLS.** Start in staging with the correct CA and complete a database-backed
  request. Repeat using a valid but unrelated CA in a separate launcher config
  and require the same request to fail. Record the Npgsql certificate error
  category and confirm the request did not succeed. VerifyFull checks both the
  CA and hostname. See [Supabase SSL
  enforcement](https://supabase.com/docs/guides/platform/ssl-enforcement).
- **Least privilege.** Verify the actual runtime connection in each database.
  The login should be a non-owner with no superuser, database creation, role
  creation or replication privilege, no inherited owner membership, and no
  schema creation rights. Its effective grants should cover only the
  application's table and sequence operations and database connections it
  needs. Confirm normal API writes succeed under it. Check elevated permissions
  using catalog queries rather than attempting DDL on shared data.

```sql
SELECT rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls
FROM pg_roles WHERE rolname = current_user;

SELECT datname,
       pg_has_role(datdba, 'MEMBER') AS member_of_owner,
       has_database_privilege(oid, 'CONNECT') AS can_connect,
       has_database_privilege(oid, 'CREATE') AS can_create
FROM pg_database
WHERE datname IN ('traveltracker_users', 'traveltracker_attractions',
                  'traveltracker_transport', 'traveltracker_accommodations',
                  'traveltracker_settings', 'traveltracker_trips');

SELECT nspname,
       pg_has_role(nspowner, 'MEMBER') AS member_of_owner,
       has_schema_privilege(oid, 'CREATE') AS can_create
FROM pg_namespace WHERE nspname = 'public';

SELECT relname FROM pg_class
WHERE relnamespace = 'public'::regnamespace
  AND relkind IN ('r', 'p', 'S')
  AND pg_has_role(relowner, 'MEMBER');
```

- **Unavailable database.** In an isolated run, override one service's
  connection with an unreachable endpoint or bad credentials. Keep Users
  healthy when testing a downstream store. With migrations disabled, probe a
  protected database-backed read and write and record status, elapsed time and
  the sanitized problem response. Connectivity failures should fail closed,
  generally with 503. Record any differently classified errors as findings.
  With migrations enabled on an isolated host, verify it fails to start. After
  restoring the connection, check that the failed write did not persist and
  that normal reads and writes recover.

## Backup and rollback

Before any schema or identity operation on existing data, stop writers and
take coordinated logical dumps of all six databases using `pg_dump --format=custom`
over verified TLS. Retain role/grant definitions and the deployed revision
separately in protected storage. If identity cutover is in scope, also preserve
provider access and the operator-verified mapping using the cutover guide.
Rehearse restoration into six separate disposable databases with
`pg_restore --exit-on-error`. Reapply ownership and grants, compare migration history,
and run the same private-ownership and fixture reads using the prior application
revision. If rollback is required, stop writers, restore the coordinated set and
validate it before routing traffic back. Rolling back just one database can
leave trip references or owner identities out of sync. Treat EF downgrade as
a separate reviewed operation, particularly if a migration drops data.

## Evidence on this branch

| Check | Result |
| --- | --- |
| Migration inventory and startup wiring | Inspected the six service migration histories in source and all six EF startup paths. |
| Launcher local configuration | `--check-config` stopped at missing `PoolerHost`. No staged project settings or database credentials were available. |
| Launcher offline tests | `dotnet run --file scripts/TestLauncher.cs`: 21 passed, 0 failed. |
| Full solution restore | Blocked by unreachable NuGet feed and missing packages. Full build and test suites could not run. |
| Isolated PostgreSQL | Docker's Linux engine was unavailable. |
| Staging migration history, full-stack reads/writes, restart, Auth0 ownership, TLS, grants, outage, restore | Pending execution against an authorized staging target. |
