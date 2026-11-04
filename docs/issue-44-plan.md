# Transport and route-point selection (#44)

Implement the owner-selected [Version 1 side-by-side concept](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=119-1028), including the subsequent green-panel/peach-selection palette, map destination selection and ordered left-hand transport chain. The owner's chat request authorizes implementation and a pull request. Target `milestone_3`, the issue's integration branch: `master` does not contain the required Blazor/authentication/Transport dependencies.

## Ground truth

The independent Transport service already supplies authenticated collection, detail and creation endpoints for saved transport entries and route points. The Blazor frontend supplies `ProtectedClient`, session revocation and the attraction workspace. Trips currently has a placeholder form, without a trip persistence endpoint. Existing attraction maps are static previews, not interactive pickers.

Catalogue records have no schedules or travel times. The design's illustrative durations must not become fabricated runtime estimates. Any displayed draft duration must be entered explicitly by the traveller; otherwise the UI states that timing is unavailable. A map coordinate has no saved public identity until an authorized route-point creation succeeds.

## File responsibilities

- `src/TravelTracker.Frontend/Api/Transport/`: separate public transport/route-point DTOs and creation requests, plus one typed client using `ProtectedClient` for collection/detail/POST calls. Public GUIDs only; no backend references.
- `src/TravelTracker.Frontend/Features/Transport/`: validated creation and ordered selection draft state suitable for trip-editor integration. Preserve repeated uses as ordered occurrences.
- `src/TravelTracker.Frontend/Components/Shared/`: editable side-by-side workspace, circle/line tracker and functional destination map/coordinate controls, with accessible empty/loading/failure states.
- `src/TravelTracker.Frontend/Components/Pages/NewTrip.razor`: host the selection workspace alongside the existing trip form without claiming a saved trip.
- `src/TravelTracker.Frontend/Program.cs`: register the typed frontend client and draft services.
- `src/TravelTracker.Frontend/Api/ServiceClient.cs`: recognize Transport validation fields and use resource-neutral errors where shared.
- `src/TravelTracker.Frontend/Api/ServiceDirectory.cs`: allow only the supported encoded collection filters for the configured Transport service, preserving the protected destination restrictions.
- `src/TravelTracker.Frontend/wwwroot/styles.css`: responsive green panels, peach active selections and left timeline using existing typography and palette.
- `tests/TravelTracker.Frontend.Tests/`: HTTP contract and bUnit journey/failure coverage using isolated handlers and fake authenticated sessions.
- `docs/issue-44-implementation.md`, `docs/issue-44-review.md`, `docs/transport-service.md`, `README.md`: describe reachable behavior, verification, integration boundary and limitations.

## Acceptance and validation

Choose/filter each supported mode and each location type, load saved details, retain exact public IDs, add ordered selections and edit/remove a selected occurrence. Verify map/manual coordinates, validation and save-before-selection behavior. Cover empty results, loading, forbidden reads/writes, unavailable service, connection failure, retained drafts, account changes and late responses. API calls carry the existing session bearer; frontend tests do not establish backend bearer validation.

Run baseline and final frontend tests, full solution restore/build with zero warnings/errors, ordinary solution tests and the isolated PostgreSQL group when available. Review the diff for secrets and layer boundaries, obtain a read-only agent review, then commit, push and open the PR. Human review and merge remain separate.
