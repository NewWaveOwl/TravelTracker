# M4-16 threat model and security evidence

**Issue:** [#112](https://github.com/NewWaveOwl/TravelTracker/issues/112), blocked by #111 (M4-15).
**Owner:** 5unekku. **Milestone:** Milestone 4: Security & Usability.
**Recorded:** 2026-11-23 against commit `a4f30f9` (the #111 cross-device picture tests).
**Status:** the threat model and test evidence below are recorded from the code and from one local run. The delivery steps and the review record are not complete. Open items are listed at the end; none of them is claimed as resolved.

Identity is built in. The Users API stores a PBKDF2 hash per account in its own PostgreSQL database and signs ES256 access tokens. Other APIs are resource servers that verify those tokens with one configured public key. No external identity provider is used. Supabase is only an optional PostgreSQL host. Details are in [authentication](../authentication.md) and [api](../api.md).

## Threat model

Each row names the control that enforces the mitigation and the test that proves it. Test names are exact. Residual risk is what remains after the control.

### Authentication

| Threat | Mitigation | Control | Test | Residual risk |
| --- | --- | --- | --- | --- |
| Password guessing against `/api/v1/auth/login` | Frontend fixed window of 10 requests per minute per remote IP on its own `/auth/login` and `/auth/register` form posts | `src/TravelTracker.Frontend/Auth/PasswordAuthEndpoints.cs` (`AddPasswordAuth`, `AuthLimit`) | `tests/TravelTracker.Frontend.Tests/PasswordSignInTests.cs`, `ExcessiveCredentialSubmissionsAreRateLimitedBeforeCallingUsers` | The Users API itself has no rate limit and no lockout. A client that calls the Users API directly is not limited. The frontend limiter is in-process and per IP. |
| Offline cracking of a stolen password database | PBKDF2 through ASP.NET Core `PasswordHasher` with `IterationCount = 1 << 18`; only the hash is stored; older hashes are upgraded at the next successful login | `src/TravelTracker.Users.Api/Controllers/AuthController.cs` (`PasswordIterations`, `Hasher`, `SuccessRehashNeeded` branch) | `tests/TravelTracker.Users.Tests/PasswordAuthTests.cs`, `Hashes_UseTheConfiguredIterationCount_AndLoginUpgradesOlderHashes`; `CredentialPersistenceTests.UpdateHashReplacesOnlyTheHashAndAccountsWithoutAPasswordAreNotFound` | Passwords need only 6 characters by design. A leaked hash of a weak password is still guessable offline. |
| Learning which usernames have accounts through login | Unknown usernames are checked against a dummy hash, and both unknown and wrong passwords answer `401` | `AuthController.Login`, `DummyHash` | `PasswordAuthTests.cs`, `Login_RefusesWrongPasswordAndUnknownUsernameIdentically` | Registration is not uniform. A duplicate username answers `409` with "That username is already taken.", so registration reveals which usernames exist. Login also answers `403` for a banned or deactivated account only after the password is verified. |
| Token forgery | ES256 signature checked against the single configured public key; `RequireSignedTokens`, `ValidAlgorithms` allowlist, `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime` and `RequireExpirationTime` | `src/TravelTracker.Security/Authentication/BearerAuthenticationExtensions.cs` | `tests/TravelTracker.Attractions.Api.Tests/AuthenticationTests.cs`, `Token_IsAcceptedWhenItsIssuerMatchesTheConfiguredIssuer`; the same suites in Transport, Trips, Settings, Accommodations and Users cover invalid signatures, wrong issuer and audience, and expired tokens | Anyone holding the private signing key can mint any subject and any scope. See the single-key risk below. |
| Stale login after an account is banned | Live standing is checked against the Users service on each protected request, not taken from the token | The standing lookup in each API (the production class was not located by file name in this pass) | `tests/TravelTracker.Attractions.Api.Tests/PrivacyPersistenceTests.cs`, `LiveStandingChanges_ImmediatelyRestrictBannedAndDeactivatedAccounts`; `tests/TravelTracker.Settings.Api.Tests/SettingsHttpTests.cs`, `BannedAccount_CanReadOwnSettingsButCannotReplaceThem` | A token that is still inside its lifetime can still read data that its owner may read; standing only restricts writes and private access on each protected call. |

### Access control

| Threat | Mitigation | Control | Test | Residual risk |
| --- | --- | --- | --- | --- |
| Caller uses a scope they were not granted, or a scope that only resembles one | Exact, case-sensitive scope matching over space-separated or repeated claims; no implication between scopes | `src/TravelTracker.Security/Authentication/ScopePolicies.cs` (`HasScope`) | `tests/TravelTracker.Attractions.Api.Tests/AuthenticationTests.cs`, `Read_AcceptsRepeatedScopeClaims`; `tests/TravelTracker.Users.Tests/UserProfileApiTests.cs`, `ReadAndWriteScopesAuthorizeOnlyTheirOwnOperations` | A missing or malformed scope claim grants nothing, which is the intended behavior. The fallback policy still requires `catalogue.read` for endpoints without a policy. |
| Another traveller reads or changes a private record by guessing its ID | Owner comes from the validated `sub`; another owner's private record returns the same `404` as an absent record | `TravelTracker.Accommodations.Api` and `TravelTracker.Trips.Api` controllers and services | `tests/TravelTracker.Accommodations.Api.Tests/AccommodationHttpTests.cs`, `PrivateRecord_IsAbsentFromAnotherUsersSearchAndDetail`; `tests/TravelTracker.Trips.Api.Tests/TripDetailTests.cs`, `Detail_AnotherUsersTrip_Returns404_LikeAnAbsentTrip` | Owner checks depend on each service's own code paths. Not every route was re-audited in this pass. |
| Administrator reads another person's private data | No administrator bypass on private records or trips | Same as above | `TripDetailTests.cs`, `Detail_AdministratorWithoutOwnership_Returns404`; `AccommodationHttpTests.cs`, `SharedRecord_AdminCanCreateAndActiveReaderCanSearchAndView` | None recorded beyond the account checks above. |
| Traveller creates shared catalogue data | Shared writes need an active administrator, checked against live Users standing | Attractions and Accommodations write paths | `tests/TravelTracker.Attractions.Api.Tests/PrivacyPersistenceTests.cs`, `SharedPlace_RequiresAdministratorForCreationAndEditing`; `AccommodationHttpTests.cs`, `Traveller_CannotCreateSharedRecordAndNothingIsPersisted` | Transport and Route Point rule changes are recorded in [M4-02 plan](M4-02-plan.md) as approved and implemented; they were not re-verified in this pass. |
| Client supplies owner, role or status in a body or header | Identity fields are rejected or ignored; the subject always comes from the validated token | `CredentialsRequest` with `JsonUnmappedMemberHandling.Disallow`; `UsersController.CurrentIdentity()` | `AccommodationHttpTests.cs`, `Create_ClientCannotSupplyIdentityOwnershipRoleOrStatus` and `Create_IdentityHeaderCannotChangeValidatedOwner`; `tests/TravelTracker.Users.Tests/UserProfileApiTests.cs`, `SuppliedIdentityHeadersAndQueryIdCannotReadOrChangeAnotherProfile` | None recorded. |
| Administrator account listing or role change by a non-administrator | `users.admin` scope is required, and the repository rechecks live administrator status in the same operation | `UsersController.List` and `ChangeRole`; `EfUserRepository` | `tests/TravelTracker.Users.Tests/UserAdministrationApiTests.cs`, `RepositoryListDenialIsAuthoritativeDespiteProviderAdministratorClaim` | Every traveller token carries `users.admin` (see the finding below). The live database check is the only real gate on these routes. |
| A Users outage lets a stale or default standing through | Protected calls fail closed | Each API's standing client | `tests/TravelTracker.Settings.Api.Tests/SettingsHttpTests.cs`, `UsersUnavailable_FailsProtectedOperationsClosed`; `tests/TravelTracker.Trips.Api.Tests/TripDetailTests.cs`, `Detail_UsersDown_Returns503`; `tests/TravelTracker.Attractions.Api.Tests/PrivacyPersistenceTests.cs`, `UnavailableUsers_FailsClosedWithoutPersistingWritesButHealthIsPublic` | Health stays public by design. |
| Internal backend lookup used by a caller that is not a backend | `users.access` plus an allowlisted service subject; the service token is issued only with the operator secret | `UsersProgram` (`AllowedServiceSubjects`), `AuthController.ServiceToken` | `tests/TravelTracker.Users.Tests/PasswordAuthTests.cs`, `ServiceToken_NeedsTheOperatorCredentialAndGrantsOnlyUsersAccess`; `UserProfileApiTests.cs`, `InternalLookupRequiresBothServiceSubjectAndExactAccessScope` | A holder of the operator secret can obtain the service token. |

### Profile data

| Threat | Mitigation | Control | Test | Residual risk |
| --- | --- | --- | --- | --- |
| Reading or changing another account's profile | `/me` routes use only the validated `(iss, sub)`; there is no user ID route on profile reads or writes | `UsersController.Me`, `UpdateMe`, `CurrentIdentity()` | `UserProfileApiTests.cs`, `SuppliedIdentityHeadersAndQueryIdCannotReadOrChangeAnotherProfile`; `MissingBearerOrBlankSubjectCannotReadOrPatch` | None recorded. |
| Personal data in logs | Log categories and failure types only; no usernames, passwords, tokens or profile text | `UsersController`, `AuthController`, `ApiHostBuilder` | `UserProfileApiTests.cs`, `ProfileOperationsKeepIdentityAndSubmittedPersonalDataOutOfLogs` | Route-level logs are used instead of paths, because paths carry IDs. |
| Address and username exposure at rest | None beyond the PostgreSQL host's own controls | `UserAccount`, `UserRecordConfiguration` | Validation only: `tests/TravelTracker.Users.Domain.Tests/UserAccountTests.cs`. No test covers encryption because none exists. | Addresses are stored in plain text. Database backups hold them. |
| No account deletion or data export | None | No delete route exists in `UsersController` | Not applicable | Missing feature. A user cannot remove their account or export data through the API. |

### Uploads and storage (profile pictures)

| Threat | Mitigation | Control | Test | Residual risk |
| --- | --- | --- | --- | --- |
| Oversized upload or upload without a declared length | `Content-Length` is checked against 1,048,576 bytes; the body read is bounded to that size plus one byte | `src/TravelTracker.Users.Api/Services/ProfilePictureUpload.cs` (`DecodeAsync`, `MaxBytes`) | `tests/TravelTracker.Users.Tests/UserPictureApiTests.cs`, `UploadWithoutContentLengthStillEnforcesTheByteLimit` | None recorded. |
| Wrong type or a file that only claims to be an image | Media type must be `image/png` or `image/jpeg`; the magic bytes must match; JPEG must end with `FFD9`; PNG chunks are walked and animation (`acTL`) is rejected; the decoder's format must match | `ProfilePictureUpload.DecodeAsync`, `ValidatePngContainer` | `UserPictureApiTests.cs`, `AnonymousCallerCannotReplaceThePicture` and the picture rejection cases | Decoding uses SkiaSharp native code on untrusted bytes. The native surface is not separately reviewed. |
| Oversized dimensions (decompression abuse) | Each side must be 1 to 512 pixels before pixels are decoded into memory | `ProfilePictureUpload.DecodeAsync` (`MaxSide`) | Covered by the picture rejection cases in `UserPictureApiTests.cs` | None recorded. |
| Location or camera metadata leaks in the stored image | Output is re-encoded from pixels as a metadata-free PNG after EXIF orientation is applied | `ProfilePictureUpload.Orient`, PNG re-encode | `UserPictureApiTests.cs`, `ExifOrientationSixRotatesTheSavedPixelsClockwise` | None recorded. |
| Another account reads or replaces a picture by guessing its owner's ID | Route is `me/picture` only; owner comes from the token | `UsersController.ReadPicture`, `ReplacePicture` | `UserPictureApiTests.cs`, `GuessedAccountIdsCannotReadOrReplaceAnotherAccountsPicture`; `OnlyTheOwnerCanReadAndAWriteScopeIsRequiredToReplace` | None recorded. |
| Banned or deactivated account changes its picture | Replace requires an active account; reads are refused after deactivation | `ReplacePicture` status check | `UserPictureApiTests.cs`, `BannedAccountCanReadButCannotReplaceAndDeactivationBlocksRead` | None recorded. |
| Storage outage loses the existing picture | Failed write leaves the previous row in place; outage answers `503` | `profiles.ReplacePictureAsync`; global exception handler in `UsersProgram` | `UserPictureApiTests.cs`, `StorageOutageAnswersServiceUnavailableAndKeepsThePreviousPicture` | None recorded. |
| Cached or shared image | `Cache-Control: private, no-store` on picture routes; `X-Content-Type-Options: nosniff` on reads | `UsersController.ReadPicture`, `ReplacePicture` | Not checked by a named test in this pass | Header behavior was read from code, not from a test. |
| Browser redirect or leaked URL used to fetch a picture | The client does not follow upload `Location` values; the image is read from the fixed route with the bearer token | Frontend profile picture client | `tests/TravelTracker.Frontend.Tests/ProfilePictureClientTests.cs`, `UploadLocationCannotRedirectTheSubsequentProtectedRead`; `StreamedPictureWithoutContentLengthStopsAtTheResponseCap` | None recorded. |
| Picture from an old session uploaded into a new account | Client binds the file selection to the session owner | Frontend profile picture client | `ProfilePictureClientTests.cs`, `FileSelectionBoundToAnOldSessionCannotUploadAsANewOwner` | None recorded. |
| Picture visible to another device signed in as the same account | The second device uses its own token and reads the stored picture; tokens are not copied between devices | `UserPictureApiTests.cs` | `SecondDeviceWithItsOwnTokenReadsTheReplacedPicture` (added in #111, commit `a4f30f9`) | This test runs against the test hosts, not deployed devices. |

Ticket PDF and text uploads in the Trips service were not reviewed in this pass. Their size, page and parser limits are not recorded here.

### Secrets

| Threat | Mitigation | Control | Test | Residual risk |
| --- | --- | --- | --- | --- |
| Private signing key committed to the repository | Key is read from `Authentication:Issuer:PrivateKey` only; the repository has no private key; shipped settings hold a placeholder public key only | `TokenIssuer`; `src/*/appsettings*.json`; `scripts/StartDev.cs` (generates an in-memory ECDSA P-256 key per launch) | `tests/TravelTracker.Users.Tests/PasswordAuthTests.cs`, `WithoutASigningKey_AnswersUnavailableInsteadOfThrowing`; `tests/TravelTracker.Attractions.Api.Tests/AuthenticationTests.cs`, `Production_MissingProviderConfigurationStillFailsStartup` | Anyone who obtains the private key can mint tokens for any subject and scope. Keys are not rotated. |
| Operator secrets weak or leaked into logs | Secrets must be at least 32 characters; comparison is constant-time over SHA-256 digests; failures log the reason category only | `SecretVerifier` (`Matches`); `AuthController.ServiceToken`; `BootstrapService` | `tests/TravelTracker.Users.Tests/BootstrapApiTests.cs`, `BootstrapIsDisabledWithoutStrongOperatorConfiguration`; `PasswordAuthTests.cs`, `ServiceToken_NeedsTheOperatorCredentialAndGrantsOnlyUsersAccess` | Secrets are plain configuration values. Their storage depends on the deployment. |
| Bearer values echoed in error responses | `IncludeErrorDetails = false`; the validation handler logs only the failure type | `BearerAuthenticationExtensions` | `AuthenticationLogCapture` helpers exist in the Attractions and Transport suites; no test was checked that asserts token absence from logs | None recorded beyond the unchecked log assertion. |
| Frontend leaks the access token to browser script | Token is in the HttpOnly authentication cookie, not in page script | `PasswordAuthEndpoints.StartSessionAsync` | `tests/TravelTracker.Frontend.Tests/PasswordSignInTests.cs`, `ValidSignInCreatesAnHttpOnlySessionWithoutExposingTheToken` | The cookie carries the token as a claim. ASP.NET Core cookie protection is relied on for its encryption; the key ring location was not checked. |

### Device and session lifecycle

| Threat | Mitigation | Control | Test | Residual risk |
| --- | --- | --- | --- | --- |
| A token is used after the session should be over | Tokens expire after 60 minutes by default; the frontend cookie ends with the token and is not persistent; sliding expiration is off | `TokenIssuer` (`LifetimeMinutes`, default 60); `PasswordAuthEndpoints` cookie options | `tests/TravelTracker.Frontend.Tests/SessionTests.cs`, `ExpiryRevokesTheSessionInEveryCircuit`; `ProtectedClientTests.cs`, `RefusesWithoutSendingWhenTheTokenHasExpired` | A token that has already been copied stays valid until its expiry. The Users API has no revocation endpoint. |
| Sign-out in one tab leaves another tab signed in | Local session registry revokes the session ID for every circuit | `SessionRegistry` (frontend) | `SessionTests.cs`, `RevocationNotifiesOtherCircuitsButDoesNotSignOutAnotherSession`; `PasswordSignInTests.cs`, `LogoutAlsoRejectsASecondTabsPreviouslyIssuedCookie` | Revocation is local to the frontend process. |
| Frontend restart keeps old sessions alive | Registry is in-process; an unknown session after restart is anonymous | `SessionRegistry` | `SessionTests.cs`, `AnUnknownSessionAfterRestartIsAnonymous` | The registry does not span several frontend instances. |
| Rejected token keeps being used | A rejected token revokes every circuit and blocks later calls | Frontend protected client | `SessionTests.cs`, `ARejectedTokenRevokesEveryCircuitAndBlocksLaterCalls` | None recorded. |
| Service token outlives its need | Service tokens are valid for `Authentication:Issuer:ServiceLifetimeMinutes`; the code default is 60 minutes. The `scripts/StartDev.cs` launcher sets 720 minutes (12 hours). There is no refresh endpoint. | `TokenIssuer.ForService`; `scripts/StartDev.cs` line 196 | `PasswordAuthTests.cs`, `ServiceToken_NeedsTheOperatorCredentialAndGrantsOnlyUsersAccess` | Backends must request a new service token after expiry. How often the backends do this was not verified. |

## Known residual risks

These are the risks the issue already lists, checked against the code.

1. **No rate limit or lockout on `/api/v1/auth/*` in the Users API.** Verified: no `RateLimiter` and no lockout exist in `src/TravelTracker.Users.Api`. The frontend limit covers only its own forms.
2. **Service token has no refresh.** Verified: there is no refresh route. The lifetime is 60 minutes in code and 12 hours in the local launcher. The issue text said 12 hours. That is the launcher value, not the code default.
3. **One shared public key.** Verified: `BearerSettings` holds one `PublicKey` for every API. Whoever holds the Users private key can mint a token for any subject and scope.
4. **No password reset or account recovery.** Verified: no reset route exists in `src/`.
5. **No email verification.** Verified: `UserAccount` has no email field. Earlier migrations carried email columns, which were removed in the username sign-in change (commit `1d4b5d4`).
6. **Passwords need only 6 characters, by design.** Verified: `AuthController.MinimumPasswordLength = 6` and the frontend check `password.Length < 6`. Commit `2d43e2a` made this the only password rule.

Findings beyond that list:

7. **Every traveller token carries `users.admin`.** Verified: `TokenIssuer.TravellerScopes` includes `users.admin`, and `ForAccount` issues all of them. The [M4-02 plan](M4-02-plan.md) says ordinary traveller tokens should exclude this scope. The code does not match that plan. The live administrator check in the repository is the only real gate on account listing and role changes. This needs a human decision.
8. **Username enumeration on registration.** Verified: a duplicate username answers `409`. See the authentication table.
9. **No server-side revocation of an issued token.** A signed-out or banned user's token stays valid until its expiry, except where a standing check on a protected call refuses it.
10. **No account deletion or data export.** Verified: no delete route exists in `UsersController`.
11. **Picture bytes and addresses sit in PostgreSQL.** Verified: `UserPictureRow.Content` is a byte array in the Users database. Backups contain them.

## Test results

Run on 2026-11-23 (UTC start 09:44:31, build finished 09:44:48, test run finished shortly after) from this worktree at commit `a4f30f9`.

Commands:

```sh
dotnet restore TravelTracker.slnx -nologo
dotnet build TravelTracker.slnx --configuration Release --no-restore -nologo
dotnet test TravelTracker.slnx -nologo --configuration Release --no-build --no-restore --filter "Category!=PostgreSQL"
```

The build result was `0 Warning(s)`, `0 Error(s)`, and elapsed 7.80 seconds. The restore succeeded. The `--configuration Release --no-build --no-restore` flags were added to match the repository's documented validation commands. The test run exited `0`.

Passed counts per project (all `Failed: 0, Skipped: 0`):

| Test project | Passed |
| --- | --- |
| TravelTracker.Accommodations.Domain.Tests | 10 |
| TravelTracker.Settings.Domain.Tests | 34 |
| TravelTracker.Api.Tests | 78 |
| TravelTracker.Trips.Domain.Tests | 81 |
| TravelTracker.Trips.Data.Tests | 19 |
| TravelTracker.Users.Domain.Tests | 25 |
| TravelTracker.Accommodations.Api.Tests | 54 |
| TravelTracker.Settings.Api.Tests | 79 |
| TravelTracker.Transport.Api.Tests | 132 |
| TravelTracker.Attractions.Api.Tests | 188 |
| TravelTracker.Users.Tests | 153 |
| TravelTracker.Trips.Api.Tests | 197 |
| TravelTracker.Frontend.Tests | 569 |
| **Total** | **1619** |

Not run or not counted:

- `TravelTracker.Accommodations.Data.Tests`, `TravelTracker.Settings.Data.Tests` and `TravelTracker.Users.Data.Tests` reported "No test matches the given testcase filter", so they contribute 0 non-PostgreSQL tests.
- PostgreSQL-category tests were not run. `TRAVELTRACKER_TEST_POSTGRES` was not configured in this session.
- `TravelTracker.Journey.Tests` is not in `TravelTracker.slnx` and was not part of this run.
- The `#111` cross-device test (`SecondDeviceWithItsOwnTokenReadsTheReplacedPicture`) ran as part of `TravelTracker.Users.Tests`. Its pass is included in the 153.
- Real deployed devices, live TLS and key distribution were not tested.

The M4-02 record reported VSTest testhost connection timeouts in an earlier environment. Those timeouts did not occur in this run.

Logs: `/var/tmp/traveltracker-m4-16/test.log` and `/var/tmp/traveltracker-m4-16/build.log` are local outputs of this run. They are not committed.

## Manual notes (not run)

These steps were written from `scripts/StartDev.cs`. They were not run in this pass.

1. Install podman or docker. `scripts/StartLocalDb.cs` starts `postgres:alpine` with the `--local-db` flag.
2. From the repository root, run:

   ```sh
   dotnet run --file scripts/StartDev.cs -- --local-db --apply-migrations
   ```

3. Open the frontend at `http://localhost:5100`. The Users API is at `https://localhost:5101`.
4. Register on the sign-in page, open Account, then Edit profile, and upload a PNG or JPEG under 1 MiB and 512 pixels. Sign out and sign in again.
5. Open a second browser profile, sign in with the same account and confirm that the picture appears.
6. Check that `GET /health` answers `200` on each API port listed in `scripts/StartDev.cs` without a token.

`StartDev.cs` generates a throwaway ECDSA key pair on every launch and sets the service token lifetime to 720 minutes.

## Board, dependencies, commits and PRs

Source: `gh issue list` and `gh pr list` on 2026-11-23. Reviewer names are GitHub logins from `gh pr view`. Where a field was not available, it is marked "not recorded".

| Issue | Title (short) | State | Assignee | PR | Merge commit | Reviewer |
| --- | --- | --- | --- | --- | --- | --- |
| #97 | M4-01 bearer validation | CLOSED | ChristelEdee | #129 (merged) | `227e542` | NewWaveOwl |
| #98 | M4-02 access policies | CLOSED | ChristelEdee | #132 (merged) | `3af39f9` | NewWaveOwl |
| #99 | M4-03 profile and credential updates | CLOSED | ChristelEdee | #134 (merged) | `d666a26` | NewWaveOwl |
| #100 | M4-04 Supabase migration verification | OPEN | ChristelEdee | #135 (merged, no closing link) | `8d3429b` | NewWaveOwl |
| #101 | M4-05 environment and secrets | CLOSED | ChristelEdee | #136 (merged) | `2174efa` | NewWaveOwl |
| #102 | M4-06 picture upload validation | CLOSED | ChristelEdee | #137 (merged) | `7b48321` | NewWaveOwl |
| #103 | M4-07 private picture storage | CLOSED | not recorded | not recorded | not recorded | not recorded |
| #105 | M4-09 trip save without categories | CLOSED | NewWaveOwl | #138 (merged) | `de394bd` | ChristelEdee |
| #109 | M4-13 PDF ticket extraction | CLOSED | NewWaveOwl | #138 (merged) | `de394bd` | ChristelEdee |
| #110 | M4-14 pasted ticket text | OPEN | 5unekku | not recorded | not recorded | not recorded |
| #111 | M4-15 cross-device picture checks | OPEN | 5unekku | not recorded | local commit `a4f30f9` only | not recorded |
| #112 | M4-16 this issue | OPEN | 5unekku | this document | not recorded | not recorded |
| #119 | M4-23 Save trip and Ctrl+S | CLOSED | NewWaveOwl | #138 (merged) | `de394bd` | ChristelEdee |
| #121 | M4-25 draggable panels | CLOSED | NewWaveOwl | #133 (merged) | `6302ab9` | ChristelEdee |
| #104, #106, #107, #108, #113, #114, #116, #120, #122, #126, #127 | Open M4 work | OPEN | see issue list | not recorded | not recorded | not recorded |
| #115, #117, #118, #123, #124, #125 | Closed UI work | CLOSED (2026-11-13, same second range as PR #130 merge) | NewWaveOwl | PR not confirmed; PR #130 merged 2026-11-13T21:42:52Z | not recorded | not recorded |

Other M4 issues in the #97 to #131 range are listed in `gh issue list` and are not repeated here. Open PR #139 (`feature/public-nominatim`, NewWaveOwl, review required) is not linked to an issue in this pass.

Dependencies:

- #111 is blocked by #97, #98, #102 and #103, as its issue body states. #97, #98 and #102 are closed and merged. #103 has no PR in the search, and its state is not recorded here.
- #112 is blocked by #111. The issue body states this, and the board shows #111 as OPEN.
- The user's brief says #111 is done. On GitHub, #111 is still OPEN, and its test commit `a4f30f9` exists only on the local branch `worktree-agent-a8fb5fc4fbca0af8b`. No remote branch contains it.

Review: each merged PR above was reviewed by a teammate login. Resolved feedback text is not recorded here. The review bodies were not read in this pass.

## Delivery requirement

The issue requires:

- A branch named `feature/milestone-4-security`, cut from the usual integration branch.
- Retention of the milestone branch after merge.
- An Azure DevOps milestone PR with a Lea link.

Status:

- No `feature/milestone-4-security` branch exists locally or on `origin`.
- The repository has no Azure DevOps or Lea references in `README.md` or `CONTRIBUTING.md`.
- The integration branch is `milestone_4` for recent PRs and the [profile picture UI doc](profile-picture-ui.md). PR #128 targeted `milestone_3` earlier, and `origin/milestone_3` still exists.

## Open items for the human

These are not resolved. Each needs a decision or a fix before submission.

1. Confirm the integration branch and the PR base for submission: `milestone_4` (recent PRs) or another branch.
2. Resolve the delivery platform difference: the issue names Azure DevOps and a Lea link, while the work is on GitHub. Decide how the submission links to it.
3. Create or confirm `feature/milestone-4-security`, and retain the milestone branch after merge.
4. Decide on `users.admin` in every traveller token (finding 7). Either remove it from traveller tokens or update the M4-02 plan and [authentication](../authentication.md).
5. Decide whether the Users API needs rate limiting or lockout, or whether the risk is accepted as residual.
6. Decide whether to add account deletion and data export, which are missing.
7. Confirm #103 has a PR, or record its state.
8. Close #100 or record why it stays open, given PR #135 merged.
9. Push the #111 commit (`a4f30f9`) and its branch for review, and merge it through a PR. The #111 issue is still OPEN.
10. Run the PostgreSQL test category against an isolated database, and run the manual notes above on a live stack.
11. Record the teammate review feedback for each PR in this document.

## Claims not verified in this pass

- Ticket PDF and text upload limits and parser behavior in the Trips service.
- Whether the backends refresh the service token before it expires.
- Whether the frontend cookie protection key ring is persisted.
- The `Cache-Control` and `nosniff` headers on picture responses were read from code, not tested.
- Owner checks on every route of the Accommodations, Attractions, Settings and Trips services. Only the routes named in the tables were checked.
- The Transport role rules recorded in the M4-02 plan.
- Real deployment TLS, key distribution and key rotation.
