# Current test layout and overlap review

The final LAB_07-style layout is one xUnit project, TravelTracker.Api.Tests, and one [TravelTrackerTests.cs](../../../tests/TravelTracker.Api.Tests/TravelTrackerTests.cs) containing all tests and internal host/assertion helpers. It references the single shared API Program. Each test creates its own host and memory repositories.

The former two-project baseline had 20 passing cases. Both old health tests would check the same endpoint after consolidation, so the duplicate was removed. All remaining creation/lookup/validation cases were preserved; the shared health expectation is now TravelTracker. **19 discovered cases pass**, including each theory row.

| Area | Cases | Behavior |
| --- | --- | --- |
| Shared host | 1 | Health/status/service identity |
| Attractions | 6 | Seed/schema; absent ID; POST/Location; blank name; two invalid ratings |
| Transport entries | 5 | Seed; absent ID; POST/Location; unsupported code; blank name |
| Route Points | 7 | Three supported types with POST/Location; seed; absent ID; invalid type/latitude |

Current duplicate checks cover normalized method bodies, repeated theory inputs and discovered case names. Eight separate live checks include the shared Swagger page/document and all three resource workflows on one port; 19 automated plus eight live checks equals 27, within the earlier maximum of 30 even when counted together.

Older 114-/30-/20-case results are historical and do not imply exhaustive current coverage. No extra input matrices, scheduling tests or new test cases were added. [Current validation](validation.md), [structured evidence](verification.json).

Integration into the original Api/Data/Domain structure preserved every test body, changing only the test namespace/project reference. The full solution builds and the same 19 cases pass. No tests were added.
