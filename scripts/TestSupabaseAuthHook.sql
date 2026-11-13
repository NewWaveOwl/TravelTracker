-- Only for a NEW disposable test database; see test-supabase-auth-hook.sh.
-- Fixtures intentionally model only the Supabase Auth columns this hook reads.
\set ON_ERROR_STOP on
BEGIN;
DO $$ BEGIN
    IF to_regnamespace('auth') IS NOT NULL THEN
        RAISE EXCEPTION 'Refusing an existing Auth schema; use a fresh disposable database.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='supabase_auth_admin') THEN CREATE ROLE supabase_auth_admin; END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='anon') THEN CREATE ROLE anon; END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='authenticated') THEN CREATE ROLE authenticated; END IF;
END $$;
CREATE SCHEMA auth;
CREATE TABLE auth.users(id uuid PRIMARY KEY, raw_app_meta_data jsonb);
GRANT USAGE ON SCHEMA auth TO supabase_auth_admin;
GRANT SELECT ON auth.users TO supabase_auth_admin;
INSERT INTO auth.users VALUES
 ('11111111-1111-1111-1111-111111111111','{"provider":"email"}'),
 ('22222222-2222-2222-2222-222222222222','{"traveltracker_service":true}'),
 ('33333333-3333-3333-3333-333333333333','{"traveltracker_scopes":["trips.read","trips.read","users.access","invented"]}'),
 ('44444444-4444-4444-4444-444444444444','{"traveltracker_scopes":[]}'),
 ('55555555-5555-5555-5555-555555555555','{"traveltracker_scopes":"users.access"}');
\ir ../src/Database/supabase-auth-hook.sql

DO $$
DECLARE
 event jsonb := '{"user_id":"11111111-1111-1111-1111-111111111111","claims":{"role":"authenticated","is_anonymous":false,"scope":"users.access","user_metadata":{"traveltracker_service":true,"scope":"users.access"}}}';
 result jsonb;
 scopes text;
BEGIN
 result := public.traveltracker_access_token_hook(event);
 scopes := result->'claims'->>'scope';
 IF scopes LIKE '%users.access%' OR scopes NOT LIKE '%trips.write%'
    OR result->'claims'->'user_metadata' IS DISTINCT FROM event->'claims'->'user_metadata' THEN
   RAISE EXCEPTION 'Traveller defaults or untrusted metadata boundary failed';
 END IF;
 result := public.traveltracker_access_token_hook(jsonb_set(event,'{user_id}','"22222222-2222-2222-2222-222222222222"'));
 IF result->'claims'->>'scope' IS DISTINCT FROM 'users.access' THEN RAISE EXCEPTION 'Service permission isolation failed'; END IF;
 result := public.traveltracker_access_token_hook(jsonb_set(event,'{user_id}','"33333333-3333-3333-3333-333333333333"'));
 IF result->'claims'->>'scope' IS DISTINCT FROM 'trips.read' THEN RAISE EXCEPTION 'Permission allowlist/deduplication failed'; END IF;
 result := public.traveltracker_access_token_hook(jsonb_set(event,'{user_id}','"44444444-4444-4444-4444-444444444444"'));
 IF result->'claims'->>'scope' IS DISTINCT FROM '' THEN RAISE EXCEPTION 'Empty restriction gained permissions'; END IF;
 result := public.traveltracker_access_token_hook(jsonb_set(event,'{user_id}','"55555555-5555-5555-5555-555555555555"'));
 IF result->'claims'->>'scope' IS DISTINCT FROM '' THEN RAISE EXCEPTION 'Malformed restriction gained permissions'; END IF;
 result := public.traveltracker_access_token_hook(jsonb_set(event,'{claims,is_anonymous}','true'));
 IF result->'claims'->>'scope' IS DISTINCT FROM '' THEN RAISE EXCEPTION 'Anonymous identity gained permissions'; END IF;
 result := public.traveltracker_access_token_hook(jsonb_set(event,'{claims,role}','"service_role"'));
 IF result->'claims'->>'scope' IS DISTINCT FROM '' THEN RAISE EXCEPTION 'Privileged API role gained traveller permissions'; END IF;
 IF has_function_privilege('anon','public.traveltracker_access_token_hook(jsonb)','EXECUTE')
    OR has_function_privilege('authenticated','public.traveltracker_access_token_hook(jsonb)','EXECUTE')
    OR NOT has_function_privilege('supabase_auth_admin','public.traveltracker_access_token_hook(jsonb)','EXECUTE') THEN
    RAISE EXCEPTION 'Hook execution grants are incorrect';
 END IF;
 RAISE NOTICE 'PASS: 8 Supabase Auth hook permission checks';
END $$;
SET LOCAL ROLE supabase_auth_admin;
SELECT public.traveltracker_access_token_hook('{"user_id":"11111111-1111-1111-1111-111111111111","claims":{"role":"authenticated"}}') IS NOT NULL AS hook_role_can_execute;
ROLLBACK;
