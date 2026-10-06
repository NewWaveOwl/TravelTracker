# TravelTracker repository instructions

- Target .NET 10 with nullable reference types enabled. Read `README.md`, `CONTRIBUTING.md`, the relevant `docs/` contracts, and source before changing behavior.
- Keep the existing `TravelTracker.Api`, `TravelTracker.Data`, and `TravelTracker.Domain` responsibilities. `src/Database/schema.sql` is an existing schema; it is not evidence that EF Core or all planned features are implemented.
- Put each new feature/use-case implementation in its own file. Keep shared types and persistence separate; keep `Program.cs` focused on startup and composition.
- Use `/** ... */` documentation with `@param` and `@return` for new/substantially changed member documentation. Preserve unrelated existing comments.
- For feature implementation, refactoring, architecture, or comments, read and use [traveltracker-code](skills/traveltracker-code/SKILL.md).
- For adding/updating/running/investigating tests or validating behavior changes, read and use [traveltracker-testing](skills/traveltracker-testing/SKILL.md).
- Follow the product's ownership/privacy rules. Keep credentials and personal data out of source and logs.
- The normal checks, from the repository root, are `dotnet restore TravelTracker.slnx`, `dotnet build TravelTracker.slnx --configuration Release --no-restore`, then `dotnet test TravelTracker.slnx --configuration Release --no-build --no-restore` when test projects exist. Report discovered test counts and pre-existing build failures accurately.
- Follow `CONTRIBUTING.md` for branch/PR work; an explicitly requested branch name takes precedence over its naming recommendation. A task request determines whether to commit, push, or open a PR.
- Source mapping and the setup-time baseline are recorded in [skills-sources.md](../docs/skills-sources.md); recheck changing facts against current files.
