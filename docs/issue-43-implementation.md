# Issue #43 implementation plan and evidence

Approved scope: implement the selected Figma **04 / Search workspace** and **04 / Choose a location** in the existing Blazor frontend. Use the owner-supplied Montreal screenshot as a static map placeholder. Keep the independent secured Attractions API and PostgreSQL contracts unchanged.

Baseline: `origin/milestone_3` at `50350da`, which includes the merged Blazor/authentication PR #74 and Attractions work. `master` has no frontend; this feature PR targets `milestone_3`.

## File plan

- `src/TravelTracker.Frontend/Api/{ServiceClient,ProtectedClient,ServiceDirectory,ApiError}.cs`: authenticated GET/POST/PATCH, single encoded name query, field errors, bounded cancellation and session-race safeguards. No automatic write retries.
- `src/TravelTracker.Frontend/Api/Attractions/`: public attraction response and HTTP client. Keep unknown/server-owned fields out of writable payloads; PATCH includes only changed fields and explicit optional nulls.
- `src/TravelTracker.Frontend/Features/Attractions/` and page/shared components: validated edit draft, two-column ordered cards, details, create/edit, map preview with manual coordinate selection, guarded saves, accessible status/error/focus and stale-response protection.
- `Components/Layout/SiteHeader.razor`, styles and `Program.cs`: attraction navigation, responsive layouts, reuse existing local fonts/assets/tokens and register the HTTP client.
- `Auth/{AuthExtensions,AccessTokenClaims}.cs`: record granted scopes from the trusted OAuth token response in server-owned session claims for UI availability. The service remains authoritative for permissions, standing and ownership.
- `wwwroot/assets/attractions-map-placeholder.png`: exact image supplied by the user. No geocoding, live tiles, fabricated screenshot-to-coordinate mapping or external map requests.
- `tests/TravelTracker.Frontend.Tests/`: HTTP-contract, authorization/session, component and failure-journey tests.
- README, frontend/API/auth docs: capability, access requirements, limitations and validation evidence.

## Acceptance journeys

Create → search by literal name → details → edit → reload; clear description/rating while preserving omitted fields; reject bad fields and preserve input; prevent duplicate saves; handle read-only and server 403; loading, empty, network and service errors; cancel/reopen map draft without changing other form fields.

The requested duration ± control is a **preview-only** planning aid: 120 ± 30 yields 90–150 minutes. The existing HTTP contract stores only estimatedVisitMinutes. Approximation is explicitly not persisted or sent as an unknown field; persisting it requires a future agreed API/data change. The screenshot is a static preview; manual coordinates are authoritative until real map/geocoding integration exists.

## Validation

Results are recorded after implementation. Backend API/security/ownership behavior is not replaced by frontend checks. Isolated HTTP/component tests do not establish a live Auth0/Users/PostgreSQL end-to-end journey.

### Final local evidence (2026-11-04)

- Clean Milestone 3 baseline: **367 passed, 0 failed, 0 skipped** (API 89, Attractions 177, frontend 101), executed from the unchanged PR #74 checkout with `--no-build --no-restore` because that checkout's frontend executable was already running. No running user process was stopped.
- `dotnet restore TravelTracker.slnx --disable-parallel`; frontend dependency audit was refreshed with `dotnet restore tests/TravelTracker.Frontend.Tests/TravelTracker.Frontend.Tests.csproj --force --no-cache` after an initial transient NuGet audit lookup warning.
- Final `dotnet build TravelTracker.slnx --configuration Release --no-restore`: **0 warnings, 0 errors**.
- Final `dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category!=PostgreSQL"`: **408 passed, 0 failed, 0 skipped** (API 89, Attractions 177, frontend 142). Frontend coverage increased by 41 tests; no baseline tests were removed.
- New coverage includes a component create/search/details/edit/reload journey, explicit optional clearing and untouched whitespace preservation, validation and 400/403/503 draft retention, exactly one in-flight save, map Confirm/Cancel, literal encoded search, malformed validation bodies, sign-out races, and real OIDC middleware storing trusted grant metadata rather than provider-supplied server claims.
- Browser comparison used test-rendered real Razor markup plus production styles/fonts/assets at 1920×1080 and 390×844. Two desktop card columns, narrow single-column fallback, product fonts and loaded local assets were confirmed; no horizontal overflow. These are deterministic fixture renders, not a live authenticated service journey.
- Running the actual frontend on a separate loopback port returned 200 for `/attractions` with the expected anonymous sign-in prompt and 200 for the map asset. SHA-256 matches the supplied image exactly.
- PostgreSQL relational tests and the live Auth0 + Users + PostgreSQL browser journey were **not run locally** because this worktree has no configured isolated PostgreSQL/Users environment. The PR's existing CI supplies PostgreSQL and runs the relational group.
- `git diff --check` passed. Independent read-only review found two medium and one low issue; all were corrected and re-reviewed with **0 remaining actionable findings**. See [review record](issue-43-review.md).
