# Reviewed implementation plan

The human requires one API using the existing Api/Data/Domain projects and preserving collaborators' work. This replaces the separate WebApi approach. Codex revised the plan; the [original fresh Copilot plan](copilot-plan.md) remains historical and no new Copilot session is claimed.

1. Compare the new Main clone and latest master against Artiom. Inventory every original source file, model property and interface signature; make no edits to the Main clone.
2. Run the existing 19-case test baseline. Move controllers/request/response DTOs, health, Swagger and launch configuration into the original TravelTracker.Api host on 5104. Retain its Data/Domain references and original interfaces.
3. Reuse the Domain Attraction/TransportMode entities, adding only approved optional description/rating fields. Add RoutePoint to Domain. Map DisplayName/Latitude/Longitude to the existing HTTP name/lat/lon fields.
4. Implement the original repository contracts in TravelTracker.Data with singleton memory stores and the original service contracts in Api. Add equivalent RoutePoint contracts. Preserve every existing method signature; expose only the requested GET-by-ID/POST endpoints.
5. Fix the Id64 constructor's missing explicit conversion. Add empty partial declarations for the four types referenced by unfinished Trips/Settings contracts. Do not invent their fields, implementations, validation or endpoints.
6. Move the same single-file xUnit suite into TravelTracker.Api.Tests and point it at Api. Both solution files contain Api, Data, Domain and the test project; keep all 19 test bodies.
7. Restore and build the full solution, run the test solution and verify Swagger/health plus all three POST -> Location GET workflows live. Audit original properties/interfaces and ensure Api is the only web SDK project.
8. Remove obsolete generated directories after confirming paths stay in this repository and contain only build/test artifacts or empty directories. Update current skills/docs/evidence while keeping historical records labeled.
9. Review the final diff, commit/push Artiom and update draft PR #16. Human review and merge remain pending.

Users/Accommodations/Trips/Settings endpoints, scheduling, search and PATCH remain future work. The compile-only declarations do not implement a trip feature.
