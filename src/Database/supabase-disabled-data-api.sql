-- Run once in the managed Supabase postgres database, not the six service databases.
-- Prerequisite: the Data API is intentionally disabled; this is not an app migration.
-- https://supabase.com/kb/troubleshooting/schema-pg_pgrst_no_exposed_schemas-does-not-exist
-- Keep this schema empty: never put application tables, views or functions here.
CREATE SCHEMA pgrst_no_exposed_schemas;
REVOKE ALL ON SCHEMA pgrst_no_exposed_schemas FROM PUBLIC, anon, authenticated, service_role;
ALTER ROLE authenticator SET pgrst.db_schemas = 'pgrst_no_exposed_schemas';
NOTIFY pgrst, 'reload config';
NOTIFY pgrst, 'reload schema';

-- Before deliberately re-enabling the Data API, undo the role override:
-- ALTER ROLE authenticator RESET pgrst.db_schemas;
-- NOTIFY pgrst, 'reload config';
-- NOTIFY pgrst, 'reload schema';
-- Review exposed schemas, grants and RLS before enabling the Data API.
