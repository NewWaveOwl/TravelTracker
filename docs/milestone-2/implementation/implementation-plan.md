# Reviewed implementation plan

The human requested one API and one Swagger page like LAB_07 after reviewing the earlier two-host arrangement. Codex revised this plan for that migration. The [original fresh Copilot plan](copilot-plan.md) remains historical; no new Copilot session is claimed.

1. Read LAB_07, repository skills/source/docs and the current user-added Swagger package. Run the existing 20-case baseline and preserve the unfinished shared scaffold.
2. Move the three current resource controllers, contracts, models, services, repositories and seeds into TravelTracker.WebApi. Keep their routes, fields, validation and memory behavior. Replace two entry points/health controllers with one Program and one /health.
3. Carry forward Swashbuckle.AspNetCore 10.2.3, register Swagger and expose the UI/document in Development. Use the shared http launch profile on port 5104. Document creation's 201 and missing-ID 404 responses in Swagger.
4. Merge both xUnit projects into TravelTracker.WebApi.Tests, with all tests/helpers in TravelTrackerTests.cs and one factory for the shared Program. Remove only the duplicate service health case: 20 becomes 19. Update both solution files and HTTP examples.
5. Restore/build/test the combined solution. Run eight live checks for Swagger, its seven operations/six resource-request schemas, health, three POST -> GET workflows, missing ID and invalid rating. Recheck the full-solution baseline failure.
6. Update current instructions, skills, documentation, review and evidence. Preserve the historical issue/Copilot records. Commit/push Artiom and update PR #16 to describe one host. Leave merging to human review.

Only the currently implemented three resources move into the shared host. Other owners' planned services and the unfinished API/Data/Domain/SQL files are preserved. Listing/search, PATCH and scheduling remain outside this change.
