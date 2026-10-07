# Decisions and corrections

This log separates direct human decisions from agent implementation choices. Order follows the visible requests and preserved records; unknown timestamps and missing answers are not supplied.

| Order | Decision or correction | Source | Result |
| --- | --- | --- | --- |
| 1 | Create separate coding/commenting and testing skills using the supplied lectures/videos; use Artiom and show skills before committing | Human request | Versioned skills and agent routing |
| 2 | Use `/** ... */`, `@param` and `@return`; one resource responsibility per production file | Human request | Separate layer/resource classes and member comments |
| 3 | Perform MCP setup, issue drafting/review, triage, interview and a fresh implementation plan | Human milestone task #9 | Preserved planning package |
| 4 | Accept VS Code/Copilot, .NET 10, seeded in-memory attraction API, description/rating and management/search endpoints | Supplied human feature specification | Confirmed issue #15 and original reviewed plan |
| 5 | Use LAB_07 as the implementation/testing structural reference | Human request | Controller/service/repository pattern and xUnit project |
| 6 | Transport POST should create a reusable entry rather than always scheduling a journey | Human clarification | Separate transport entry and scheduled-route concepts |
| 7 | Route points represent accommodation, attraction and transport stops; routes can connect them with times/stops | Human clarification | Reusable RoutePoint, with future route relationships documented |
| 8 | Too many tests; cap discovered cases at 30 | Human correction | Historical 114-case suite reduced; coverage tradeoffs documented |
| 9 | Keep only simple POST and GET by ID for attractions, transport and route points | Human request and supplied scope reply | Scheduled routes, search/PATCH and collection routes removed from current demo |
| 10 | Use one Swagger address like LAB_07 | Human correction | Separate hosts consolidated |
| 11 | Extend existing Api/Data/Domain and preserve collaborators' source | Human correction | Parallel WebApi removed; existing entities/interfaces reused |
| 12 | Fix compilation without inventing unfinished Trip/Settings data | Agent implementation choice within integration scope | Id64 conversion fix and empty partial declarations, clearly described as compile-only |
| 13 | One xUnit project, initially all tests in one file | Earlier human request | Shared 19-case suite and test solution |
| 14 | Split tests by what they verify | Later human request | HealthTests, AttractionTests, TransportModeTests and RoutePointTests; helpers in Support; same 19 cases |
| 15 | PR description should explain actual differences from main briefly | Human request | Concise main-versus-branch description; earlier migration narrative removed |
| 16 | Use a short validation.md and update the PR | Human request | Detailed JSON removed from main; concise results/commands/limits retained |
| 17 | Archive all AI/development evidence in the dedicated resource branch and create a separate removal PR | Current human request | Records organised in AI_evdence/devlopmet_flow and old JSON restored here |
| 18 | Keep reusable skills/MCP setup on main while moving evidence/history | Recommended agent default announced during archive task; no separate human answer recorded at authoring | Cleanup initially targets evidence documents; snapshots also preserved in AI_evdence |

The original interview answers are preserved in [their supplied form](../AI_evdence/issue-9/evidence/human-interview-answers.md). The [conversation record](../AI_evdence/conversation/conversation-record.md) contains direct request excerpts, with assistant outcome summaries labelled separately.
