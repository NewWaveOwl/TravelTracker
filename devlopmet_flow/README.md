# Development flow

The resource branch preserves the existing main documentation and adds a fuller account of how the Milestone 2 change evolved. Folder spelling follows the requested layout.

| Start here | Contents |
| --- | --- |
| [Development process](development-process.md) | Steps 1-9, tools, accepted scope, implementation, verification and delivery |
| [Decision log](decision-log.md) | Explicit human choices, agent defaults, corrections and their effects |
| [Migration validation](cleanup-pr-validation.md) | Preservation and link checks for the separate main-cleanup PR |
| [Issue #9 handoff](issue-9/README.md) | MCP setup, triage, interview and reviewed original feature plan |
| [Implementation records](implementation/README.md) | Reduced specification, adjusted plan, test selection, validation, review and reflection |
| [AI evidence](../AI_evdence/README.md) | Prompts/responses, sessions, conversation record and historical verification |
| [Original project context](project-context/README.md) | Main README before cleanup, with links to immutable product/source snapshots |
| [Product design snapshot](project-context/docs/design.md) | Product roadmap and the distinction between proposed capabilities and the implemented demo |
| [Route relationship snapshot](project-context/docs/trip-points-and-routes.md) | Existing source preservation and reusable points versus future schedules |
| [Original reflection placeholder](reflections/reflection_milestone_2.md) | Preserved exactly; main contained only a placeholder, not a completed teammate reflection |

The current demo has one .NET 10 Api/Data/Domain application on port 5104, health plus POST/GET by ID for Attractions, Transport entries and Route Points, seeded memory and 19 HTTP tests. Planned routes and Trips have no runtime implementation. Earlier documents are historical snapshots and may describe superseded scope or a pre-merge PR status.

PR #16 merged into main; PR #17 updated the root GitHub MCP URL. This branch starts from its existing documentation-only history and imports records from main `68fe7ea`, preserving the branch's original README. Application source stays on main. [Cleanup PR #19](https://github.com/NewWaveOwl/TravelTracker/pull/19), based on newer main `825ab43`, proposes removal of the archived records. Main's new AGENTS.md rule is retained. This documentation branch is not merged into the application.
