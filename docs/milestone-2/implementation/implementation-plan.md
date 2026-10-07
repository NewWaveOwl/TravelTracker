# Reviewed implementation plan

This current plan reflects the human's final three-resource GET/POST scope. The [original fresh Copilot plan](copilot-plan.md) addressed the earlier search/PATCH/scheduled-route scope and remains historical. Codex adjusted the plan after the human's correction; this revision was not produced by a new Copilot session.

1. Read repository skills, existing docs/source, LAB_07 and the current branch/PR. Establish the current test baseline and preserve the original shared scaffold.
2. Keep the two standalone .NET 10 APIs, health endpoints and ports (Attractions 5104, Transport 5103).
3. Retain only Attraction, TransportMode and RoutePoint models and create requests. Use standard DataAnnotations for the agreed required fields, allowed codes/types and numeric ranges.
4. Consolidate each resource's GET-by-ID and Create into one controller and one service. Keep one repository/interface per resource; each singleton in-memory repository owns its seeded ConcurrentDictionary. Keep services scoped and operations synchronous because no external I/O occurs.
5. Remove per-action partial files, response mappers, custom validation/converters, list/search/PATCH and scheduled-route/stop machinery. Review the three-resource contract and creation Location behavior.
6. Replace tests for removed capabilities with 7 Attractions and 13 Transport/Route Point cases in the existing two single-file xUnit projects. Build and execute both targeted suites and eight independent-process smoke checks.
7. Recheck the full solution. Report the unchanged shared Id64 failure separately; do not run full-solution tests against stale output. Review the diff, comment style, links, skills and source hashes.
8. Update current docs/examples/evidence, retaining historical Copilot records. Commit and push Artiom, then update the existing PR's title/body to this scope. Remove the earlier closing reference to #15 because search/PATCH are deferred. Leave merging to human review.

Implementation decisions are explicit defaults derived from the accepted scope, not invented interview answers. No new layers, EF provider, mediator, scheduler or Trips aggregate is needed for these basic in-memory operations.
