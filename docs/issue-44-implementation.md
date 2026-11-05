# Transport and route-point selection (#44)

The owner-selected [Version 1 design](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=119-1028) is implemented in the interactive Server Blazor New Trip page. Existing Aldrich/Atkinson typography and cream background are retained; green panels, pale-red active cards and a circle/line tracker follow the reviewed concept.

The subsequent screenshot refinement places compact vertical travel labels beside the left connector, leaves transfer labels below each preceding card, numbers occurrences with two digits, and shows the removal action only while an entry is selected for editing. The active occurrence retains its pale-red card/circle. Unknown travel, transfer and total duration remain explicitly unavailable rather than using the screenshot's illustrative numbers.

After this refinement, the full Release build again passed with zero warnings/errors and all 505 application tests passed, including all 172 frontend tests. The edit/remove regression now verifies that removal appears after selecting an entry. No persistence behavior changed; the earlier 27-test relational verification remains applicable.

Selecting a saved transport reveals the destination/map panel with a 300 ms slide and fade. The panel keeps its draft controls mounted while closed, but is hidden from assistive technology and made inert until a transport is selected. New-entry/reset hides it again; selecting an existing chain entry opens it for editing. Narrow screens use a short vertical reveal, and reduced-motion preferences disable the transition.

Animation validation: the full Release build passed with zero warnings/errors; 506 application tests passed, including 173 frontend tests. One new interaction test verifies initial hidden/inert state, transport-triggered reveal, retained inputs through close/reopen, and reopening an existing draft entry. No persistence behavior changed.

Owner correction after live review removes transport searching entirely: the saved mode cards are available directly, and selecting one opens the map. Each draft occurrence now holds explicit saved departure and destination snapshots. From/To controls select which endpoint the map/list or creation form changes; both endpoints are re-read by GUID before confirmation. The next occurrence can prefill its departure from the previous destination, while editing restores both saved choices. The extra duplicate transport-details article is removed; details remain in the selected card.

Correction validation: Release build 0 warnings/errors; **508 application tests passed**, including **175 frontend tests** and **16 focused Transport tests**. New coverage proves distinct departure/destination IDs, disabled confirmation without a departure, automatic map opening without transport search, endpoint editing/prefill and saved creation of both endpoints. Independent read-only review found no remaining issues. Persistence code is unchanged.

The local Transport service was not listening at the frontend's configured port. It now runs at `localhost:5105` using a dedicated development database with the existing six transport and three location examples, with the existing local Auth0 issuer/audience configuration. Health returns 200 and anonymous catalogue reads return 401. The rebuilt frontend at `localhost:5100/new-trip` returns 200. Restart invalidates its prior session: protected live-browser inspection remains unverified because automatic approval rejected initiating the external Auth0 flow without explicit account-access authorization. The local page remains open for the owner to sign in.

## Behavior and boundaries

The owner-requested output separation and timing controls are now implemented: a cream review panel with a peach divider separates selected-route output and planning-minute inputs from the location catalogue and creation controls. `TravelMinutes` and `ChangeMinutes` are nullable draft-only values with no fabricated defaults. Changes are counted after their transport and only when a following draft entry exists. Editing preserves occurrence identity and restores entered minutes. Validation rejects negative, fractional, nonnumeric and overflowing inputs before any HTTP call; failed confirmation retains both timing fields. Totals use a wide integer and explicitly label incomplete values.

