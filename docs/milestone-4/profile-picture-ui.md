# Profile picture UI

The Account UI follows the updated [profile overview](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-2028)
and [Edit profile](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-2141)
frames in **04 / Account & preferences**. The reference uses a 144-pixel circular
photo or forest initials fallback, a 24-pixel gap, and the existing shared
typography and buttons. Account photos are dynamic user content; the Figma
example name and initials are not application data.

## Integration and API dependency

The repository has no `main` branch. The actual default and PR base branch is
`milestone_4`; its latest changes at `8d3429b` were pulled without rewriting
the PR's published history. This includes the configuration/redaction updates,
the profile-picture backend and the Supabase migration verification record.

The picture contract is supplied by [PR #137](https://github.com/NewWaveOwl/TravelTracker/pull/137),
which implements the backend for [UI issue #126](https://github.com/NewWaveOwl/TravelTracker/issues/126).
The owner chose to keep that backend PR separate from the frontend implementation.
It has since merged into `milestone_4` at `7b48321`, including the final image
orientation/sampling fixes through `5953312`, and is now included by the base
pull. Deployment still requires the Users API and database migration; live
authenticated picture reads/uploads have not been verified.

| Operation | Protected Users contract |
| --- | --- |
| Read | `GET /api/v1/users/me/picture`, `users.read`, HTTPS and the validated caller's bearer token; `200 image/png`, or `404` for no picture |
| Upload/replace | `PUT /api/v1/users/me/picture`, `users.write` and an active account; raw PNG/JPEG bytes with matching media type; success `204` |
| Input limits | One static PNG or JPEG; at most 1,048,576 bytes, and each dimension at most 512 pixels |
| Rejection | `400`, `413`, `415` or concurrency `409` keeps the previous stored picture |
| Returned image | A metadata-free PNG from the API, private and non-cacheable; its normalized size can exceed the uploaded JPEG size |

The frontend uses the configured Users service and the fixed private route.
It does not follow arbitrary upload `Location` URLs, put tokens in image URLs,
introduce a public storage URL, or access a database directly. Image responses
need a separate bounded receive allowance from the upload limit. Client checks
help the user; the API remains responsible for validating decoded image data.

The client also reads recognizable PNG/JPEG header dimensions before uploading.
A photo wider or taller than 512 pixels gets local resize guidance even when its
compressed file is below 1 MiB; no PUT is sent and the confirmed picture stays
visible. Truncated or unreadable headers still go to the API for authoritative
format/decoder validation. The client neither resizes nor rewrites the photo.

## User flow and private state

The profile overview shows the confirmed photo, or initials derived from the
confirmed username. Edit profile exposes a labelled native file picker styled
as **Upload photo**. Selecting a file saves the photo immediately and
independently of text edits; cancelling text edits does not undo a confirmed
photo upload. The UI states this next to the picker.

The UI shows success only after `204`, then reads the fixed private picture
route. A failed preview refresh after that confirmation is reported separately
from a rejected upload. Rejected uploads keep the prior image visible. Banned
accounts retain permitted read access but cannot choose a new photo.

Binary requests use the existing HTTPS destination, token-revocation and
timeout boundary. Upload bytes are bounded to 1 MiB; normalized PNG reads have
an independent 2 MiB cap. Redirects and cookies are disabled. A rendered-owner
session check prevents a file chosen under an old session from being uploaded
to a replacement account. Late responses are discarded, and authentication
changes, revocation, expiry and component disposal clear private image state.
Accepted `/account` and `/account/` responses are non-cacheable and use
`Referrer-Policy: no-referrer`. The browser receives a bounded in-memory
PNG data URI, without a bearer token, filename or persistent storage reference.

## Verification

After integrating `milestone_4`, the baseline Release solution build passed
with zero warnings and errors. All 1,364 application tests passed, including
513 frontend cases; the 41 JavaScript interaction checks also passed.

UI implementation validation at `045d0cc`, before the latest base pull:

- Restore and Release solution build passed, with zero warnings and errors.
- `dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category!=PostgreSQL"`:
  1,416 discovered, executed and passed; zero failed or skipped. This includes
  565 frontend cases, 52 more than the integrated baseline.
- The four JavaScript interaction suites used by CI: 41 passed, zero failed,
  skipped or cancelled.
- `dotnet run --file scripts/TestLauncher.cs`: 22 passed, zero failed. These
  checks are offline launcher configuration tests.
- A read-only security review found no confirmed findings in the picture
  transport, authentication/session guards, lifecycle or upload recovery code.
- Live frontend HTTP checks returned non-empty `200` responses for Account,
  its trailing-slash alias and Map. Both Account forms returned `no-store`
  and `no-referrer` headers. The signed-out preview shows the existing
  unavailable-sign-in state; it does not demonstrate live picture upload.
- Whitespace and local documentation-link checks passed.

New cases cover raw request/media headers, fixed read destinations, independent
upload/read byte bounds, rejected and uncertain uploads, normalized images,
permission changes, stale reads/writes, session replacement/revocation/expiry,
independent component reloads, native file input lifetime, profile text Cancel,
and accepted Account URL forms. Controlled identity and HTTP doubles are not
evidence of a live authenticated provider or physical-device journey.

Base-pull validation against `milestone_4` at `8d3429b`:

- Restore and Release solution build passed with zero warnings and errors.
- Application suite: 1,430 discovered, executed and passed; zero failed or
  skipped. The 14 additional cases come from the merged picture API tests;
  all 565 frontend cases remain passing.
- JavaScript interaction suites: 41 passed, zero failed, skipped or cancelled.
- Offline launcher configuration suite: 22 passed, zero failed.
- Read-only integration review found no confirmed correctness or security
  regressions between the frontend and the merged picture API.
- Whitespace and changed documentation-link checks passed.

The requested cloud browser still reports an unavailable access host, so a
fresh visual/pixel comparison and rendered geometry check remain unverified.
Local PostgreSQL tests were not run because no isolated connection is
configured; CI supplies the disposable relational test database. The backend
and migration from PR #137 are now in the branch through the base pull, but
deployment and live authentication remain unverified. No live database,
Auth settings or user pictures were modified by these checks.
