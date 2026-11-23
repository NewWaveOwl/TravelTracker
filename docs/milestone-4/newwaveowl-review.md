# NewWaveOwl implementation and review — 2026-11-21

Branch: `braacnh_artiom_ml4`, based on the repository's actual default branch `milestone_4` at `68746a0`. The initial working tree was clean; no pre-existing user changes were included or discarded.

The subsequent [Figma alignment review](figma-alignment-review.md) records the user's design reference, visual corrections to the added controls, verification and retained differences from older mockups.

Supported sub-agents used the repository's frontend, persistence, testing and read-only code-review instructions. The Markdown agent definitions were used as guidance for available runtime agents, rather than claimed as directly launched agent types. File ownership was separated; the primary agent alone staged, committed and published changes. The final reviewer found and rechecked the focused-field save fix; additional feature-review findings are recorded below.

## Scope and dependency decisions

Read all currently assigned open/closed issue descriptions and comments through authenticated GitHub CLI, linked PR metadata and repository history. Cloud-browser access is unavailable. The CLI token lacks `read:project`; the existing Opera window provided a read-only view of project 3 instead. No issue closure, board status change, merge, deployment, force push or dependency-branch import was performed.

| Assigned open issue | Observed board state | Result in this branch |
| --- | --- | --- |
| #121 — draggable workspace panels | Backlog | Implemented: Itinerary, Map tools and Stop editor handles, pointer/touch movement, keyboard arrows/Shift/Home, individual/global reset, viewport/resize bounds and unchanged child state. Depends on completed #117. |
| #119 — prominent save and Ctrl+S | Blocked | Button and Ctrl+S/Cmd+S share a guarded save path, preserve edits on failure and require explicit catalogue creation. Focused fields commit before keyboard save. Partial: #105's relaxed empty/single-stop contract is not present, so current validation remains. |
| #113 — address search | Blocked | #104 remains open; no agreed candidate-search API is implemented in this checkout. Existing saved-catalogue search is not address geocoding. |
| #120 — addresses instead of coordinate fields | Blocked | Depends on #104. Ordinary forms still require coordinate work; this issue is not claimed complete. |
| #109 — PDF ticket extraction | Blocked | Actual GitHub assignment includes NewWaveOwl despite the older body naming 5unekku. #97 is closed; #98 is In Progress in unmerged PR #132. The protected import contract and size/page/parser rules are not integrated. No upload/parser implementation is claimed. |
| #122 — review PDF/text ticket proposals | Blocked | Depends on #109 and #110. Neither review-draft backend is present. No speculative frontend contract added. |
| #127 — route preview selection | Blocked | Depends on open #107/#108 and completed #117. Verified routing/mode/GTFS response contracts are absent. Existing approximate lines are not route alternatives. |

## Previously implemented work reviewed

Assignment, issue authorship and implementation authorship were checked separately. Closed states and old checklists were not treated as proof of working behavior.

