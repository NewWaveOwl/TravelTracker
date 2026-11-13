#!/usr/bin/env bash
set -euo pipefail
# Uses standard PGHOST/PGPORT/PGUSER/PGPASSWORD settings on an isolated test server.
# Never run against the shared Supabase project. CI supplies its disposable PostgreSQL.
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
database="traveltracker_auth_hook_test_${RANDOM}_$$"
[[ "$database" =~ ^traveltracker_auth_hook_test_[0-9]+_[0-9]+$ ]] || exit 2
createdb "$database"
trap 'dropdb --if-exists "$database"' EXIT
psql -X -v ON_ERROR_STOP=1 -d "$database" -f "$root/scripts/TestSupabaseAuthHook.sql"
