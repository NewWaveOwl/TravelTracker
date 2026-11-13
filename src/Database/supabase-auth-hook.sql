-- Run in the Supabase project's postgres database, then select this function
-- under Authentication > Hooks > Custom Access Token. This is not an EF migration.
-- Only administrator-owned app_metadata may restrict scopes or mark a service user.
CREATE OR REPLACE FUNCTION public.traveltracker_access_token_hook(event jsonb)
RETURNS jsonb
LANGUAGE plpgsql STABLE
SET search_path = ''
AS $$
DECLARE
    metadata jsonb;
    claims jsonb := event->'claims';
    granted text;
    traveller_scopes text[] := ARRAY[
        'catalogue.read', 'catalogue.write', 'users.read', 'users.write',
        'settings.read', 'settings.write', 'trips.read', 'trips.write'];
BEGIN
    SELECT raw_app_meta_data INTO metadata FROM auth.users
    WHERE id = (event->>'user_id')::uuid;

    IF metadata IS NULL OR claims->>'role' IS DISTINCT FROM 'authenticated'
        OR claims->'is_anonymous' = 'true'::jsonb THEN
        granted := '';
    ELSIF metadata->'traveltracker_service' = 'true'::jsonb THEN
        -- A machine identity receives no interactive traveller permissions.
        granted := 'users.access';
    ELSIF metadata ? 'traveltracker_scopes' THEN
        -- An explicit empty/malformed restriction must never become full access.
        IF jsonb_typeof(metadata->'traveltracker_scopes') = 'array' THEN
            SELECT COALESCE(string_agg(scope, ' ' ORDER BY scope), '') INTO granted
            FROM (SELECT DISTINCT value #>> '{}' AS scope
                  FROM jsonb_array_elements(metadata->'traveltracker_scopes')
                  WHERE jsonb_typeof(value) = 'string') allowed
            WHERE scope = ANY(traveller_scopes);
        ELSE
            granted := '';
        END IF;
    ELSE
        -- Ordinary signed-up travellers still pass each API's live Users standing checks.
        granted := array_to_string(traveller_scopes, ' ');
    END IF;

    RETURN jsonb_set(event, '{claims}', jsonb_set(claims, '{scope}', to_jsonb(granted)));
END;
$$;

REVOKE ALL ON FUNCTION public.traveltracker_access_token_hook(jsonb) FROM PUBLIC, anon, authenticated;
GRANT USAGE ON SCHEMA public TO supabase_auth_admin;
GRANT EXECUTE ON FUNCTION public.traveltracker_access_token_hook(jsonb) TO supabase_auth_admin;
-- supabase_auth_admin already owns/has access to the Supabase-managed auth.users table.