| Features/issues | Implementation evidence and current assessment |
| --- | --- |
| Attractions #15, #51–#56, #35 | NewWaveOwl's PRs #16/#58/#70/#77 and subsequent team changes; current API/Data/Domain and HTTP/privacy tests inspected. Create/detail/literal search/atomic PATCH/repeated-query validation remain present. #55's process-only store was deliberately superseded by #35 PostgreSQL persistence. Confirmed culture/collation search regression fixed here; database visibility remains enforced before ordinal filtering/order. |
| Compiler warnings #31 | PR #64; current full solution builds with zero warnings/errors. No empty bookkeeping commit. |
| Bearer infrastructure #34 | PR #69 plus later Supabase work in #130; current signed-token tests exercise issuer/audience/signature/lifetime and scope boundaries. Local fixture evidence does not establish live Supabase availability or hook deployment. |
| Transport #36/#44 | NewWaveOwl PRs #76/#78; independent host and client selection implementation inspected and automated tests rerun. PostgreSQL restart/concurrency were not verified locally. |
| Users #37 and administrator integration | Joint assignment: PR #80 was opened by ChristelEdee; NewWaveOwl also contributed substantial implementation (`61ab085`) and administration in #75. Profiles, access scopes, status/ownership and bootstrap tests rerun. The client setup lookup/focus failure is fixed here; no unrelated changes to unmerged PR #132. |
| Frontend foundation/sign-in #32/#33 | NewWaveOwl's React PR #72 was superseded by the team's Blazor PR #74. NewWaveOwl PR #73 is closed **without merge**; it is not claimed present. Current Supabase/compatibility client tests were exercised instead. |
| Attraction UI #43 | NewWaveOwl PR #75; current search/create/PATCH forms and error/privacy tests reviewed. Denied reads now clear cached records/details, and missing detail reads remove the affected stale record. Write-denial drafts remain intact. |
| Accommodation/settings UI #46/#48 | Shared assignment does not imply NewWaveOwl implemented everything: accommodation PR #87 was ChristelEdee's; settings work came through team PRs #85/#89. Current client suites included in the complete frontend run. |
| Trip editor/map #47/#82; #115/#117/#123/#124/#125 | NewWaveOwl PRs #88/#95/#130; current route loading, draft restoration, catalogue confirmation, ETags, minute controls/untouched seconds, stop edits/cancel and panel state tests reviewed. Map marker Space scrolling fixed. Live manual checks covered map rendering, name edits, keyboard validation, drag/Home/reset and responsive access; authenticated server save/reopen remains unverified locally. |
| History #118 | PR #130; filtering, ordering, paging, empty/error and session-clearing behavior inspected and tested. No additional confirmed defect requiring a commit. |
| Planning/report/workflow #5/#9/#12/#28/#50 | Historical planning, reflection and reporting items inspected as evidence records, not executable features. This review does not fabricate another student's reflection, peer approval, human gate approval or instructor signoff. |

## Confirmed fixes and verification

- SVG marker selection/removal responded to Space while also scrolling the page. Browser listener now prevents the unwanted scroll; two regression cases failed before the fix and all three marker keyboard cases pass afterward.
- Keyboard save could run before an active `onchange` binding reached the model. It now blurs the active planner field before dispatching save. A focused trip-name edit immediately followed by Ctrl+S appeared correctly in review in the real browser; JavaScript tests verify update-before-save ordering.
- Attraction search used invariant-lowercased input against provider/culture-lowercased stored names, and database-dependent ordering. It now applies ordinal comparison and deterministic UUID tie ordering after the database visibility predicate. Turkish/English HTTP tests cover casing, punctuation, accented names, ties and owner isolation. The unpaged query materializes all visible rows; this is a documented scaling tradeoff, not a provider replacement.
- Initial administrator-status lookup 403/503 left authentication state unset and attempted to focus an absent error element. It now shows a retryable account-check error, blocks setup while the lookup is unresolved, and treats 401 as sign-in without the focus crash. Three regression cases cover denial, outage/recovery and rejected sessions.
- Attraction collection/detail read 403 retained cached private text and cards; detail 404 left the stale record selectable. Read failure handling now clears forbidden cached state or removes the missing record, while preserving drafts on write-only denial and distinguishing temporary 503 reads from lost authorization. Six regression cases cover these boundaries.
- Supabase token/JWKS/user service outages were mapped to invalid credentials. Sanitized transient errors now reach fixed unavailable/rate-limit feedback; 401 credential rejection remains distinct. Sign-up and email confirmation use the same shared transport distinction. Sixteen HTTP regression cases use real antiforgery forms, signing fixtures and cookie middleware to verify no session or private provider diagnostics escape on failure. JWT validation and permissions are unchanged.

