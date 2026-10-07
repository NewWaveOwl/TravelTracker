# Current test layout and overlap review

Following the human's 2026-10-07 request, one xUnit project, TravelTracker.Api.Tests, now groups tests by resource or purpose. These are HTTP integration tests: they exercise controllers, binding, validation, services and in-memory repositories together, rather than testing Domain models in isolation. The project references the single TravelTracker.Api Program.

| File | Cases | Aim |
| --- | --- | --- |
| [HealthTests.cs](../../../tests/TravelTracker.Api.Tests/HealthTests.cs) | 1 | Health/status/service identity |
| [AttractionTests.cs](../../../tests/TravelTracker.Api.Tests/AttractionTests.cs) | 6 | Seed/schema; absent ID; POST/Location and saved fields; blank name; two invalid ratings |
| [TransportModeTests.cs](../../../tests/TravelTracker.Api.Tests/TransportModeTests.cs) | 5 | Seed; absent ID; POST/Location and saved fields; unsupported code; blank name |
| [RoutePointTests.cs](../../../tests/TravelTracker.Api.Tests/RoutePointTests.cs) | 7 | Three supported types with POST/Location and source metadata; seed; absent ID; invalid type/latitude |

[ApiFactory.cs](../../../tests/TravelTracker.Api.Tests/Support/ApiFactory.cs) configures the HTTP test host. Each case creates and disposes its own factory/client and singleton memory stores, so separate test classes can run independently. [HttpTestSupport.cs](../../../tests/TravelTracker.Api.Tests/Support/HttpTestSupport.cs) shares POST/Location/GET and validation-problem assertions. Neither helper contains a test or mutable shared state.

The split preserves all 16 test methods, their documentation, assertions and theory inputs from 8ead33a, giving **19 discovered cases**, including every theory row. Helper bodies are unchanged. No cases were added or removed, and production source is unchanged. The one test project and TravelTracker.Tests.slnx runner remain the same; the earlier one-file preference is superseded.

Duplicate review compares method names, normalized bodies and theory inputs; the TRX results confirm 19 distinct discovered cases across the four classes. Different resources deliberately share status/header checks through the helper, because each has its own controller and request contract. These are separate workflows rather than duplicate tests for one endpoint.

The earlier two-project baseline had 20 cases. The duplicate health case was removed when both APIs became one host; older 114-/30-/20-case records remain historical. This reorganization does not restore removed matrices or scheduling tests. The 19-case suite remains below the requested maximum of 30; eight separate live Swagger/API checks are also recorded.

Coverage is representative. It does not exercise every coordinate/address/binding edge, internal collection/update/delete method, persistence, authentication or future scheduling/Trips rule. [Current validation](validation.md) and [review record](review-record.md) record the checks and limits.
