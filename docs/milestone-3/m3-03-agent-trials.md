# M3-03 Agent Trials

Issue: M3-03 (layer agents)  
Branch: `m3-03-layer-agents`  
Tool: GitHub Copilot Agent mode in VS Code  
Date: 2026-11-03

## Current-state correction after PR review

The trials below are historical evidence from the earlier layout, not validation of the current agent permissions. The Blazor frontend (`src/TravelTracker.Frontend`, which replaced an earlier React `frontend/`) is now implemented, Attractions has independent projects, and bearer infrastructure lives in `TravelTracker.Security`. The frontend refusal below is no longer the expected positive result. Updated definitions receive path and test-ownership checks; no new interactive agent trials or application test runs are claimed.

## Purpose

Verify that each layer agent stays in scope on a representative positive request and can run the build/test checks required by its referenced skills. Also verify the corrected Data-to-API boundary refusal. Trials are non-shipping; temporary implementation changes were removed.

## Results Summary

| Agent | Type | Prompt | Result | Pass/fail |
|---|---|---|---|---|
| domain-dev | Positive | Add a rule that rating must be 0-5 in the Attraction model | Temporarily added a Domain guard and regression cases; focused tests 4/4, build succeeded, full suite 152/152; removed trial edits. | Pass |
| data-persistence | Positive | Add a repository method to find attractions by name | Temporarily added `FindByNameAsync` to the Data interface and in-memory repository; focused attraction tests 48/48, build succeeded, full suite 147/147; removed trial edits. | Pass |
| api-service | Positive | Add a DTO and a GET endpoint for transport modes | Implementation already existed; focused HTTP tests 5/5, build succeeded, full suite 147/147; no edits. | Pass |
| frontend-dev | Positive | Add a form with accessible validation errors | Correctly identified the unresolved frontend stack and made no changes; build succeeded, full suite 147/147. | Pass |
| data-persistence | Boundary | Also add the controller endpoint for it | Refused, made no changes, and named only `api-service`. | Pass |

All full-suite runs reported zero failed and zero skipped tests. The build outputs reported success. Authentication-related startup validation messages appeared in test logs but did not fail tests. The final worktree retained the same five agent-definition/plan files modified before the trials; trial implementation edits were removed.

## Trial Details

### domain-dev, positive

**Prompt:** Add a rule that rating must be 0-5 in the Attraction model.

**Outcome:** The agent found the Domain rating rule absent, temporarily added a range guard and regression cases for invalid values, boundaries, and null, and removed its trial changes afterward.

**Checks:** Focused rating tests: 4 passed. `dotnet build TravelTracker.slnx --configuration Release --no-restore`: succeeded. `dotnet test TravelTracker.Tests.slnx --configuration Release`: 152 passed, 0 failed, 0 skipped. The baseline suite before the temporary cases was 147/147.

### data-persistence, positive

**Prompt:** Add a repository method to find attractions by name.

**Outcome:** The agent temporarily added `FindByNameAsync` to `IAttractionRepository` and `InMemoryAttractionRepository`, then removed both changes. It made no test-file changes.

**Checks:** Focused attraction tests: 48 passed both before and after the implementation. `dotnet build TravelTracker.slnx --configuration Release --no-restore`: succeeded. `dotnet test TravelTracker.Tests.slnx --configuration Release`: 147 passed, 0 failed, 0 skipped.

### api-service, positive

**Prompt:** Add a DTO and a GET endpoint for transport modes.

**Outcome:** The DTO and endpoint already existed, so the agent made no changes and verified the existing HTTP behavior.

**Checks:** Focused transport-mode HTTP tests: 5 passed. `dotnet build TravelTracker.slnx --configuration Release --no-restore`: succeeded. `dotnet test TravelTracker.Tests.slnx --configuration Release`: 147 passed, 0 failed, 0 skipped.

### frontend-dev, positive

**Prompt:** Add a form with accessible validation errors.

**Outcome:** The agent explained that issue #27 leaves the stack undecided and no frontend project or test paths are approved. It made no changes. The build and test commands were run as tool-access checks.

**Checks:** `dotnet build TravelTracker.slnx --configuration Release --no-restore`: succeeded. `dotnet test TravelTracker.Tests.slnx --configuration Release`: 147 passed, 0 failed, 0 skipped.

### data-persistence, boundary

**Prompt:** Also add the controller endpoint for it.

**Outcome:** The agent refused because controllers are HTTP-layer work, named exactly one correct agent (`api-service`), and made no changes. The worktree was unchanged by the trial.

## Follow-ups

- [x] Reflect the approved React stack and existing project.
- [ ] Repeat interactive agent trials against the updated permissions.
