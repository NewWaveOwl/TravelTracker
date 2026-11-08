# M3-20 plan: accommodation search and selection

## Goal

Let a signed-in traveller search visible lodging, review its details, add a permitted place, and select the server-confirmed record while planning a trip. This is the frontend slice in the approved M3-20 scope; the independent Accommodations service and its HTTP contract already exist.

Grounding: `docs/design.md` sections 4, 6 and 7; `docs/api.md` accommodation contract; `docs/accommodations-service.md`; `docs/milestone-3-contracts.md`; current New Trip and Transport workspace. The accommodation search, details, add modal and feedback Figma frames are the visual references listed in M3-20.

### Figma frame map

- [Accommodation search and results](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=147-3364)
- [Accommodation details and selection](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=147-3488)
- [Add accommodation modal](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=147-3619)
- [Saved and ready to select](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-3085), [unavailable accommodation](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-2836), [loading](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-3198), and [no results](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-3304)
- [Yellow validation](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-3419), [permission denied](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-3566), and [connection failure with retry](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-3713)

The selected search frame was accessible in the linked Figma file. The in-app browser showed only part of its wide canvas at a readable scale, so implementation should compare each state against these linked frames directly and record the comparison in the implementation PR.

## Current state

| Implemented | Still needed for M3-20 |
| --- | --- |
| Independent Accommodations API on port 5106 with PostgreSQL, validated create, name search, detail, public GUIDs, `catalogue.read`/`catalogue.write`, private/shared rules and live Users checks. | Frontend service URL, accommodation public DTO/client, workspace, create draft/validation, selection callback, and accommodation HTTP/component tests. |
| `ProtectedClient` sends the current session token and enforces configured service destinations. `AttractionsClient` and the Attractions workspace provide the nearest frontend pattern for authenticated search/details/create and feedback. | `ServiceDirectory` currently permits collection filters only for Attractions and Transport; it must explicitly allow only the accommodation collection's encoded `name` query. |
| `Auth0Settings.DefaultScope` includes `catalogue.read` but not `catalogue.write`. The attraction write grant is intentionally opt-in and documented in `docs/frontend-auth0.md`. | Explain the accommodation write grant alongside the existing opt-in catalogue grant. Do not silently broaden default sign-in scopes. |
| `/new-trip` hosts `TransportWorkspace` and a placeholder `TripForm`. Transport selections are retained only in page memory; the UI says trip persistence is not connected. | Host accommodation selection in the New Trip draft and retain the confirmed accommodation's public ID. Do not claim that a trip or stay was saved. |

## Scope

### In scope

- Add a responsive accommodation workspace to `/new-trip` based on the supplied Figma search/results, details/selection, add-place and feedback states.
- Search the authenticated caller's visible records by literal name through the Accommodations HTTP API; allow an empty search to browse all visible records.
- Fetch details by public accommodation GUID before confirming a selection. Show the server's `private`/`shared` visibility label.
- Provide an add form for name, address, latitude, longitude and visibility. Default to private. Offer shared visibility only when the existing live Users profile indicates an administrator; this is a display affordance only, and the Accommodations API remains the authority.
- Require a valid active frontend session and `catalogue.read` to browse. Show read-only behavior without `catalogue.write`; the server remains responsible for enforcing all scopes, account standing and ownership.
- Retain search/create inputs after validation, permission, service and network failures. Disable duplicate submissions and do not automatically retry POST after an ambiguous network failure.
- Emit the saved public record through a selection callback and retain it in the New Trip in-memory draft. Preserve the existing transport draft and do not claim the trip itself is persisted.

### Out of scope

- Any changes to the Accommodations API, database, visibility policy or DTO contract.
- Accommodation edit, archive, delete, booking, availability, external lodging lookup, map/geocoding, or payment.
- Trip/stay persistence or changes to Trips endpoints; no accommodation is added to a saved trip in this slice.
- Admin catalogue screens or revealing another owner's private place.
- Adding `catalogue.write` to the default OAuth request or sending it to any service other than Accommodations.

## HTTP contract used

Base URL: configured `Services:Accommodations`, currently to be set to `http://localhost:5106` for local use.

| Operation | Request | Expected result and UI behavior |
| --- | --- | --- |
| Search | `GET /api/v1/accommodations` or `GET /api/v1/accommodations?name={encoded literal}` with `catalogue.read` | `200` array of visible public DTOs. Preserve API ordering. Other users' private records are omitted. |
| Details | `GET /api/v1/accommodations/{accommodationId}` with `catalogue.read` | `200` public DTO; `404` is shown as unavailable without implying whether a private record exists. |
| Create | `POST /api/v1/accommodations` with `catalogue.write`; body contains only `name`, `address`, `lat`, `lon`, and optional `visibility` | `201` saved public DTO plus `Location`. Select only after receiving the response and retain its `accommodationId`. Server generates ID and owner. |

The response shape is `accommodationId`, `name`, `address`, `lat`, `lon`, `visibility`. Search is case-insensitive literal substring matching; blank search returns all visible records. Validate trimmed nonblank name and latitude `-90..90` / longitude `-180..180` in the form and display field errors from the server. Address may be blank; do not send null. Omitted visibility is private. Only an active administrator may create shared records.

Expected auth/error handling follows the existing API client: missing/invalid bearer `401`; insufficient scope or denied shared create `403`; invalid input/unknown field `400` with field errors; hidden/absent detail `404`; service/User dependency failures `503`; network failures retain the current draft and offer retry for reads. A `401` clears protected catalogue data through the existing session behavior. Never infer success from a submitted request alone.

## File-by-file implementation plan

### Frontend API and configuration

