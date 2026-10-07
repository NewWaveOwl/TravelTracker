# Current implementation triage

The initial [issue/code triage](../issue-9/triage-notes.md) and historical Copilot records remain the earlier research evidence. No new remote issue search or Copilot interview is claimed for this simplification.

The human explicitly reduced the work to POST/GET and retained route points, transport and attractions. The earlier implementation expanded into collection search, updates and scheduled routes. Those capabilities and their related tests are removed from the current API surface.

| Area reviewed | Result |
| --- | --- |
| LAB_07 | Groups related actions in resource controllers/services with repository injection. The simplified code follows that layout. |
| Existing standalone resources | Reuse AttractionRecord, TransportModeRecord and RoutePointRecord, fixed seeds, health and launch profiles. |
| Shared scaffold | API/Data/Domain interfaces and SQL remain untouched. The pre-existing Id64.cs:14 CS1503 still fails the full build. |
| Ownership | Attractions owns attractions. Transport owns entries and route-point locations. Source IDs are metadata. Whole itineraries remain future Trips work. |
| Complexity | Production C# files reduced from 88 to 29. Removed schedules/stops, per-action partials, custom converters/validators and response-mapper layers. |
| Test overlap | Earlier identical-body audit found no literal duplicates, but scope and boundary coverage were much broader than requested. Current tests cover only the remaining API behavior. |

Memory repositories are thread-safe singletons so POST writes survive later requests. Services remain scoped. No SQL database, EF context or remote service is required for the reduced demo. [Current specification](specification.md).


The final host-layout review confirmed LAB_07 has one web application with multiple controllers and one xUnit project. The human explicitly selected that deployment. Current resource types moved into TravelTracker.WebApi on 5104; separate host projects were removed, routes/data rules preserved, and the existing Swagger 10.2.3 package carried forward. The prior two-project 20-case baseline passed before migration. Current validation uses one 19-case project plus the shared live Swagger/API checks.