- `TransportClient` loads saved transport entries and filtered location collections plus saved detail records through `ProtectedClient`. Supported modes are Car, Bus, Train, Walking (`pedestrian`), Bike and Plane. Transport cards are directly selectable without searching or filtering first. Locations retain their accommodation, attraction or transport-stop type and public GUID.
- Selecting transport opens the map by default, with separate departure and destination choices. Both saved endpoint IDs are required and re-read, together with the transport ID, before adding a draft occurrence. Later occurrences can prefill departure from the preceding destination while keeping that choice editable.
- The left-hand ordered chain supports adding, selecting, changing and removing draft occurrences and shows both endpoints. The page receives transport/departure/destination DTO snapshots through `TransportWorkspace.SelectionChanged`; it does not call an unfinished Trips endpoint.
- **Choose on map** displays filtered saved locations on a fitted coordinate map. Markers can be selected by mouse or keyboard. The saved list is an alternative for overlapping markers. Street tiles, address lookup, arbitrary pin placement and drag/zoom are not implemented.
- Write-capable sessions can create named transport entries and locations with validated coordinates and optional source metadata. A map location is never treated as saved merely because coordinates exist; the server must return a saved public ID.
- Loading, no results, validation, forbidden actions, unavailable services and network failures receive feedback. Forms and choices are retained for same-session failures; account changes clear session-specific state. Failed writes do not add an itinerary entry.
- A visibly separate selected-route summary shows the current transport and endpoints, followed by optional travel and change-after-this-transport minutes. These are explicit traveller-entered draft planning estimates. Blank values remain unknown, and change minutes only enter the overall total when another entry follows. They are restored during editing and excluded from catalogue HTTP payloads. Figma sample durations are never used as defaults. There is no timetable, routing engine or trip persistence in this feature.

## Verification

Baseline on `origin/milestone_3` (`3ecf442`): 492 application tests passed (8 shared/API, 177 Attractions, 159 frontend, 94 Transport, 54 Users). The dedicated PostgreSQL baseline passed 27 tests (7 Attractions, 5 Transport, 15 Users). An initial attempt against another local test server failed because that server stopped; the dedicated isolated instance resolved the environment failure.

Final validation on 2026-11-04:

| Check | Result |
| --- | --- |
| `dotnet restore TravelTracker.slnx --force` | Passed; package metadata refreshed successfully |
| `dotnet build TravelTracker.slnx --configuration Release --no-restore` | Passed; 0 warnings, 0 errors |
| `dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category!=PostgreSQL"` | 516 passed, 0 failed, 0 skipped: 8 shared/API, 177 Attractions, 183 frontend, 94 Transport, 54 Users |
| `dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category=PostgreSQL"` with the dedicated local test instance | 27 passed, 0 failed, 0 skipped: 7 Attractions, 5 Transport, 15 Users |
| Focused `TransportWorkspaceTests` | 24 passed, covering saved IDs/direct selection/map, explicit From/To, failed/forbidden saves, field and timing validation, location filters, empty/unavailable results, repeated occurrences, edit/remove callbacks, timing retention/restoration/totals, rejected tokens and sign-out during confirmation |
| `git diff --check` | Passed |
| Independent read-only review | No remaining Critical, High, Medium or Low findings; see [review](issue-44-review.md) |

The ordinary suite increases from 492 to 516 without removing baseline tests. The latest full Release build passed with zero warnings/errors. Initial restricted restore produced NuGet metadata connectivity warnings; the refreshed restore/build has no warnings. The earlier PostgreSQL verification used isolated randomly named databases; subsequent changes affect only the frontend and its documentation/tests, so that unchanged persistence group was not rerun for the timing UI.

Runtime smoke check: the frontend started on an isolated local port; `/new-trip` returned HTTP 200 with the itinerary heading, sign-in prompt and retained trip form. An unknown route returned HTTP 404. Authenticated component/HTTP behavior uses isolated test handlers and fake validated sessions; this is not a new claim of live Auth0 end-to-end or backend bearer verification.

Design context and screenshot were read from the selected Figma node. Existing local logo/account/menu SVGs and bundled fonts are reused. The map and chain are data-driven controls rather than fixed illustrative streets/times. A populated component markup preview was exported for layout inspection, but a browser screenshot of the implementation was not completed: local preview access timed out and the browser policy rejected local-file navigation. Responsive layout is implemented in CSS; pixel-level desktop/mobile screenshot equivalence remains unverified.
