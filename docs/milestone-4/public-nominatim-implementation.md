# Public Nominatim and shared routing implementation — 2026-11-22

Implemented in the local `feature/public-nominatim` TravelTracker branch and
`backedn-for-TravelTracker` RNDbackend branch. Source changes remain uncommitted
and have not been pushed. The earlier empty backend branch is published, but
does not yet contain this implementation. No live server was updated.

The [integration guide](../public-nominatim-integration.md) defines contracts,
provider policy, Dev Tunnel configuration and target-server checks. The previous
Photon recommendation is explicitly superseded by the owner's Public Nominatim
decision.

## Behavior delivered

- Search/Enter address submission, reverse lookup after marker placement/move,
  an animated spinner inside the address field, cancel/manual fallback, editable
  suggestions, explicit marker confirmation and stale-response rejection.
- Protected, app-specific backend routes; one global Nominatim queue/cache,
  bounded authentication/provider concurrency, provider backoff and no automatic
  HTTP retries. Authentication project overrides are validated as a complete pair.
- Chosen-endpoint OTP previews for walking, cycling, car, bus and train, with real
  provider geometry and estimated/scheduled/realtime labels. Manual trip times and
  saved coordinates are not replaced by unconfirmed suggestions.
- A second Windows control window over the existing process controller, with
  shared tunnel/state and aggregate counters. Closing the secondary window hides it.
- A transferable JAR update ZIP, compatibility/hash checks, per-file backup and
  rollback; existing EXE/runtimes/graph/data/settings are preserved.

## Validation evidence

| Check | Observed result |
| --- | --- |
| `dotnet restore TravelTracker.slnx` | Passed. |
| `dotnet build TravelTracker.slnx --configuration Release --no-restore` | Passed; 0 warnings, 0 errors. |
| `dotnet test TravelTracker.Tests.slnx --configuration Release --filter 'Category!=PostgreSQL' --logger 'trx;LogFilePrefix=nominatim-validation'` | 1,716 discovered/executed/passed; 0 failed, 0 skipped. PostgreSQL-only projects have no applicable cases under this filter. |
| Frontend affected-suite baseline/final | 625 → 662 passed; 37 additional meaningful cases. |
| `gradlew.bat -p backend test :launcher:test installDist :launcher:installDist --console=plain` in RNDbackend | Passed with no compiler warnings; backend 44 → 73, launcher 45 → 60; all 133 cases passed, 0 failed/errors/skipped. |
| `backend/scripts/Test-TravelTrackerUpdate.ps1` in RNDbackend | 8 isolated checks passed: CheckOnly/no writes, standalone Windows PowerShell -File default-path resolution, apply, rollback, corruption, duplicates, traversal and dependency mismatch. |
| Launcher visual QA | Main, secondary and settings windows rendered and visually inspected with fixtures; no services/tunnel started. |
| Read-only AI review | No remaining confirmed findings after rate-limit transport, pre-Auth admission, settings-validation and pending-marker-name fixes. This is not human teammate approval. |
| Diff checks | Both repositories pass `git -c core.whitespace=cr-at-eol diff --check`. |

An initial test-solution call with `--no-build` could not find Journey.Tests because
the full production solution does not build that project. The prescribed normal
test-solution command built it and completed successfully. No source/test was
removed or filtered beyond the repository's documented PostgreSQL category split.

## Windows artifact

RNDbackend relative path:

```text
backend/launcher/build/updates/TravelTracker-Nominatim-20261122-powershell-fix/RNDTransitServer-TravelTracker-Update.zip
```

SHA-256: `7F60C006A3F6EE34DEC2F8A8978208E56A6C436050206E3DDE81DBB9E3D72515`.

This corrected ZIP supersedes the earlier `-final` and non-final build folders.
The user reported an empty `LiteralPath` on the target PC: Windows PowerShell 5.1
`-File` evaluated the old `$PSScriptRoot` default before it was initialized. The
helper now resolves the omitted update directory in its script body, with a fresh
PowerShell-process regression test. Older copies also work when the extracted
update folder is supplied explicitly with `-UpdateDirectory`. This error occurs
before any backup or target-file writes. Application JARs are unchanged by this fix.

The ZIP contains 34
verified JARs and the update helper/manifest/instructions; no OTP graph, runtime or
private settings. Apply only after stopping shared services and exiting the tray
launcher. The helper refuses differing dependency filenames; use a complete new
portable package if that compatibility check fails. The included README explains
CheckOnly, apply and rollback.

## Preserved work and verification limits

The ML_4 checkout was over 100 commits behind its tracked remote and had a prior
Auth0 README edit. Before switching to current `milestone_4` source, that edit was
preserved in the named stash
`preserve-preexisting-Auth0-README-before-address-lookup`; it was not discarded or
silently applied to the current Supabase README.

`TRAVELTRACKER_TEST_POSTGRES` was not configured, so the PostgreSQL/relational group
was not run. This change has no schema/persistence implementation edits. No live
Nominatim calls, real authenticated Dev Tunnel journey, target-PC OTP mode/date
coverage, physical-device UI test or actual server update/rollback is claimed.
Finish those configured-environment checks before claiming deployment readiness.
