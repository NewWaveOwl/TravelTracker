# M3-03 Layer Agent Plan

Issue: #29 (M3-03)
Dependency: #27 (M3-01)
Branch: `m3-03-layer-agents`

## Decisions and constraints

- Create four focused agents under `.github/agents/`.
- Reuse canonical skills under `.github/skills/`; do not copy skill bodies into agents.
- `domain-dev`, `data-persistence`, and `api-service` each reference both `.github/skills/code/SKILL.md` and `.github/skills/testing/SKILL.md`.
- `frontend-dev` references `.github/skills/code/SKILL.md` and remains stack-neutral because issue #27 and `docs/milestone-3-contracts.md` do not record a React Native versus ASP.NET Core decision.
- The current implementation remains in `src/TravelTracker.Domain`, `src/TravelTracker.Data`, and `src/TravelTracker.Api`; the proposed per-service project layout is defined by issue #27 and is not restated in the discovery instructions.
- Agent metadata uses the existing repository tool-name vocabulary: `search`, `changes`, `usages`, `problems`, `edit`, `runCommands`, and `runTests` as applicable. These are agent-runtime identifiers, not local PowerShell commands.

## Exact paths and test-file permissions

| Agent | Production paths | Test files it may edit |
|---|---|---|
| `domain-dev` | `src/TravelTracker.Domain/**` | None in the current solution; it must not edit the existing API test files. |
| `data-persistence` | `src/TravelTracker.Data/**` | `tests/TravelTracker.Api.Tests/EntityIdGeneratorTests.cs`, `tests/TravelTracker.Api.Tests/RoutePointTests.cs`, `tests/TravelTracker.Api.Tests/TransportModeTests.cs` |
| `api-service` | `src/TravelTracker.Api/**` | `tests/TravelTracker.Api.Tests/AttractionTests.cs`, `tests/TravelTracker.Api.Tests/HealthTests.cs`, `tests/TravelTracker.Api.Tests/RoutePointTests.cs`, `tests/TravelTracker.Api.Tests/TransportModeTests.cs` |
| `frontend-dev` | Future frontend project only; no frontend project currently exists | None currently; a future frontend test file must be created by an explicitly scoped frontend issue. |

Shared test helpers under `tests/TravelTracker.Api.Tests/Support/**` are not permitted to these layer agents unless a task explicitly requires a helper change and names the file.

## Commit split

1. Discovery routing commit: update `.github/copilot-instructions.md` only with agent routing, canonical skill references, and links to the focused agent definitions. Link to issue #27 for the project layout instead of duplicating its contents.
2. Corrections commit: update `.github/copilot-instructions.md` only with the corrected attraction search/PATCH scope statement and the actual test command/solution name.

## File-by-file implementation order

1. `docs/milestone-3/M3-03-plan.md` — preserve this approved implementation plan.
2. `.github/agents/domain-dev.agent.md` — create first; stop and wait for explicit approval after showing the file.
3. `.github/agents/data-persistence.agent.md` — create only after approval; reference code and testing skills, enforce Data-only boundaries, and list exact permitted test files.
4. `.github/agents/api-service.agent.md` — create only after approval; reference code and testing skills, enforce API/service boundaries, and list exact permitted test files.
5. `.github/agents/frontend-dev.agent.md` — create only after approval; reference the code skill, remain stack-neutral, enforce HTTP-only DTO integration, and list the absence of current frontend test files.
6. `.github/copilot-instructions.md` — make the discovery-routing changes as a separately staged commit.
7. `.github/copilot-instructions.md` — make the search/PATCH and test-command corrections as a separately staged commit.
8. `docs/milestone-3/M3-03-agent-trials.md` — document representative positive prompts and boundary-violation prompts after all four agents exist. Record tool/path verification and any unavailable agent-runtime tools; do not claim trials were run unless they were actually run.

## Verification

- Verify every referenced skill path, agent path, source path, test path, and documentation path exists before use.
- Report agent-runtime tool names that cannot be resolved locally rather than inventing replacements.
- Validate that each agent references canonical skills by path and does not duplicate their content.
- Confirm the frontend agent does not select a stack that issue #27 leaves open.
- Check that the two `.github/copilot-instructions.md` changes can be staged as two independent commits.
- Run documentation/path consistency checks and any available representative agent trials after implementation; no application build is required for agent-only Markdown changes unless the repository validation gate requires it.
