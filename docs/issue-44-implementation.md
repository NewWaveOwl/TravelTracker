# Transport and route-point selection (#44)

The owner-selected [Version 1 design](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=119-1028) is implemented in the interactive Server Blazor New Trip page. Existing Aldrich/Atkinson typography and cream background are retained; green panels, pale-red active cards and a circle/line tracker follow the reviewed concept.

The subsequent screenshot refinement places compact vertical travel labels beside the left connector, leaves transfer labels below each preceding card, numbers occurrences with two digits, and shows the removal action only while an entry is selected for editing. The active occurrence retains its pale-red card/circle. Unknown travel, transfer and total duration remain explicitly unavailable rather than using the screenshot's illustrative numbers.

After this refinement, the full Release build again passed with zero warnings/errors and all 505 application tests passed, including all 172 frontend tests. The edit/remove regression now verifies that removal appears after selecting an entry. No persistence behavior changed; the earlier 27-test relational verification remains applicable.

Selecting a saved transport reveals the destination/map panel with a 300 ms slide and fade. The panel keeps its draft controls mounted while closed, but is hidden from assistive technology and made inert until a transport is selected. New-entry/reset hides it again; selecting an existing chain entry opens it for editing. Narrow screens use a short vertical reveal, and reduced-motion preferences disable the transition.

Animation validation: the full Release build passed with zero warnings/errors; 506 application tests passed, including 173 frontend tests. One new interaction test verifies initial hidden/inert state, transport-triggered reveal, retained inputs through close/reopen, and reopening an existing draft entry. No persistence behavior changed.

## Behavior and boundaries

- `TransportClient` reads filtered saved transport/location collections and saved detail records through `ProtectedClient`. Supported modes are Car, Bus, Train, Walking (`pedestrian`), Bike and Plane. Locations retain their accommodation, attraction or transport-stop type and public GUID.
- The left-hand ordered chain supports adding, selecting, changing and removing draft occurrences. The page receives the public DTO snapshots through `TransportWorkspace.SelectionChanged`; it does not call an unfinished Trips endpoint.
- **Choose on map** displays filtered saved locations on a fitted coordinate map. Markers can be selected by mouse or keyboard. The saved list is an alternative for overlapping markers. Street tiles, address lookup, arbitrary pin placement and drag/zoom are not implemented.
- Write-capable sessions can create named transport entries and locations with validated coordinates and optional source metadata. A map location is never treated as saved merely because coordinates exist; the server must return a saved public ID.
- Loading, no results, validation, forbidden actions, unavailable services and network failures receive feedback. Forms and choices are retained for same-session failures; account changes clear session-specific state. Failed writes do not add an itinerary entry.
- Travel/transfer durations are explicitly not supplied. The Figma sample durations were illustrative and are not rendered as genuine estimates. There is no timetable, routing engine or trip persistence in this feature.

## Verification

Baseline on `origin/milestone_3` (`3ecf442`): 492 application tests passed (8 shared/API, 177 Attractions, 159 frontend, 94 Transport, 54 Users). The dedicated PostgreSQL baseline passed 27 tests (7 Attractions, 5 Transport, 15 Users). An initial attempt against another local test server failed because that server stopped; the dedicated isolated instance resolved the environment failure.

Final validation on 2026-11-04:

| Check | Result |
| --- | --- |
| `dotnet restore TravelTracker.slnx --force` | Passed; package metadata refreshed successfully |
| `dotnet build TravelTracker.slnx --configuration Release --no-restore` | Passed; 0 warnings, 0 errors |
| `dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category!=PostgreSQL"` | 505 passed, 0 failed, 0 skipped: 8 shared/API, 177 Attractions, 172 frontend, 94 Transport, 54 Users |
| `dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category=PostgreSQL"` with the dedicated local test instance | 27 passed, 0 failed, 0 skipped: 7 Attractions, 5 Transport, 15 Users |
| Focused `TransportWorkspaceTests` | 13 passed, covering saved IDs/map selection, failed/forbidden saves, field validation, encoded filters, empty/unavailable results, repeated occurrences, edit/remove callbacks, rejected tokens and sign-out during confirmation |
| `git diff --check` | Passed |
| Independent read-only review | No remaining Critical, High, Medium or Low findings; see [review](issue-44-review.md) |

The ordinary suite increases from 492 to 505 without removing baseline tests. Initial restricted restore produced NuGet metadata connectivity warnings; the refreshed final restore/build has no warnings. PostgreSQL tests use isolated randomly named databases on a dedicated task-local cluster, which is stopped after verification.

Runtime smoke check: the frontend started on an isolated local port; `/new-trip` returned HTTP 200 with the itinerary heading, sign-in prompt and retained trip form. An unknown route returned HTTP 404. Authenticated component/HTTP behavior uses isolated test handlers and fake validated sessions; this is not a new claim of live Auth0 end-to-end or backend bearer verification.

Design context and screenshot were read from the selected Figma node. Existing local logo/account/menu SVGs and bundled fonts are reused. The map and chain are data-driven controls rather than fixed illustrative streets/times. A populated component markup preview was exported for layout inspection, but a browser screenshot of the implementation was not completed: local preview access timed out and the browser policy rejected local-file navigation. Responsive layout is implemented in CSS; pixel-level desktop/mobile screenshot equivalence remains unverified.
