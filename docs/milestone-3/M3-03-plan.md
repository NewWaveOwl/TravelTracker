# M3-03 Layer Agent Plan

PR #71 review correction: the approved Blazor frontend exists in `src/TravelTracker.Frontend`; Attractions has independent Api/Data/Domain projects and both API hosts consume `TravelTracker.Security`. The permissions below reflect that implemented layout.

Issue: #29 (M3-03)
Dependency: #27 (M3-01)
Branch: `m3-03-layer-agents`

## Decisions and constraints

- Create four focused agents under `.github/agents/`.
- Reuse canonical skills under `.github/skills/`; do not copy skill bodies into agents.
- `domain-dev`, `data-persistence`, and `api-service` each reference both `.github/skills/code/SKILL.md` and `.github/skills/testing/SKILL.md`.
- `frontend-dev` references `.github/skills/code/SKILL.md` and uses the approved Blazor Web App stack recorded in `docs/milestone-3-contracts.md`.
- The current implementation includes the original and Attractions Domain/Data/Api projects, shared Security infrastructure, and `src/TravelTracker.Frontend`.
- Agent metadata uses the existing repository tool-name vocabulary: `search`, `changes`, `usages`, `problems`, `edit`, `runCommands`, and `runTests` as applicable. All four agents need `runCommands` and `runTests` to execute the build and test checks required by their referenced skills. These are agent-runtime identifiers, not local PowerShell commands.

## Exact paths and test-file permissions

| Agent | Production paths | Test files it may edit |
|---|---|---|
| `domain-dev` | `src/TravelTracker.Domain/**`, `src/TravelTracker.Attractions.Domain/**` | `tests/TravelTracker.Api.Tests/TripTests.cs` (pure Domain rules; no HTTP or storage) |
| `data-persistence` | `src/TravelTracker.Data/**`, `src/TravelTracker.Attractions.Data/**` | `tests/TravelTracker.Api.Tests/EntityIdGeneratorTests.cs` |
| `api-service` | `src/TravelTracker.Api/**`, `src/TravelTracker.Attractions.Api/**`, `src/TravelTracker.Security/**` | All five non-Support Attractions test files listed in the API agent, plus `tests/TravelTracker.Api.Tests/AuthenticationTests.cs`, `tests/TravelTracker.Api.Tests/HealthTests.cs`, `tests/TravelTracker.Api.Tests/RoutePointTests.cs`, `tests/TravelTracker.Api.Tests/TransportModeTests.cs` |
| `frontend-dev` | `src/TravelTracker.Frontend/**` | Client tests under `tests/TravelTracker.Frontend.Tests/**`, including `ServiceClientTests.cs` and `LayoutTests.cs`. |

Shared test helpers under either backend test project's `Support/**` are not permitted to any agent unless an approved task explicitly requires a helper change and names the exact file.

## Commit split

1. Discovery routing commit: update `.github/copilot-instructions.md` only with agent routing, canonical skill references, and links to the focused agent definitions. Link to issue #27 for the project layout instead of duplicating its contents.
2. Corrections commit: update `.github/copilot-instructions.md` only with the corrected attraction search/PATCH scope statement and the actual test command/solution name.

## File-by-file implementation order

1. `docs/milestone-3/M3-03-plan.md` — preserve this approved implementation plan.
2. `.github/agents/domain-dev.agent.md` — create first; stop and wait for explicit approval after showing the file.
3. `.github/agents/data-persistence.agent.md` — create only after approval; reference code and testing skills, enforce Data-only boundaries, and list exact permitted test files.
4. `.github/agents/api-service.agent.md` — create only after approval; reference code and testing skills, enforce API/service boundaries, and list exact permitted test files.
5. `.github/agents/frontend-dev.agent.md` — create only after approval; reference the code skill, use the approved Blazor stack, enforce HTTP-only DTO integration, and list current frontend test ownership.
6. `.github/copilot-instructions.md` — make the discovery-routing changes as a separately staged commit.
7. `.github/copilot-instructions.md` — make the search/PATCH and test-command corrections as a separately staged commit.
8. `docs/milestone-3/m3-03-agent-trials.md` — document representative positive prompts and boundary-violation prompts after all four agents exist. Record tool/path verification and any unavailable agent-runtime tools; do not claim trials were run unless they were actually run.

## Verification

- Verify every referenced skill path, agent path, source path, test path, and documentation path exists before use.
- Report agent-runtime tool names that cannot be resolved locally rather than inventing replacements.
- Validate that each agent references canonical skills by path and does not duplicate their content.
- Confirm the frontend agent uses the recorded Blazor decision and existing project.
- Check that the two `.github/copilot-instructions.md` changes can be staged as two independent commits.
- Run documentation/path consistency checks and representative positive and boundary agent trials after implementation; positive trials must use `runCommands` and `runTests` to run the checks required by the relevant skill. No application build is required for agent-only Markdown changes unless the repository validation gate requires it.
