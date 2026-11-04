# First administrator setup

Owner explicitly approved implementing a one-time first-administrator setup after requesting promotion of a named account. New Users Domain/Data/Api projects and tests under tests/TravelTracker.Users.Tests are assigned to the corresponding layer agents; frontend and solution wiring are handled by the coordinating agent.

The account is identified by its validated Auth0 issuer/subject. Bootstrap additionally requires an operator-owned secret, configured target verified email, live Auth0 userinfo with matching subject, and a previously unused durable bootstrap record. The target address and credentials live in local configuration, not source. PostgreSQL owns user roles and bootstrap audit state. A global transactional lock prevents concurrent first-admin claims. Completing bootstrap never provides a general self-promotion route.

Routes: Users health, authenticated current-user read and first-admin POST, plus a service-token-protected access lookup for the existing Attractions consumer contract. Full profile CRUD and status-management remain separate; the role-management extension is documented below. Service endpoints use HTTPS. Missing identity, configuration, verification or dependencies fail closed.

The frontend shows a one-time setup page and current role; bootstrap credentials stay on the server. Authentication continues through the existing OIDC session and the Users API validates the access token independently. API permission grants and Users administrator status remain distinct.

Verification includes pure domain tests, real bearer bootstrap rejection/success tests, isolated PostgreSQL bootstrap race/restart tests, UI access/failure tests and independent read-only security review. The live first-administrator promotion was confirmed from Users after the target account signed in.

## Local configuration and operation

The Users host runs at `https://localhost:5101`; use a trusted localhost development certificate. Configure its user-secrets (`traveltracker-users`) with `Authentication:Bearer:Authority`, `Issuer`, `Audience`, `ConnectionStrings:Users`, `Database:ApplyMigrations=true`, `Bootstrap:Email`, `Bootstrap:Issuer`, `Bootstrap:Secret` and the separate `UsersAccess:ServiceToken`. Secrets must have at least 32 characters; generate unpredictable values and do not commit them. Role changes are never selected from request bodies or unverified email text.

Configure the frontend user-secrets with `Services:Users=https://localhost:5101` and `AdminBootstrap:Secret` matching the operator secret. Open `/admin-setup`, sign in as the selected verified account, then choose **Make this account administrator**. The frontend sends the credential only to the configured HTTPS bootstrap path and never renders it. Read the confirmed role from `/api/v1/users/me`; do not infer success from a clicked button. Once completed, remove/disable the bootstrap secrets. The durable marker prevents reuse even if the administrator is later demoted or deleted.

For Attractions, set its existing `Users:AccessEndpoint` to `https://localhost:5101/api/v1/users/access` and `Users:ServiceAccessToken` to the separate access credential. Unknown identities resolve as deactivated/traveller and cannot access protected Attractions operations until verified profile access provisions an account. Both the API's `catalogue.write` grant and appropriate live Users standing/ownership are still required for writes; becoming an application administrator does not change Auth0 grants.

The initial bootstrap slice provided first-admin setup, current-account standing and the Attractions access contract. The follow-up below adds administrator user listing and role changes; full profile CRUD and status management remain separate. Local account data is durable PostgreSQL data outside git.

## Review and test evidence

Independent read-only review found no Critical/High issues. Its one Medium finding (deactivated profile disclosure) was fixed and covered by a real-bearer regression. Tests cover verified email and subject mismatch, missing/wrong credentials, rejected JWTs, HTTPS, no provider-response redirect, permanent bootstrap consumption, concurrency, replay without re-elevation, deletion/demotion, atomic audit rollback, and frontend secret handling.

### Completed local verification

Final build: **0 warnings, 0 errors**. Full solution tests with an isolated PostgreSQL instance: **459 passed, 0 failed, 0 skipped** (API89, Attractions184, frontend146, Users40). Users includes22 bearer/bootstrap HTTP tests,13 domain cases and5 PostgreSQL cases. The initial PostgreSQL timestamp precision issue was fixed by normalizing to database microseconds before persistence.

