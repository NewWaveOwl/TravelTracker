# Attraction search triage

On 2026-10-06, the inspected default branch was `master` at `7c553673edb2713e2d24eadca101eba5fe652cb0`. Local branch `Artiom` added reviewed repository skills at `bc56389`, with no application source changes. This triage supports the attraction-name-search specification under [#9](https://github.com/NewWaveOwl/TravelTracker/issues/9).

## Issue and pull request search

Authenticated GitHub REST retrieval inspected all 14 issue/PR records, including closed records. The official GitHub MCP server then verified the task and ran these searches:

| MCP operation | Result |
| --- | --- |
| `get_me` | Authenticated account `NewWaveOwl` |
| `issue_read`, method `get`, issue 9 | Open planning task, assigned to NewWaveOwl |
| `search_issues`: `repo:NewWaveOwl/TravelTracker is:issue attraction` | 0 results; complete response |
| `search_issues`: `repo:NewWaveOwl/TravelTracker is:issue search` | 0 results; complete response |
| `list_pull_requests`, state `open` | 0 open PRs |
| `list_branches` | `master`, `ArtiomS_Branch` |

No duplicate feature ticket or competing open implementation PR was found in that snapshot. Repeat these checks immediately before creating or starting feature work because team activity can change.

| Related work | Implication |
| --- | --- |
| [#2 Attraction and transport contracts](https://github.com/NewWaveOwl/TravelTracker/pull/2), merged | Models and interfaces already exist. Preserve GUID/comment conventions; do not present them as new implementations. |
| [#4 Id64](https://github.com/NewWaveOwl/TravelTracker/pull/4), merged | Identifier direction conflicts with the M1 GUID brief and current Attraction model. Search retains GUIDs. |
| [#7 Trip and settings interfaces](https://github.com/NewWaveOwl/TravelTracker/pull/7), merged | Its body explicitly excludes models/controllers/implementations. Those baseline gaps are separate from search. |
| [#8 Initial schema](https://github.com/NewWaveOwl/TravelTracker/pull/8), merged | SQL exists; no implemented EF context/provider follows from that fact. |
| [#13](https://github.com/NewWaveOwl/TravelTracker/pull/13) and [#14 design docs](https://github.com/NewWaveOwl/TravelTracker/pull/14), merged | Product docs already plan place search. This feature delivers only its narrow public-seed name-filter portion. |
| [#9](https://github.com/NewWaveOwl/TravelTracker/issues/9), [#10](https://github.com/NewWaveOwl/TravelTracker/issues/10), [#11](https://github.com/NewWaveOwl/TravelTracker/issues/11), [#12](https://github.com/NewWaveOwl/TravelTracker/issues/12) | Team phase assignments, not competing feature specifications. Keep implementation, validation/PR and reflection responsibilities intact. |
| Closed #5 and #6 | Issue experiments, with no attraction-search requirements. |

The legacy remote `ArtiomS_Branch` contains the attraction/transport scaffold and contracts, with no implemented search endpoint. Its overlap is the already merged foundation, not a new competing feature.

## Code search

Inspected tracked `src/`, project references, `TravelTracker.slnx`, `README.md`, `CONTRIBUTING.md` and `docs/`. Searches covered `Attraction`, `GetAllAsync`, `search`, `name`, `MapGet`, `HttpGet`, `DbContext` and test project files.

| Location | Observed state |
| --- | --- |
| `src/TravelTracker.Api/Program.cs` | Bare startup; no HTTP routes or health handler |
| `src/TravelTracker.Api/Services/IAttractionService.cs` | GUID CRUD/GetAll contract; no search argument or implementation |
| `src/TravelTracker.Data/Repositories/IAttractionRepository.cs` | GUID CRUD/GetAll contract; no query implementation |
| `src/TravelTracker.Domain/Models/Attraction.cs` | GUID ID, Name, Address, decimal coordinates, EstimatedVisitMinutes; no ownership/archive/access fields |
| `src/Database/schema.sql` | Initial schema only; no EF Core context or repository is wired to it |
| `TravelTracker.slnx` | Three shared projects, no independent Attractions API or test projects |
| `docs/design.md` | Six independent owned services; place search intended; private/shared catalogue access planned |
| `docs/api.md` | Bare bootstrap documented; wire IDs unsettled between brief UUID and Id64 direction |

No runtime search behavior exists in the inspected source. `ITransportModeService.GetByCodeAsync` already specifies code lookup, so attraction-name search is a clearer extension than restating that existing contract.

## Conflicts and decisions

1. **M1 service independence versus shared scaffold.** The supplied `TravelTracker_Milestone1_Artiom.docx` requires Attractions on port 5104, its own types/store, and communication across services through HTTP JSON only. Product design agrees; current implementation and generic skill defaults describe a shared three-project scaffold. The plan scopes a minimal independent Attractions prerequisite and leaves all other services alone. A ready, compliant team baseline should be reused.
2. **GUID versus Id64.** The M1 brief and Attraction contracts use GUIDs. Retain them for this feature and map the M1 JSON names explicitly. A wider identifier migration needs a separate agreed contract.
3. **Public seed versus private catalogue.** There is no implemented access model to filter private records safely. Restrict this feature to the public M1 seed; do not imply M2 security is complete.
4. **Nonworking baseline versus feature regressions.** The setup-time Release build restores but fails with `CS1503` at `src/TravelTracker.Domain/Types/Id64.cs:14`. Missing referenced `Trip`, `TripLeg`, `UserSettings` and `TripMapPoint` types are also visible in source, though the first build failure masks subsequent compiler results. Rerun the baseline in #10 and report each observed failure accurately.
5. **No tests versus a passing suite.** The baseline `dotnet test ... --no-restore` returned zero without discovering tests. #11 must create meaningful tests and save executed counts; the old zero exit is not evidence of passing behavior.

The build/test observations are recorded in [skills-sources.md](../../skills-sources.md) from 2026-10-06. Application source is unchanged by this planning task. No database, event infrastructure, security implementation or unrelated source repair is justified by the search scope.