- `src/TravelTracker.Frontend/Api/Accommodations/AccommodationDto.cs` — public response DTO with public GUID and visibility only; no owner, database key or server model.
- `src/TravelTracker.Frontend/Api/Accommodations/CreateAccommodationRequest.cs` — typed writable fields only; omit the ID and all identity/role/status fields.
- `src/TravelTracker.Frontend/Api/Accommodations/AccommodationsClient.cs` — authenticated search/detail/create using `ProtectedClient`, one encoded `name` query, bounded cancellation, and clear empty-response failures.
- `src/TravelTracker.Frontend/Api/ServiceDirectory.cs` — permit exactly the encoded optional `name` query for `/api/v1/accommodations`; retain current destination/path checks and reject other query forms.
- `src/TravelTracker.Frontend/Program.cs` — register `AccommodationsClient`.
- `src/TravelTracker.Frontend/appsettings.json` — add the Accommodations base URL at port 5106.

### Form and workspace

- `src/TravelTracker.Frontend/Features/Accommodations/AccommodationDraft.cs` — editable values, client-side field validation, and a create request builder. Keep it independent of HTTP and persistence types.
- `src/TravelTracker.Frontend/Components/Accommodations/AccommodationWorkspace.razor` — search/results/details/add modal and accessible loading, empty, validation, permission and connection states. Follow the supplied Figma frames and existing shared feedback patterns.
- `src/TravelTracker.Frontend/Components/Accommodations/AccommodationWorkspace.razor.cs` — session-aware orchestration, search/detail/create calls, server field errors, cancellation/stale-response protection, duplicate-save guard and confirmed-selection callback. Use the authenticated session scope metadata for UI availability and `UsersClient.GetMeAsync`/existing administrator state only to decide whether to display the shared option; never treat either as API authorization.
- `src/TravelTracker.Frontend/Components/Pages/NewTrip.razor` — host the workspace and retain selected confirmed accommodation in the in-memory New Trip draft alongside the existing transport draft. Show an explicit selection/draft status; keep the TripForm placeholder and avoid suggesting trip persistence.
- `src/TravelTracker.Frontend/wwwroot/styles.css` — responsive workspace/modal/card styling aligned with the approved accommodation frames and existing earthy palette; preserve focus visibility and small-screen usability.

### Tests and documentation

- `tests/TravelTracker.Frontend.Tests/AccommodationsClientTests.cs` — verify service destination, encoded/blank name search, GET detail, POST body allowlist and returned public GUID handling, HTTP status/field-error mapping, and no owner or identity fields.
- `tests/TravelTracker.Frontend.Tests/AccommodationWorkspaceTests.cs` — cover search/detail/create/select, private default, administrator-only shared affordance, read-only scope, validation without HTTP writes, retained form values, loading/empty/retry states, `401` session clearing, `403` permission, `404` private/unavailable detail, `503`/network failures, duplicate-submit prevention, and sign-out/account-change stale-response behavior.
- `docs/frontend-auth0.md` — document the optional `catalogue.write` grant/configuration for accommodation creation; keep the default grant read-only and explain that the API enforces access.
- `README.md` — update the frontend capability summary and New Trip instructions to include accommodation search/add/selection and explicitly state that the selection remains a draft until trip persistence is connected.
- `docs/m3-20-implementation.md` — record final behavior, exact build/test evidence, limitations, live verification limits and Figma frame links after implementation.
- `docs/m3-20-review.md` — attach the required read-only review and disposition of findings before PR completion.

## Acceptance and failure cases

1. Signed-in read-granted user loads search, sees visible results in API order, searches a literal name, opens details and selects the confirmed public ID.
2. Search with no matches shows the no-results frame and offers a clear/reset action; initial load and retry have distinct loading/error feedback.
3. A write-granted active traveller creates a private place. The selected item comes from the `201` response and its identifier is retained by the New Trip draft.
4. Form errors for blank name, nonnumeric/out-of-range coordinates and server validation preserve entered values and make no invalid POST; blank address is allowed and visibility defaults to private.
5. A non-administrator cannot choose shared. An administrator can request shared only when the live Users profile allows the affordance; a server `403` remains visible and the form is retained.
6. Another user's private item never appears in results; a guessed/inaccessible ID produces an unavailable `404` state without data disclosure.
7. Missing read/write scope and a session `401` show the documented sign-in/permission behavior; signed-out direct `/new-trip` access never exposes protected records.
8. Accommodations service `503`, network failure and post-create refresh failure show retry/confirmed-save feedback without losing inputs or accidentally submitting a duplicate.
9. Browser interaction uses only the HTTP client; no backend project references, local API shortcuts, or trip persistence claim are introduced.

## Risks and assumptions

- The Figma search frame is designed for a 1920×1180 canvas; implementation must adapt it responsively rather than reproduce that fixed canvas. Current repository UI is a Blazor interactive-server application and all API calls remain server-side through `ProtectedClient`.
- `catalogue.write` is opt-in in existing Auth0 configuration. Local/test UI behavior can be verified with trusted session-scope fixtures; the live tenant grant and protected end-to-end flow require deployment configuration and a fresh sign-in.
- Showing a shared-visibility control relies on the existing Users self-profile/live administrator information. If that lookup is unavailable, default to private and do not block private creation solely because the optional shared control cannot be determined; the Accommodations API still performs its live check.
- Search response does not identify record ownership, only visibility. UI must avoid labeling an item as "yours" based on visibility alone.
- Trip selection is in-memory only because the current `/new-trip` page does not save a trip/stay. M3-20 should not claim restart persistence for the selected draft.

## Approval and implementation status

The owner approved this plan in chat before implementation began. Implementation is complete on `accomodation-search-creative-view`; behavior and validation evidence are recorded in [`docs/m3-20-implementation.md`](../m3-20-implementation.md), with the read-only review in [`docs/m3-20-review.md`](../m3-20-review.md).
