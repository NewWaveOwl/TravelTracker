-- Run with psql against the Supabase project's postgres database as a role with CREATEDB.
-- psql executes each CREATE DATABASE separately, outside a transaction.
\set ON_ERROR_STOP on

SELECT format('CREATE DATABASE %I TEMPLATE template0', name)
FROM (VALUES ('traveltracker_users'), ('traveltracker_attractions'), ('traveltracker_transport'),
    ('traveltracker_accommodations'), ('traveltracker_settings'), ('traveltracker_trips')) AS services(name)
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = services.name)
\gexec

REVOKE CONNECT, TEMPORARY ON DATABASE traveltracker_users, traveltracker_attractions, traveltracker_transport,
    traveltracker_accommodations, traveltracker_settings, traveltracker_trips FROM PUBLIC;
