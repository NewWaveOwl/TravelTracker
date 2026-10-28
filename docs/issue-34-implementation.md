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
| `tests/TravelTracker.Api.Tests/Support/ApiFactory.cs`, `DiscoveryBackchannelHandler.cs`, `IdentityProbeController.cs`, `AuthenticationTests.cs` | Isolated real RSA-signed JWTs, public-only HTTP discovery/JWKS responses through the framework metadata retriever, HTTP security matrix, identity/configuration checks and OpenAPI verification. No production test bypass. |
| `appsettings.json`, `appsettings.Development.json`, `requests.http`, README, API/Domain/Data/contracts docs, `docs/authentication.md` | Blank production settings, Development-only identity placeholders without keys or bypasses, token placeholders, accurate capabilities, configuration and service adoption recipe. |

## Scope and limitations

No Data or Domain production code changed. No provider was selected and no credentials were introduced. The approved configurable pattern uses a selected scope claim name (`scope` by default, optionally e.g. `scp`), without claiming compatibility with an undecided provider. Account-standing, ownership and issuer/subject-to-Users mapping remain related service work. The frontend does not yet exist; a frontend sign-in journey and real deployed-provider token/discovery verification cannot be claimed.

Original baseline: 62 tests passed, zero failed or skipped. Initial implementation evidence follows; the PR-review follow-up is recorded separately below. Team-member approval, deployed-provider verification and merge are not claimed.

## Validation evidence (2026-10-24)

- `dotnet restore TravelTracker.slnx`: succeeded.
- `dotnet build TravelTracker.slnx --configuration Release --no-restore`: succeeded, zero warnings and zero errors.
- `dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --logger "trx;LogFileName=issue-34.trx"`: 112 total, 112 passed, zero failed, zero skipped (62 existing cases plus 50 authentication/configuration/OpenAPI cases). The ignored report is under the test project's `TestResults/`.
- Live Kestrel check on loopback with example authority/issuer/audience: anonymous health returned 200; anonymous catalogue and malformed bearer requests returned 401. Development Swagger generated its bearer scheme successfully, and the test-only identity controller was absent from production routes. The process was stopped after checking. No valid provider-issued token was available for a live success check; successful signed-token requests are proven through the real bearer handler in the isolated test host.
- `git diff --check`: passed. Reviewed all resource action annotations, middleware ordering, exact scope matching, strict token settings, layer boundaries, test-only key isolation, blank deployment settings and documentation consistency. No generated build/test outputs are included in the change.

## PR #69 review verification and fixes (2026-10-28)

The user requested verification and fixes for the supplied review comment. Baseline at `21d7e6a`: 112 passed, zero failed or skipped. The startup issue was confirmed: empty provider settings were rejected even in Development. The environment variables were already documented, but this follow-up makes the shipped Development launch usable and puts the overrides directly in README.

| Review point | Verification and resolution |
| --- | --- |
| Plain Development launch fails | Confirmed. Development-only `.invalid` authority/issuer and sample audience now allow health and Swagger to start. They issue no tokens and add no keys or bypass. Production still requires provider configuration and has a startup-failure regression test. |
| Fallback is authentication-only | Confirmed; bare `[Authorize]` also uses a separate default policy. Both now require `catalogue.read`, tested with anonymous, unscoped, write-only and read tokens. Explicit write policies remain independent. |
| Zero skew and unrestricted algorithms | Confirmed configuration. Skew now defaults to 30 seconds and is configurable within 0–120. Algorithms default to an explicit RS256 allowlist; configured choices must be supported asymmetric JWT algorithms. Acceptance/rejection tests cover tolerance and algorithm selection. |
| Authority/Issuer mismatch and diagnostics | Exact token issuer validation was already enforced. Requiring Authority to equal Issuer would incorrectly reject legitimate providers. Instead, discovery metadata must match configured Issuer; trailing-slash/version mismatches fail with 401 and a safe server warning. A differing authority with matching metadata/token issuer is tested. Public error details stay disabled; logs contain failure categories rather than token/exception text. |
| Blank Swagger implicit permission / incorrect 403 | Confirmed blank permission description. Both implicit policies now enforce read permission, so Swagger names `catalogue.read` and 403 is reachable and documented. Regression tests cover both policy forms. |
| Static metadata skips discovery | Confirmed. Replaced the static configuration manager with an HTTP backchannel stub. The framework now retrieves/parses authority discovery JSON and JWKS; tests assert those URLs and validate resulting tokens. Live provider TLS/availability/rotation remain unverified. |
| Interleaved controller usings | Confirmed and reordered the three affected controllers' imports. |

Validation: solution restore passed; Release build passed with **0 warnings and 0 errors**. Focused authentication suite: **77 passed**. Full suite: **139 passed, 0 failed, 0 skipped** (all 112 baseline cases retained plus 27 review regressions). Report: ignored `tests/TravelTracker.Api.Tests/TestResults/issue-34-review.trx`. Live `dotnet run --project src/TravelTracker.Api --launch-profile http --no-build --configuration Release` with a loopback URL override and no bearer environment variables returned health 200, anonymous catalogue 401, and a valid Swagger bearer scheme. The started processes were stopped afterward. Diff whitespace checks passed.

Repository-required read-only code-review agent reviewed the follow-up diff including the discovery helper: **0 Critical, 0 High, 0 Medium, 0 Low findings**; verdict **Ready to merge**, conditional on final checks and CI. No code was edited by the reviewer. This is agent review evidence, not team-member approval. Actual provider integration and the frontend journey remain outside what these checks prove.