| Check | Result |
| --- | --- |
| `dotnet restore TravelTracker.slnx` | Passed. |
| Baseline Release solution build | Passed, 0 warnings / 0 errors. |
| Baseline `dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL"` | 1,241 passed, 0 failed, 0 skipped; frontend 438. |
| Final `dotnet build TravelTracker.slnx --configuration Release --no-restore` | Passed, 0 warnings / 0 errors. |
| Final non-PostgreSQL suite, same command as baseline | 1,275 passed, 0 failed, 0 skipped; frontend 470, Attractions 191. Thirty-four additional cases; existing coverage retained. |
| `node --test tests/TravelTracker.Frontend.Tests/TripSaveKeyboardTests.mjs tests/TravelTracker.Frontend.Tests/TripMapCanvasKeyboardTests.mjs tests/TravelTracker.Frontend.Tests/MovablePanelTests.cjs` | 21 passed, 0 failed, 0 skipped with Node 24.19.0. Separate from `dotnet test`; now also wired into PR CI with Node 24. |
| `dotnet run --file scripts/TestLauncher.cs` | 21 passed, 0 failed. Offline configuration only. |
| Running frontend at `http://localhost:5100/map` in native Opera | Desktop drag and Home retained trip text; Ctrl+S used current focused name and showed validation without Save Page dialog. 400×716 emulation retained sticky Save/Reset, accessible panels below the map, touch-emulated dragging/clamping and global reset. Desktop layout restored afterward. |
| Browser diagnostics | Fresh preview connected to Blazor; visible network assets returned 200. Console showed three external map-style null-filter warnings, no application exception during the fresh preview checks. Deliberate host stops produced expected reconnect/network errors before reload. Development host logged its existing HTTP-only HTTPS-redirection warning. |
| Diff review | Scoped files, staged diffs and whitespace checked with `git -c core.whitespace=cr-at-eol diff --check`, preserving existing tracked CRLF/mixed line endings. |

The Supabase authentication failure review consulted the current [changelog](https://supabase.com/changelog.md) and official [Auth error-code documentation](https://supabase.com/docs/guides/auth/debugging/error-codes.md), including provider rate limits and degraded-service responses. No schema, permission, API-key or provider configuration change is required for the client error-reporting fix.

## Remaining verification and delivery limits

No configured `TRAVELTRACKER_TEST_POSTGRES`, local PostgreSQL client/server or Docker/Podman runtime was available. The marked relational suite was not run locally, including two new PostgreSQL ordinal-search cases; existing EF InMemory HTTP tests do not prove migration, locking, rollback or restart durability. CI's isolated PostgreSQL job remains the relational verification path.

No live Supabase credentials were used, no shared database was modified, and no production data was touched. Live sign-in, full-stack save/reload/restart and physical mobile-device behavior remain unverified. Emulated touch and synthetic pointer tests are explicitly distinguished from physical-device testing. No human teammate approval is claimed.

The final read-only agent review found no remaining confirmed findings in the combined implementation, including the three client recovery fixes and CI wiring. That verdict is subject to the separately recorded validation results and is not human approval. The three late client fixes were verified with rendered components or in-process HTTP hosts; their authenticated failure flows were not repeated in a live provider browser session.

The PR is a draft because the assigned dependency-blocked issues and required live integration evidence remain incomplete. Only #121 is a candidate for `Closes`; #119 and the other remaining issues are related, not resolved.

## Focused commits

| Commit | Result |
| --- | --- |
| `46dbfb4` | Prominent workspace save and Ctrl+S/Cmd+S, partial #119. |
| `e185fc6` | Prevent page scrolling on marker Space activation, related #115/#82. |
| `7af86c1` | Commit the focused field before keyboard save, related #119/#124. |
| `05f3d99` | Accessible movable panels and layout reset, #121. |
| `0cf9a67` | Restore ordinal attraction search/filter/order, related #53/#35. |
| `497c0a5` | Run the JavaScript interaction regressions in PR CI. |
| `841eb1c` | Recover first-administrator setup after lookup failure, related #37/#43. |
| `7ae884f` | Clear stale attraction state after denied/missing reads, related #43. |
| `f6791c1` | Distinguish Supabase outages/rate limits from credential rejection, related #33. |
