# Issue #34 implementation record

The user approved the file-by-file plan in chat on 2026-10-24. Work is based on `origin/milestone_3` (`5bcf078`), the repository's current default branch, on `establish-bearer-auth-34`. The existing unrelated local branch was preserved.

## Approved plan and resulting files

| Files | Implementation |
| --- | --- |
| `src/TravelTracker.Api/TravelTracker.Api.csproj`, `Program.cs` | Framework JWT bearer package, composition registration and authentication-before-authorization middleware; Swagger registration. |
| `src/TravelTracker.Api/Authentication/BearerSettings.cs` | Environment-owned authority, issuer, audience and exact scope claim name; startup validation. |
| `src/TravelTracker.Api/Authentication/BearerAuthenticationExtensions.cs` | Discovery-based signing keys, strict signature/issuer/audience/lifetime validation, validated subject, fallback protection and named policies. |
| `src/TravelTracker.Api/Authentication/ScopePolicies.cs` | Exact, case-sensitive space-separated/repeated scope matching, separate read/write permissions. |
| `src/TravelTracker.Api/Authentication/BearerSecurityOperationFilter.cs` | Per-operation bearer requirement, permission and 401/403 documentation; public health exception. |
| Resource controllers and `HealthController.cs` | Explicit read/write policies on every implemented resource action and anonymous health. |
| `tests/TravelTracker.Api.Tests/Support/ApiFactory.cs`, `IdentityProbeController.cs`, `AuthenticationTests.cs` | Isolated real RSA-signed JWTs, public-only static test discovery metadata, HTTP security matrix, identity and configuration checks, OpenAPI verification. No production test bypass. |
| `appsettings.json`, `requests.http`, README, API/Domain/Data/contracts docs, `docs/authentication.md` | Blank deployment settings, token placeholders, accurate capabilities, configuration and service adoption recipe. |

## Scope and limitations

No Data or Domain production code changed. No provider was selected and no credentials were introduced. The approved configurable pattern uses a selected scope claim name (`scope` by default, optionally e.g. `scp`), without claiming compatibility with an undecided provider. Account-standing, ownership and issuer/subject-to-Users mapping remain related service work. The frontend does not yet exist; a frontend sign-in journey and real deployed-provider token/discovery verification cannot be claimed.

Baseline: 62 tests passed, zero failed or skipped. This local record does not claim team-member approval, an independent agent review, remote CI success, or a merged PR.

## Validation evidence (2026-10-24)

- `dotnet restore TravelTracker.slnx`: succeeded.
- `dotnet build TravelTracker.slnx --configuration Release --no-restore`: succeeded, zero warnings and zero errors.
- `dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --logger "trx;LogFileName=issue-34.trx"`: 112 total, 112 passed, zero failed, zero skipped (62 existing cases plus 50 authentication/configuration/OpenAPI cases). The ignored report is under the test project's `TestResults/`.
- Live Kestrel check on loopback with example authority/issuer/audience: anonymous health returned 200; anonymous catalogue and malformed bearer requests returned 401. Development Swagger generated its bearer scheme successfully, and the test-only identity controller was absent from production routes. The process was stopped after checking. No valid provider-issued token was available for a live success check; successful signed-token requests are proven through the real bearer handler in the isolated test host.
- `git diff --check`: passed. Reviewed all resource action annotations, middleware ordering, exact scope matching, strict token settings, layer boundaries, test-only key isolation, blank deployment settings and documentation consistency. No generated build/test outputs are included in the change.
