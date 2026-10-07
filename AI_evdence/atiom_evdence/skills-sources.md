# Skill sources and setup baseline

## Scope and user conventions

These two repository skills were created for TravelTracker from the supplied 420-5W6 lectures and video transcripts. The documents provide learning material; their exercises are not additional tasks to execute.

The user requested separate coding/commenting and testing skills, one feature per file, `/** ... @param ... */` comments, a preview in chat before committing, and a separate `Artiom` branch. The file organization and comment syntax are user conventions, not claims about a required lecture standard.

The setup follows `Skills and Agents _ 420-5W6-Notes.pdf`: `.github/skills/<skill-name>/SKILL.md`, matching lowercase name/folder, meaningful discovery descriptions, and focused repeatable workflows. `.github/copilot-instructions.md` follows the companion Copilot setup lecture. `AGENTS.md` routes agents to these same canonical files; it is not a new custom agent or a duplicate skill installation.

## Lecture mapping

The PDFs were supplied outside this repository. Filenames below identify the original sources; teammates do not need the local PDFs to execute the skills. The instructions synthesize the relevant patterns rather than copy lesson examples.

| Supplied lecture | Applied guidance |
| --- | --- |
| Skills and Agents _ 420-5W6-Notes.pdf | Repository skill format, discovery, focused workflows, baseline and reporting |
| How to Setup GitHub Copilot in Your Project _ 420-5W6-Notes.pdf | Durable project instructions, inspect/build/test context, review generated work |
| Introduction to SOA _ 420-5W6-Notes.pdf; lecture_1.pdf | Capability ownership, explicit contracts, typed clients; the supplied texts are duplicates |
| ASP.NET Core Web API with Minimal APIs _ 420-5W6-Notes.pdf | Thin HTTP boundary, DTOs, REST routes, DI, contract tests; JSON storage remains a learning example |
| SOAP and REST _ 420-5W6-Notes.pdf | HTTP methods, status codes, creation location, stable representations |
| Dependency Injection _ 420-5W6-Notes.pdf | Constructor injection, composition root, lifetimes, fakes, captive dependencies |
| Dependency Injection _ IoC Patterns _ 420-5W6-Notes.pdf | Handler/method injection and keyed implementations where appropriate |
| Repository Pattern _ 420-5W6-Notes.pdf | Business rules in services, use-case repositories, master/detail atomicity |
| Generic Repository and Unit of Work (EF Core, MVC) _ 420-5W6-Notes.pdf | Shared scoped context, one commit, conditional generic abstractions |
| Databases and Entity Framework Core _ 420-5W6-Notes.pdf | Service-owned storage, tracking, migrations, constraints, transactions |
| LINQ Syntax and Usage _ 420-5W6-Notes.pdf; matching HTML | Projection, deferred execution, deliberate materialization, stable paging |
| Strategy Pattern in a REST API _ 420-5W6-Notes.pdf | Interchangeable algorithms, allowed strategy codes, independent calculation tests |
| Microservice Architecture _ 420-5W6-Notes.pdf | Explicit boundaries, dependency failure contracts, health checks when relevant |
| Event Bus_ Foundations _ 420-5W6-Notes.pdf | Immutable event contracts, event identity, consumer-owned state |
| Event Bus_ Reliable Patterns _ 420-5W6-Notes.pdf | Idempotency, scoped consumers, bounded retries, dead-letter and ordering tests |
| Authentication and OAuth2 _ 420-5W6-Notes.pdf | Token validation, authentication/authorization order, permission policies |
| Security Foundations _ 420-5W6-Notes.pdf | Trust-boundary validation, least privilege, external secret configuration |
| Security in Practice _ 420-5W6-Notes.pdf | Safe errors, audit/correlation, endpoint controls |
| Testing Fundamentals _ 420-5W6-Notes.pdf | xUnit, appropriate test levels, WebApplicationFactory, public contracts |
| Testing in Practice _ 420-5W6-Notes.pdf | Deterministic evidence, failure paths, duplicates, auth, coverage interpretation |
| Azure DevOps CI_CD_ Testing _ 420-5W6-Notes.pdf | Restore/build/test flow, test-result publishing and agreed quality gates |
| Azure DevOps CI_CD_ Delivery _ 420-5W6-Notes.pdf | Test before delivery; deployment examples do not expand this setup task |

## Video mapping

- [The Ultimate Guide to Making Your Entire Development Cycle AI Native - Cole Medin](https://www.youtube.com/watch?v=Tliio-33w4g): relevant transcript sections at 33:33-44:59 explain reusable workflows, lean persistent rules, repository facts, and source control. At 1:05:04-1:19:24, research and planning connect a request to the existing code; at 1:23:49-1:25:36, a plan supports implementation and validation. The skills adapt this workflow to a focused TravelTracker task without requiring another agent, session, ticket system, or tool installation.
- [End-to-end development with GitHub Copilot + MCP server | Full Workflow Demo - GitHub](https://www.youtube.com/watch?v=HN47tveqfQU): the transcript at 2:38-3:08 demonstrates implementing a concrete issue and running tests; 3:12-3:58 demonstrates reviewing changes before branch/commit/PR work. The skills retain local validation and review. MCP configuration, GitHub issues, remote pushes, and PR creation are outside this setup request.

## Repository baseline observed on 2026-10-06

- Base commit: `7c553673edb2713e2d24eadca101eba5fe652cb0`, matching fetched `origin/master` before creating `Artiom`.
- All three project files target `net10.0`; local SDK: `10.0.401`.
- API bootstrap exists; there are no implemented routes/controllers, EF Core context, repository implementations, test projects, or CI pipeline in the inspected tracked files.
- `dotnet build TravelTracker.slnx --configuration Release` restores successfully but fails with `CS1503` in `src/TravelTracker.Domain/Types/Id64.cs:14`: the parameterless constructor passes `Id64` to a constructor expecting `long`. This predates the skills and was not changed.
- `dotnet test TravelTracker.slnx --configuration Release --no-restore` exits successfully without discovering/executing tests. This is not a passing test suite.
- Repository docs also identify planned missing types and unsettled identifier contracts. Reinspect the current build after fixing its first failure instead of assuming no other failures exist.

These are dated observations, not permanent instructions to preserve a broken build. The skills require a fresh baseline on future tasks.
