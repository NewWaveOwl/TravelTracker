# Reviewed implementation plan

The human requires one API using the existing Api/Data/Domain projects and preserving collaborators' work. This replaces the separate WebApi approach. Codex revised the plan; the [original fresh Copilot plan](../../AI_evdence/implementation/copilot-plan.md) remains historical and no new Copilot session is claimed.

1. Compare the new Main clone and latest master against Artiom. Inventory every original source file, model property and interface signature; make no edits to the Main clone.
2. Run the existing 19-case test baseline. Move controllers/request/response DTOs, health, Swagger and launch configuration into the original TravelTracker.Api host on 5104. Retain its Data/Domain references and original interfaces.
3. Reuse the Domain Attraction/TransportMode entities, adding only approved optional description/rating fields. Add RoutePoint to Domain. Map DisplayName/Latitude/Longitude to the existing HTTP name/lat/lon fields.
4. Implement the original repository contracts in TravelTracker.Data with singleton memory stores and the original service contracts in Api. Add equivalent RoutePoint contracts. Preserve every existing method signature; expose only the requested GET-by-ID/POST endpoints.
5. Fix the Id64 constructor's missing explicit conversion. Add empty partial declarations for the four types referenced by unfinished Trips/Settings contracts. Do not invent their fields, implementations, validation or endpoints.
6. Keep the xUnit suite in TravelTracker.Api.Tests referencing Api. Following the human's 2026-10-07 adjustment, split it into HealthTests, AttractionTests, TransportModeTests and RoutePointTests, with shared helpers in Support. Preserve all 19 cases, method bodies and theory inputs. Both solution files contain Api, Data, Domain and the test project.
7. Restore and build the full solution, run the test solution and verify Swagger/health plus all three POST -> Location GET workflows live. Audit original properties/interfaces and ensure Api is the only web SDK project.
8. Remove obsolete generated directories after confirming paths stay in this repository and contain only build/test artifacts or empty directories. Update current skills/docs/evidence while keeping historical records labeled.
9. Review the final diff, commit/push Artiom and update draft PR #16. Human review and merge remain pending.

Users/Accommodations/Trips/Settings endpoints, scheduling, search and PATCH remain future work. The compile-only declarations do not implement a trip feature.

## Test organization follow-up, 2026-10-07

The integration above is complete. For the latest request: establish the 19-case baseline; move existing tests/helpers without changing assertions; compare the moved blocks and inputs against 8ead33a; review current API/model/data/design docs against the source and Swagger; replace stale one-file guidance; build and run all tests; record findings/results; then commit/push Artiom and update the same draft PR. Historical Copilot plans and earlier verification stay labeled as historical.
