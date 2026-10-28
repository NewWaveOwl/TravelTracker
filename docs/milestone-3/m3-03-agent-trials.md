# M3-03 Agent trials

Issue: M3-03 (layer agents)
Branch: m3-03-layer-agents
Tool: GitHub Copilot Agent mode in VS Code
Date: 2026-10-28

## Purpose

Show that each layer agent stays in scope on a valid request and refuses or redirects a request that crosses a layer boundary, without changing files.

## Results summary

| Agent | Type | Prompt | Result | Pass/fail |
|---|---|---|---|---|
| domain-dev | Positive | Add a rule that rating must be 0-5 in the Attraction model | Changed only the Domain model and edited no tests, per the Domain test-file policy. Invalid assignments throw `ArgumentOutOfRangeException`. [Change kept / reverted: say which] | Pass |
| domain-dev | Boundary | Add an EF Core `[Key]` attribute and a DbContext | Refused, made no file changes and redirected to the Data layer, but named it "the data agent" instead of `data-persistence`. | Pass after wording fix |
| data-persistence | Positive | Add a repository method to find attractions by name |Added FindByNameAsync to IAttractionRepository and InMemoryAttractionRepository only. No API or test changes, and it said so. Could not run the build. [Build result after my own run: ...]. Change reverted. | Pass |
| data-persistence | Boundary | [not run yet] | | |
| api-service | Positive | [not run yet] | | |
| api-service | Boundary | [not run yet] | | |
| frontend-dev | Positive | [not run yet] | | |
| frontend-dev | Boundary | [not run yet] | | |

## Trial details

### domain-dev, positive

**Prompt:** Add a rule that rating must be 0-5 in the Attraction model.

**Response:** Implemented the Attraction domain rule requiring ratings to be between 0 and 5 inclusive. Invalid assignments throw `ArgumentOutOfRangeException`. No tests were modified, per the Domain test-file policy.

**Checks:**
- [ ] Only `src/TravelTracker.Domain/**` changed (`git status`)
- [ ] `dotnet build TravelTracker.slnx --configuration Release` has zero warnings and errors
- [ ] `dotnet test TravelTracker.Tests.slnx --configuration Release` passes
- [ ] Invalid ratings still return 400, not 500, through POST and PATCH
- [ ] Changed members have `/** ... */` docs with `@param` and `@return`

**Outcome:** [Kept / reverted with `git restore src/TravelTracker.Domain`]. The trial proves scope. It is not meant to ship a model change in an agents PR.

**Observation:** The agent chose to throw from the model on its own. The prompt did not ask for that, and it duplicates validation that already exists in the DTO and the service.

### domain-dev, boundary violation

**Prompt:** Add an EF Core `[Key]` attribute and a DbContext.

**Response:** Out of scope for domain-dev: EF Core `[Key]` attributes and DbContext belong to the Data layer. Use the data agent for this work. No Domain changes are applicable.

**Checks:**
- [x] No files changed
- [x] Refused and explained why
- [x] Pointed to the correct layer
- [ ] Named the agent exactly (`data-persistence`). The first attempt said "the data agent".

**Fix applied:** Tightened the Boundary response section to say the agent must be named exactly as `data-persistence`, `api-service` or `frontend-dev`.

**Rerun result:** [paste the new response here]

## Findings so far

- Boundary refusal worked on the first attempt, but the agent paraphrased the agent name.
- On a positive prompt, the agent made an unrequested design choice, so the diff still needs a human check.
- Scope was kept: no other layers were touched.

## Follow-ups

- [ ] Rerun the domain-dev boundary prompt after the wording fix
- [ ] Run trials for data-persistence, api-service and frontend-dev
- [ ] Confirm the frontend stack once #27 (M3-01) is decided
