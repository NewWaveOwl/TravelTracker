# Test scope and overlap review

The human requested LAB_07-style xUnit tests in one file per project, at most 30 cases, and then clarified that only simple POST/GET resources should remain. The current suite contains **20 discovered cases**, all passing: 7 Attractions and 13 Transport/Route Points. Every theory row counts separately; no old matrix is hidden inside loops.

| Resource | Cases | Kept behavior |
| --- | --- | --- |
| Attractions | 7 | Health; seeded record/schema; absent ID; creation/Location; blank name; rating below/above range |
| Transport API and entries | 6 | Health; seeded entry; absent ID; creation/Location; unsupported mode; blank name |
| Route Points | 7 | Creation/Location for each of three types; seeded point; absent ID; unsupported type; invalid latitude |

All tests and internal host/assertion helpers stay in [AttractionsTests.cs](../../../tests/TravelTracker.Attractions.Api.Tests/AttractionsTests.cs) or [TransportTests.cs](../../../tests/TravelTracker.Transport.Api.Tests/TransportTests.cs). Separate projects prevent the two Program entry points from colliding. Each test owns its factory, client and seeded memory.

The earlier 114-case suite had no identical normalized method bodies or repeated rows within a theory, but did repeat coverage at HTTP and store boundaries. It was first reduced to 30. This follow-up removes tests for the now-removed list/search/PATCH/schedule features and extra edge matrices. It does not claim equivalent coverage to either historical suite.

Current duplicate checks cover normalized test-method bodies, repeated rows within each theory and discovered case names. Shared assertion helpers intentionally repeat across the two isolated projects; they are not additional tests. Exact counters and source hashes are under simpleImplementation in [verification.json](verification.json).

Eight separate live-process checks exercise both health endpoints and each resource's POST -> GET and absent-ID behavior. These supplement the 20 automated cases; even counting both kinds together gives 28 checks. The smaller suite does not exhaust every binding, null, coordinate, concurrency or restart edge.
