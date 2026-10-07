# Milestone 2 development process

This expanded account was written on 2026-10-07 from the preserved repository records, visible chat messages and checked Git history. Earlier draft requirements remain evidence of the workflow, not promises about the current API.

## Starting point and ownership

The initial shared main revision was `7c55367`: Api, Data, Domain and SQL existed with collaborator models/interfaces, but the Api bootstrap exposed no resource endpoints. The first full build failed at the Id64 constructor, and a test command without discovered tests did not establish a passing suite. These limitations were recorded before implementation.

Christel owned Users/Accommodations, Artiom owned Attractions/Transport, and James owned Trips/Settings. The implementation ultimately extended those original projects and retained original interfaces/properties. It did not provide missing collaborator endpoints or infer unfinished business schemas.

## Steps and evidence

| Milestone step | What happened | Evidence |
| --- | --- | --- |
| 1. GitHub MCP | The official remote GitHub MCP server was configured. The setup guide explains editor trust/authorization and replication. Repository/issue/PR operations used the configured server. | [Setup](issue-9/github-mcp-setup.md), [configuration snapshots](../AI_evdence/setup-snapshot/README.md) |
| 2. Issue | Copilot produced an issue draft. Codex reviewed/edited it and published attraction management/name search as #15, related to workflow task #9. | [Specification](issue-9/feature-spec.md), [draft/prompt](../AI_evdence/issue-9/evidence/copilot-management-draft.md) |
| 3. Triage | Existing issues, code and interfaces were inspected for duplicates, conflicts and missing implementations. Baseline build risks and ownership were written down. | [Original triage](issue-9/triage-notes.md), [later integration triage](implementation/triage.md) |
| 4. Prime/interview | Questions were generated before coding. The human supplied environment, fields, endpoints and validation preferences. Provisional defaults were replaced by those answers. | [Interview](issue-9/priming-interview.md), [human answers](../AI_evdence/issue-9/evidence/human-interview-answers.md) |
| 5. Fresh plan | A distinct Copilot session generated the plan after the answers. Review corrected it. Later human scope changes required a revised implementation plan. | [Original plan](issue-9/implementation-plan.md), [fresh-session prompt](../AI_evdence/issue-9/evidence/confirmed-plan-prompt.md), [final adjusted plan](implementation/implementation-plan.md) |
| 6. Implement/R-PIV | Research and plan preceded implementation. Real Copilot CLI sessions produced plan/foundation/review output; Codex completed feature work and later corrections. The early separate-host/scheduled-route design was reduced after human feedback. | [Copilot provenance](../AI_evdence/implementation/copilot-evidence.md), [original responses](../AI_evdence/implementation/responses), [decision log](decision-log.md) |
| 7. Validate | Build/test/live checks were executed. The suite was reduced from historical 114/30/20 versions to 19 current cases. Existing bodies/inputs were preserved when split into four files. | [Current validation](implementation/validation.md), [test selection](implementation/test-review.md), [historical results](../AI_evdence/verification/verification-history.json) |
| 8. PR/review | PR #16 carried the change. Review corrected architecture, decimal validation, interface/property preservation, stale instructions and test organisation. Its final description was shortened to the actual main-versus-branch changes. | [Review record](implementation/review-record.md), [PR #16](https://github.com/NewWaveOwl/TravelTracker/pull/16) |
| 9. Reflect | The record distinguishes where guidance/planning helped from where human judgment corrected excess scope, tests and duplicate layers. | [Reflection](implementation/reflection.md), [conversation](../AI_evdence/conversation/conversation-record.md) |

## Final implemented scope

- Existing TravelTracker.Api is the only web host. Data contains repositories; Domain contains entities; Swagger is enabled in Development on port 5104.
- Health, POST and GET by ID are implemented for Attractions, Transport entries and Route Points. POST generates a GUID and returns 201 plus a details Location; reads return 200/404; binding/validation returns 400 for invalid requests.
- Attractions have optional description/manual rating, a required trimmed name, coordinates and visit minutes. Transport catalogue entries have an exact supported mode code, name and optional description. RoutePoint is a reusable typed location with optional unverified sourceId.
- Services are scoped; seeded in-memory repositories are singletons. Changes reset on restart. There is no SQL persistence, authentication, list/search/PATCH or schedule calculation in this demo.
- Future scheduled routes use transport entries and start/end/intermediate RoutePoints. Departure/arrival, boarding/preparation and downtime belong to those future journey occurrences, not catalogue POST.
- Trip, TripLeg, TripMapPoint and UserSettings are empty compile-only declarations for existing interfaces. They do not implement Trips/Settings features.

## Verification and preservation

The final source/tests committed as `2921aef` built with zero warnings/errors, passed 19 HTTP cases and eight live checks. No new test run is claimed for later documentation-only cleanup. All 26 initial main source files remained; 20 were unchanged, six had integration updates, and all 11 original interfaces and original model properties were preserved. The separately cloned main checkout was not edited.

The source-preservation proof concerns the checked initial main revision. The cleanup PR separately checks that current main source/tests/configuration remain untouched by moving documentation. The [manifest](../AI_evdence/archive-manifest.md) identifies every moved record and its source hash.

## Provenance and limits

The human chose VS Code with GitHub Copilot. Recorded plan/foundation/review evidence comes from actual fresh Copilot CLI sessions, not a new VS Code Agent-mode screen recording. Codex performed later implementation, tests and corrections. No complete new interview or extra Copilot run is invented for this archival task.

Issue #15 originally requested search/PATCH too. The accepted simple implementation does not fully complete that broader issue. The test suite covers representative public HTTP behavior, not every input edge, internal update/delete operation, database transaction or future route/Trips rule.

The JSON history was removed from main to reduce noise, then restored here because the human requested fuller evidence on a dedicated branch. Raw internal reasoning, authentication logs, packages and generated binaries are not part of this archive.
