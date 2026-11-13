-- Operator-only, one explicitly verified old/new identity pair per invocation.
-- Stop ALL application processes first. Back up all five affected databases.
-- Run against attractions, accommodations, settings, trips, then users, while stopped.
-- Defaults to preview. -v apply=true executes the updates in this database only.
-- See docs/supabase-auth.md. Never infer an identity mapping from an email match.
\set ON_ERROR_STOP on
\if :{?apply}
\else
\set apply false
\endif
\if :{?old_issuer}
\else
\echo 'Required: -v old_issuer=... -v old_subject=... -v new_issuer=... -v new_subject=...'
\quit 2
\endif
\if :{?old_subject}
\else
\quit 2
\endif
\if :{?new_issuer}
\else
\quit 2
\endif
\if :{?new_subject}
\else
\quit 2
\endif

BEGIN;
SELECT set_config('traveltracker.old_issuer', :'old_issuer', true) AS old_issuer,
       set_config('traveltracker.old_subject', :'old_subject', true) AS old_subject,
       set_config('traveltracker.new_issuer', :'new_issuer', true) AS new_issuer,
       set_config('traveltracker.new_subject', :'new_subject', true) AS new_subject,
       set_config('traveltracker.apply', :'apply', true) AS apply
\gset migration_

DO $$
DECLARE
    old_issuer text := current_setting('traveltracker.old_issuer');
    old_subject text := current_setting('traveltracker.old_subject');
    new_issuer text := current_setting('traveltracker.new_issuer');
    new_subject text := current_setting('traveltracker.new_subject');
    table_name text;
    issuer_column text;
    subject_column text;
    old_count bigint;
    new_count bigint;
BEGIN
    new_subject := new_subject::uuid::text;
    IF length(btrim(old_issuer)) = 0 OR length(btrim(old_subject)) = 0
        OR new_issuer !~ '^https://[a-zA-Z0-9.-]+/auth/v1$'
        OR new_subject::uuid = '00000000-0000-0000-0000-000000000000'::uuid
        OR new_subject IS NULL OR (old_issuer = new_issuer AND old_subject = new_subject) THEN
        RAISE EXCEPTION 'Provide distinct, verified old/new identities and a trusted Supabase issuer.';
    END IF;
    table_name := CASE current_database()
        WHEN 'traveltracker_users' THEN 'UserAccounts'
        WHEN 'traveltracker_attractions' THEN 'Attractions'
        WHEN 'traveltracker_accommodations' THEN 'Accommodations'
        WHEN 'traveltracker_settings' THEN 'UserSettings'
        WHEN 'traveltracker_trips' THEN 'Trips'
        ELSE NULL END;
    IF table_name IS NULL THEN
        RAISE EXCEPTION 'Run only against a supported TravelTracker service database.';
    END IF;
    issuer_column := CASE WHEN table_name = 'UserAccounts' THEN 'Issuer' ELSE 'OwnerIssuer' END;
    subject_column := CASE WHEN table_name = 'UserAccounts' THEN 'Subject' ELSE 'OwnerSubject' END;
    -- Exclude concurrent writes during this database's transaction as a second line of defence.
    EXECUTE format('LOCK TABLE public.%I IN EXCLUSIVE MODE', table_name);
    EXECUTE format('SELECT count(*) FROM public.%I WHERE %I=$1 AND %I=$2',
        table_name, issuer_column, subject_column) INTO old_count USING old_issuer, old_subject;
    EXECUTE format('SELECT count(*) FROM public.%I WHERE %I=$1 AND %I=$2',
        table_name, issuer_column, subject_column) INTO new_count USING new_issuer, new_subject;
    IF old_count > 0 AND new_count > 0 THEN
        RAISE EXCEPTION 'Destination already owns records. Resolve this collision explicitly; accounts are never merged automatically.';
    END IF;
    IF table_name = 'UserAccounts' AND old_count = 0 AND new_count = 0 THEN
        RAISE EXCEPTION 'Neither identity has a Users account; check the mapping before proceeding.';
    END IF;
    RAISE NOTICE '%: source rows %, destination rows %', table_name, old_count, new_count;
    IF current_setting('traveltracker.apply')::boolean THEN
        EXECUTE format('UPDATE public.%I SET %I=$1, %I=$2 WHERE %I=$3 AND %I=$4',
            table_name, issuer_column, subject_column, issuer_column, subject_column)
            USING new_issuer, new_subject, old_issuer, old_subject;
        IF table_name = 'UserAccounts' THEN
            UPDATE public."AdministratorBootstrap" SET "Issuer"=new_issuer, "Subject"=new_subject
            WHERE "Issuer"=old_issuer AND "Subject"=old_subject;
            -- Public UserId, roles, standing, editable profile fields and audit IDs stay intact.
            UPDATE public."UserAccounts" SET "ConcurrencyToken"=gen_random_uuid()
            WHERE "Issuer"=new_issuer AND "Subject"=new_subject AND old_count > 0;
        END IF;
    END IF;
END $$;
COMMIT;
