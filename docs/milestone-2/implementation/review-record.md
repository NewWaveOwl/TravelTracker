# Implementation review record

Fresh read-only Copilot review: session 470e99f3-adad-4030-b5db-f689b1998333. [Original response](copilot-review.md). It changed no files and executed no tests. Codex separately executed the recorded checks and reviewed the final diff against the approved specifications and both skills. Human review of the final PR remains the normal merge step; it is not claimed to have happened.

| Finding | Disposition and evidence |
| --- | --- |
| ValidationProblem called with an unsupported dictionary overload | **Fixed** before execution: thin controllers construct ValidationProblemDetails. Both suites compile and malformed-ID/duplicate-query HTTP cases return 400. |
| Attractions test expected nine public fields | **Fixed test**: explicitly assert the eight agreed field names, rather than alter the correct API contract. Final 53 pass. |
| Broad general-purpose timestamp parsing could obscure the ISO contract | **Addressed during review**: explicit-offset suffix plus Utf8JsonReader.TryGetDateTimeOffset; UTC output and invalid timestamp cases pass. |
| Copilot suggested missing explicit coverage for unverified sourceId metadata | **Already covered**: TransportTests.Create_AcceptsSupportedPointTypeAndCoordinateBoundaries (originally RoutePointHttpTests) posts unknown GUID ...999 for each point type and verifies its unchanged return in an isolated Transport host. Live verification additionally creates a point with Attractions stopped. No duplicate test was added. |
| Existing shared Id64 build error and missing planned models | **Explicitly deferred**: outside the approved independent service scope and other owners' aggregate work. Full build is still failed; targeted success is not presented as solution success. |
| VS Code Agent-mode evidence versus actual available tool run | **Recorded limitation**: plan/foundation/review evidence is from Copilot CLI; business operations/tests were implemented with Codex. No full Copilot-only or VS Code GUI workflow is asserted. |
| Human requested LAB_07-style xUnit tests in one file | **Addressed**: one public test class and internal helpers in each API's single test file. Both baseline and reorganized suites passed 53/61 cases; TRX comparisons confirmed identical cases. Production source was unchanged, and the testing/coding skills now preserve the requested layout. This follow-up was checked by Codex; no additional Copilot review is claimed. |

Copilot found no actionable standalone product defects after checking validation, atomic PATCH, offset/stop/preparation rules, immutable snapshots, persistence-after-reference-resolution, unknown fields, DI lifetimes and feature-file organization. That source-review conclusion supplements executed tests; it does not replace them. Scope exclusions (local memory, no auth/live transit/Trips, optional metadata) remain explicit in the contract.