The live target account completed sign-in and the explicit setup action. The website confirmed **Administrator access confirmed** with the configured account. Bootstrap credentials were then disabled in local user-secrets; the permanent database marker and audit retain the operation. Auth0 catalogue write grants are independent and were not changed by this role assignment.

The verified local runtime uses website port 5100, Users HTTPS port 5101 and a dedicated PostgreSQL loopback instance. Runtime credentials and the target email remain outside source control. The owner requested these follow-ups be included in PR #75 alongside the attraction workspace.

## All users administrator panel

The owner approved listing TravelTracker accounts and promoting/demoting administrators. Work is assigned to Users.Data (durable role mutation and audit), Users.Api (authenticated public contracts and live account-role authorization) and the Blazor frontend (All users tab, confirmation/reason, refresh and safe state handling).

`/administrator` lists application accounts only for an active persisted administrator. Auth0 tenant identities are not fetched through a Management API; a user becomes a TravelTracker account after a validated sign-in profile read verifies its provider identity and email. First-use provisioning always creates a traveller and never overwrites an existing role/status.

GET `/api/v1/users` returns public userId/email/role/status only. PATCH `/api/v1/users/{id}/role` accepts exactly role, expectedRole and a trimmed1–500 character reason. Authentication uses the existing valid audience and catalogue.read session grant; the additional authority for account administration is the live Users administrator role, not catalogue.write or token role claims. This does not change catalogue API permission checks.

Every list and mutation checks live standing. Role changes share the PostgreSQL transaction advisory lock with bootstrap, recheck the actor, verify the last-observed target role, protect the last active administrator, and write actor/target/old/new/reason audit evidence atomically. Same-role or stale requests return conflict without a new audit. Nonactive accounts cannot be promoted. Self-demotion is allowed only when another active administrator remains; subsequent panel reads are forbidden.

The UI requires a confirmation and reason, disables duplicate submissions, preserves input on failure, and announces only confirmed results. It never performs role changes automatically while rendering or testing a live user session.

### Administrator Panel validation

Final solution build: **0 warnings, 0 errors**. Full suite with isolated PostgreSQL: **503 passed, 0 failed, 0 skipped** (API89, Attractions184, frontend159, Users71). No baseline tests were removed. The sign-out regression now captures the account-page antiforgery token before resetting its recorded request, because the account page legitimately checks the live administrator role; it still proves logout and subsequent protected reads send no new backend requests.

New coverage includes real-JWT administrator/nonadministrator/standing rules, verified ordinary-user provisioning with spoofed roles ignored, atomic audited promotion/demotion, last-active-admin concurrent demotions, stale/no-op refusal, reason/unknown-field validation, audit rollback, role persistence, self-demotion authority loss, UI confirmation and duplicate suppression, error input retention, and immediate removal of rendered personal data on account changes. Independent read-only review has **0 remaining actionable findings** after correcting service outage wording.

The live audit migration adds actor, action and previous/new role columns without changing existing account roles. No real users are promoted or demoted as part of rendering or checking this feature.

Live check: the signed-in administrator opened `/administrator`, the **All users** tab displayed the current application account with active administrator role, and its demotion action was disabled with the last-active-administrator explanation. The burger menu exposed both Attractions and Administrator Panel. No live role changes were performed during verification. Other identity-provider accounts appear only after verified TravelTracker registration/sign-in. The website and Users service were restarted with the new migration applied.

### Live attraction write access

With explicit owner approval, the Auth0 API's missing `catalogue.write` permission was defined and added to the frontend's user-delegated grant. The frontend requests `openid profile email catalogue.read catalogue.write` through local user-secrets. A fresh hosted sign-in and consent displayed **Create attraction**; opening it displayed the editable form, coordinate picker entry point and duration controls. This verifies granted-scope UI availability, not a saved live attraction: no sample attraction was submitted. Administrator status and OAuth catalogue permissions remain separate checks.
