# Supabase as the database

This page covers Supabase as the PostgreSQL server only. By default sign-in is handled by the Users API against its own PostgreSQL database (see [API authentication](authentication.md)), so Supabase is just a database here, exactly like a local container or any other host. The launcher's `--supabase` flag is a preset that builds the connection for you. To use Supabase for sign-in as well, see [Supabase Auth](supabase-auth.md); the two uses are independent.

## Connect

```sh
dotnet run --file scripts/StartDev.cs -- --supabase
```

The first run asks for, and saves in the Git-ignored `.traveltracker-launch.json`, only non-secret values:

| Setting | Where to find it |
| --- | --- |
| Pooler host or PostgreSQL URI | Supabase **Connect > Session pooler**, port **5432** (not the transaction pooler). URI input is hidden; any embedded password is discarded. |
| Database username | Same panel; include the `.project-reference` suffix. |
| Root certificate | The database SSL root certificate, as a path on this computer. Spaces and quotes are supported. |

The database password is asked for privately on every launch and never saved. Equivalent to any other server, you can skip the preset and set `TRAVELTRACKER_DATABASE` to a full Npgsql connection string; `--supabase` and `TRAVELTRACKER_DATABASE` cannot be combined.

```sh
dotnet run --file scripts/StartDev.cs -- --supabase --setup          # review and save settings
dotnet run --file scripts/StartDev.cs -- --supabase --check-config   # validate without prompts
```

Older launcher files that still hold Supabase Auth or Auth0 fields (`SupabaseUrl`, `PublishableKey`, `ServiceEmail`, ...) keep working: the launcher reports each obsolete field, ignores it and drops it the next time it saves.

## Initialize a new project (maintainer)

Create the six service databases with [the bootstrap SQL](../src/Database/bootstrap.sql), using the project's default `postgres` database and a role with `CREATEDB`:

```sh
psql -X -W "host=your-region.pooler.supabase.com port=5432 user=postgres.yourprojectref dbname=postgres sslmode=verify-full sslrootcert='/path/to/supabase-ca.crt'" -f src/Database/bootstrap.sql
```

Then run `dotnet run --file scripts/StartDev.cs -- --supabase --apply-migrations` with a migration-capable role. Teammates use their assigned runtime role afterwards.

## Switching between Local and Supabase sign-in

Accounts are keyed by the token's issuer and subject, so switching provider never carries accounts over. A traveller's old trips stay keyed to the previous account and are hidden from the new one, because a matching email or username never merges accounts. The identity migration script was removed with the earlier switch to built-in sign-in, so moving existing data between providers is a manual operator task.
