# Disabled Supabase Data API retry errors

## Diagnosis

On 2026-11-23, the TravelTracker Supabase project produced approximately 2,693
PostgREST schema-cache failures in the preceding 24 hours, retrying roughly every
32 seconds. The missing schema was `pg_pgrst_no_exposed_schemas`, Supabase's
placeholder when the Data API is disabled. Readiness requests to
`/rest-admin/v1/ready` returned 503. Recorded Auth token and signing-key requests
returned 200; this evidence does not establish the meaning or time window of the
reported dashboard **0.9% success rate**.

TravelTracker uses its own authenticated .NET APIs and PostgreSQL connections.
It uses Supabase Auth, but does not need Supabase's auto-generated Data API.
Supabase documents that disabling the Data API can leave PostgREST running and
repeatedly reporting this missing schema. See the
[official issue and workaround](https://supabase.com/kb/troubleshooting/schema-pg_pgrst_no_exposed_schemas-does-not-exist)
and [Data API security guidance](https://supabase.com/docs/guides/api/securing-your-api).

## Applied workaround

The hosted `postgres` database received migration
`20261123231049_quiet_disabled_data_api_schema_cache` on 2026-11-23 at
approximately 23:10 UTC.
The corresponding SQL is recorded in
[supabase-disabled-data-api.sql](../src/Database/supabase-disabled-data-api.sql).
It creates an empty `pgrst_no_exposed_schemas` schema, explicitly removes schema
access for public API roles, points PostgREST's `authenticator` role at that schema,
and requests configuration and schema-cache reloads. Do not automatically run this
script at application startup or against the six service databases.

No application tables, user records, Auth permissions, or service database
credentials were changed. Never add application objects to the empty schema.
Keep the Data API disabled. Enabling `public` to hide the error would expand the
API surface unnecessarily.

## Verification and rollback

Verify the following after applying the workaround:

- `pg_db_role_setting` for `authenticator` contains
  `pgrst.db_schemas=pgrst_no_exposed_schemas`.
- The schema has zero relations and zero routines, and `anon`, `authenticated`
  and `service_role` have no schema USAGE privilege.
- PostgREST reports successful schema-cache loading, and no further
  missing-schema/reconnect cycle occurs across several former 32-second retry
  intervals. Query a fresh time window; old errors remain in retained logs.
- Supabase `/auth/v1/health` still returns 200. A request to `/rest/v1/` with
  the configured publishable key remains rejected (401 in the live check).
  A rejected REST request alone does not prove the cache is healthy.

The role setting and empty-schema checks passed immediately after the migration;
the live Auth health and REST rejection checks also passed. At 23:11:14 UTC,
PostgREST successfully loaded its schema cache with zero relations,
relationships and RPCs. The health advisor reported no findings. The security
advisor still reported the pre-existing disabled leaked-password-protection
warning; that separate Auth policy was not changed by this repair.
The fresh log query at 23:13:11 UTC found no further PostgREST entries after
23:11:15 UTC (and thus no missing-schema or reconnect entries in that window).
The post-fix edge logs also recorded `/rest-admin/v1/ready` returning 200,
compared with the previous 503 responses. This is a short observation window,
subject to log ingestion delay, rather than a long-term availability guarantee.
The owner subsequently reported the project overview's success rate rising to
2.7%; its exact aggregation window was not independently verified.

Historical dashboard percentages will not reset when a fix is applied. Compare
the same metric over a fresh post-fix window and identify its service, numerator,
denominator and status-code breakdown before interpreting it as application
success. This change does not promise a particular dashboard percentage.

The SQL file includes the role-reset and reload commands for rollback before a
future intentional Data API enablement. Audit exposed schemas, grants and RLS
before enabling that API. Leave the empty schema in place unless a separate
cleanup verifies that no configuration still references it.
