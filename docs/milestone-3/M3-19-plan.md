# M3-19 Authenticated Profile View Plan

Issue: [#45, M3-19](https://github.com/NewWaveOwl/TravelTracker/issues/45)  
Branch: `authenticated-profile-view`  
Dependencies: [#33, M3-07](https://github.com/NewWaveOwl/TravelTracker/issues/33), [#37, M3-11](https://github.com/NewWaveOwl/TravelTracker/issues/37)  
Status: research complete; awaiting approval before implementation.

## Goal and user value

Replace the account page's temporary protected-Transport check with an authenticated view of the traveller's own Users profile and a small editor for the fields the Users API permits. The page must show server-confirmed values and useful loading, validation, permission, account-standing, expired-session and service-failure feedback.

This is a frontend-only feature. The independent Users service already implements and documents `GET /api/v1/users/me` and `PATCH /api/v1/users/me`; the frontend must consume those public HTTP contracts without referencing Users Domain/Data/API assemblies or duplicating server authority.

## Ground truth

### Already implemented

- Auth0 sign-in, server-held bearer sessions, expiry/revocation handling and named-service HTTP calls exist in `src/TravelTracker.Frontend`.
- `ProtectedClient` sends the active bearer token to the configured Users service, maps `401` to session rejection and preserves the session on `403`.
- `UsersClient.GetMeAsync` reads `/api/v1/users/me`, but `SelfUserProfile` currently exposes only `PublicId`, `Role` and `Status`; there is no self-profile update method.
- `/account` currently offers sign-in/sign-out and a temporary Transport access check. `GET /api/v1/users/me` is already used by `AdminAccessState` for live administrator standing.
- Users supports editable `username`, contact `email` and `address`. Omitted fields stay unchanged; explicit `address: null` clears the address. Username/email cannot be set to null. Role and status are server-owned. The full contract is in [docs/api.md](../api.md#users-contracts).
- The frontend currently requests `catalogue.read` and `users.read` by default. The Users API requires the separate `users.write` permission for profile changes. A missing permission must leave the form read-only and the API remains authoritative.
- `NewTrip` is a draft form and `History` is a placeholder. There is no local guest profile or persisted local trip feature today.

### Not part of this issue

- No Users API, Domain, Data, migration, authentication-provider password or administrator UI work.
- No local guest profile editor, browser/local profile persistence, trip persistence or claim that a guest's profile/trips are saved on-device. The supplied guest Figma frame asks for device-only profile/trip messaging, but the current app has no such local data capability; adding it would expand this feature materially.

## User experience and states

- **Signed out:** retain an account/guest landing state with a clear sign-in action. Do not imply profile editing or trip data is stored locally. If product scope requires the guest Figma frame's local-data promise, decide and implement that as separate approved storage work first.
- **Loading:** announce that the profile is loading and do not flash stale profile values.
- **Active, read/write:** show profile overview, role/status as read-only metadata, and an edit action when the trusted session grant includes `users.write`.
- **Active, read-only:** show profile when `users.read` is granted but omit edit controls when `users.write` is absent; explain that editing permission is unavailable.
- **Banned:** show the returned profile and a read-only notice; disable editing even if the OAuth grant includes `users.write`. The API remains authoritative and can reject a write if standing changes during the session.
- **Denied/deactivated:** show a safe access-denied state when Users returns `403`. Use the trusted granted-scope claim to distinguish a missing read grant from a denied profile where possible; do not infer standing from provider identity claims.
- **Expired/rejected session:** clear displayed personal data and return to sign-in guidance after `401`; do not retry automatically.
- **Validation/conflict:** show field errors for `400`, explain username conflict or stale profile for `409`, preserve the user's form values, and do not display success.
- **Unavailable/network:** retain entered values, show a retry action, and avoid automatic PATCH retries because the outcome of a timed-out save may be unknown.
- **Confirmed save:** use the response body from PATCH to refresh the displayed values and show success only after the service confirms the update.
- Cancel in-flight reads/writes and clear private values when the authentication session changes or is revoked. Disable repeat submission while a save is active.

## Public profile contract and validation

`GET /api/v1/users/me` returns `publicId`, `username`, `email`, `address`, `role`, `status`, `createdAt` and `updatedAt`. Treat email as editable contact email, never as provider-verified identity. Render role/status as informational text only.

`PATCH /api/v1/users/me` accepts only `username`, `email` and `address`. Preserve field presence semantics: omit unchanged values; send explicit JSON `null` only when clearing `address`. The current API rejects null username/email and requires at least one supplied editable field. Mirror server validation in the form for username (3–32 ASCII letters, digits, `.`, `_`, `-`), email (valid, at most 254 characters) and address (optional/null or 1–500 nonblank characters). Server validation remains final.

Request only the additional least-privilege `users.write` scope needed for editing; do not request `users.access`, admin authority, or catalogue write for this profile screen. Display write controls based on trusted scope metadata and live profile standing, while handling API `403` in case access changes after rendering.

## File-by-file implementation plan

### Production frontend

- `src/TravelTracker.Frontend/Components/Pages/Account.razor` — replace the temporary API-access button with accessible profile overview/edit states matching the supplied frames. Keep the existing sign-in, sign-out, session-expiry and sign-in failure behavior. Use an `EditForm`, labelled inputs, validation summary/field messages, loading/error/status announcements and a disabled save button while submitting. Keep role/status out of the editable request model.
- `src/TravelTracker.Frontend/Components/Pages/Account.razor.cs` — move the growing page lifecycle and profile form orchestration out of inline Razor code. Load on direct `/account` navigation and after a valid authentication-state replacement; cancel stale calls, clear profile values on sign-out/revocation, preserve drafts on failed save, and apply only the server-confirmed PATCH response on success.
- `src/TravelTracker.Frontend/Api/Users/SelfUserProfile.cs` — expand the response DTO to all public self-profile fields, keeping only the public UUID and never adding provider issuer/subject or verified-email data.
- `src/TravelTracker.Frontend/Api/Users/UpdateSelfProfileRequest.cs` — add a frontend request type limited to the editable fields. Ensure the client can omit unchanged fields while representing an explicit null address clear.
- `src/TravelTracker.Frontend/Api/Users/UsersClient.cs` — add `UpdateMeAsync` using `ProtectedClient.SendJsonAsync` to the fixed Users `/api/v1/users/me` path. Return the server-confirmed self-profile DTO and retain categorized API errors for the UI.
- `src/TravelTracker.Frontend/Api/ServiceClient.cs` — add `username` and `email` to the explicit safe field-error allow-list so Users validation errors reach the profile form; keep filtering unknown problem fields.
- `src/TravelTracker.Frontend/Auth/Auth0Settings.cs` — add `users.write` to the default requested scope while retaining `users.read` and existing catalogue read scope.
- `src/TravelTracker.Frontend/appsettings.json` — keep its explicit default scope in sync with `Auth0Settings.DefaultScope`.
- `src/TravelTracker.Frontend/wwwroot/styles.css` — add only the responsive profile card/form and read-only/status treatments needed to match the referenced frames, preserving the existing palette, typography, keyboard focus and reduced-motion/accessibility conventions.

### Tests

- `tests/TravelTracker.Frontend.Tests/UsersProfileClientTests.cs` — cover fixed Users URL and bearer attachment, GET/PATCH public DTO mapping, field-presence/null-address request semantics, field-error filtering and categorized 401/403/409/503/network failures with a stub handler.
- `tests/TravelTracker.Frontend.Tests/AccountTests.cs` — replace temporary Transport-check expectations with profile page states and cover direct route load, signed-out view, active view/edit, read-only grant, banned state, denied/deactivated state, 401 session expiry, validation/conflict/network feedback, duplicate-save prevention, retained input on failure, confirmed response refresh and removal of personal data on sign-out/revocation or account replacement.
- `tests/TravelTracker.Frontend.Tests/Support/FrontendFactory.cs` — change only if the existing test host needs a Users endpoint or session fixture to prove direct-route behavior; prefer the existing BUnit service setup and stub handlers where sufficient.

### Documentation/configuration

- `docs/frontend-auth0.md` — document `users.write` as a user-delegated permission for profile edits, add it to the frontend default scope instructions and explain that existing sessions need a fresh sign-in to receive the grant. Preserve the rule that Auth0 passwords are never collected by this app.
- `README.md` — move the profile editor from unavailable/planned capability into the implemented frontend feature summary after implementation and validation.
- `docs/milestone-3/M3-19-plan.md` — record approval, implementation checkpoint and final evidence after the work; keep the research baseline and open guest-profile scope note.

No update to `docs/api.md` or Users backend code is planned because this feature consumes the existing profile contract without changing it.

## Acceptance and verification cases

1. Signed-in active user opens `/account` directly, sees the current server profile, edits one or more permitted fields, receives the server response, and sees those values after reloading.
2. Client request contains only permitted profile fields; attempting to add ID, provider identity, role or status is impossible through the UI model and rejected by the existing API contract.
3. Active user with `users.read` but no `users.write` can view but cannot submit changes; direct API write remains forbidden.
4. Banned user sees their profile and a clear read-only state. Deactivated/unknown or otherwise forbidden profile access shows denied state without leaking data.
5. Invalid fields, username conflict and concurrency conflict keep user input visible and show relevant field or general errors; no success announcement occurs.
6. `401` ends the usable frontend session and removes personal profile data; `503`, timeout or network loss leaves the draft intact and offers retry without duplicate submission.
7. A change/sign-out while a request is in flight cannot restore stale profile data or show a late success.
8. Signed-out account state offers sign-in and does not falsely claim that profile/trip editing is locally persisted.

Run frontend tests and the required solution build/ordinary test suite after approval. Before completion, verify the rendered overview, edit, read-only, denied and expired states against their linked Figma frames. Figma links were not readable through the research browser during this planning pass; visually inspect the linked frames in Figma before UI implementation. No live user account should be modified for verification without the owner's explicit direction.

## Figma references

- [Profile overview](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-2028)
- [Edit profile](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-2141)
- [Signed-out local guest profile](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=150-2599) — informational sign-in view only unless local guest-profile/trip persistence is separately approved and implemented.
- [Read-only profile](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-2253)
- [Access denied](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-2372)
- [Session expired](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-2480)
- [Connection error pattern](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=92-3523)

## Approval gate

This file is the file-by-file implementation plan required by M3-19. No production feature code is changed until the developer approves the plan. The signed-out guest frame's promise of local profile/trip persistence is the one scope question to settle before implementation; current code has no such local persistence.
