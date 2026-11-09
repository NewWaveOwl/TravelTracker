# Milestone 3 Architecture and Reflection
 
## 1. Layered backend architecture and frontend integration choices

**Owner:** James

*Draft written from the repository state; the owner should review it before submission.*

Six backend services run independently, each with its own Domain, Data and Api project: Attractions (port 5104), Transport (5105), Accommodations (5106), Settings (5107), Trips (5108) and Users (5101). Dependencies point one way, Api to Data to Domain. A boundary test fails the build if a Domain project gains a reference or a Data project becomes a web project. Each service owns its PostgreSQL database and never reads another service's tables. Services talk to each other only over versioned HTTP contracts: for example, Trips confirms every place and transport mode with the owning service before it saves, and asks Users for the live account standing. If any of them is unavailable, the save fails closed and stores nothing.

The frontend is a Blazor Web App in interactive Server mode (`src/TravelTracker.Frontend`). It has no project reference to any backend project, so it can only use the public HTTP DTOs. It replaced an earlier same-day React choice by owner decision on 2026-11-03. It calls the services from the server, so the browser needs no CORS configuration or proxy, and the access token stays in a server-owned session. The decision is recorded in `docs/milestone-3-contracts.md`.

Sign-in uses Auth0 through the ASP.NET Core OpenID Connect handler with PKCE. Every API validates the bearer token's signature, issuer, audience and expiry through one shared `TravelTracker.Security` project, so the rules are written once and not per service, then enforces a scope policy per operation (`catalogue.read`, `catalogue.write`, `users.read`, `settings.read`, `settings.write`, `trips.read`, `trips.write`). The full endpoint-to-policy table is in `docs/milestone-3-contracts.md` and `docs/authentication.md`. No secret is committed: the validation settings are authority, issuer and audience only.

Trade-offs: the Trips save has no idempotency key, so a network failure after a send leaves the outcome unknown (the client says so). Trip history, editing and deletion are not implemented. Scheduled routes, stays and visits were left out of the minimum trip slice by an owner decision.

---

## 2. How work was divided across the team

**Owner:** [name]

*Draft from the owners recorded on the GitHub issues; the owner should add how conflicts and drift were handled.*

| Member | Services | Frontend work | Shared work |
|---|---|---|---|
| Artiom | Attractions, Transport | Attraction views (M3-17), transport and route-point selection (M3-18) | Milestone board and submission workflow (M3-02), compiler warnings (M3-05), bearer authentication and scope policies (M3-08) |
| Christel | Users, Accommodations | Frontend shell (M3-06), sign-in (M3-07); profile (M3-19) and accommodation views (M3-20) merged in PRs #86 and #87 | Layer agents (M3-03) |
| James | Trips, Settings | Settings view (M3-22), trip creation and details (M3-21) | Architecture and contracts (M3-01), testing guidance (M3-04), CI and validation (M3-23) |

Each member owns two independently runnable services, as the milestone requires. The matching verification for each frontend view lives in `tests/TravelTracker.Frontend.Tests`.

---

## 3. How skill-based agents accelerated development and where they required manual correction
 
**Owner:** Christel
 
### Setup
 
We created four layer agents under `.github/agents/`: `domain-dev`, `data-persistence`, `api-service` and `frontend-dev`.
 
### Where the agents helped
 
- **Scoped context.** Each agent started from its own layer's rules, so prompts such as "Add a repository method to find attractions by name" did not have to restate conventions like one primary type per file, documentation comment style, or `PublicId` as the only public identifier.
- **Built-in validation.** Once the agents could run commands, they ran the build and tests themselves and reported results. In the trials, the build succeeded each time. The baseline suite was 147 passing tests, and the domain trial reached 152 with its temporary regression cases.
- **Layer boundaries held.** When asked to add a controller endpoint, `data-persistence` refused, changed no files and named `api-service`. `domain-dev` refused a request to add EF Core attributes and a DbContext to the Domain.
- **Avoiding duplicate work.** For "add a DTO and a GET endpoint for transport modes", `api-service` found the implementation already existed, made no edits and verified the existing HTTP tests instead.
- **A clear stop when information is missing.** `frontend-dev` declined to invent a framework while the frontend stack was undecided, and changed nothing.
We did not time the work, so the speed benefit is qualitative. The clearest gains were less re-explaining of conventions and fewer cross-layer edits.
 
### Where manual correction was needed
 
| Area | What happened | How it was corrected |
|---|---|---|
| Boundary reply wording | The first `domain-dev` refusal said "the data agent" instead of the exact name `data-persistence`. Later, `data-persistence` listed all three other agents instead of one. The `domain-dev` file also once contained another agent's name in its reply template. | Reworded the boundary section to "choose exactly one agent" and named each agent's responsibility. Used `git grep` across the agent files to compare them. A teammate review caught that one fix had been recorded as done but was missing from the file. |
| Unrequested design choice | In the `domain-dev` rating trial, the agent enforced the 0 to 5 range by throwing an exception inside the model. Nobody asked for that, it duplicates validation already in the request DTO and the service, and it risked a 500 response instead of the expected 400. | Reviewed the diff, checked the build and tests, and removed the trial change, because the trial was meant to prove scope and not to ship model changes. |
| Missing tools | The agents were first created with only search, usages, problems and edit tools. They could not run builds or tests, although the skills they pointed to require validation. The `data-persistence` trial reported that it could not run the build. | Gave the agents a way to run the build and test commands, then re-ran the checks. This is a design decision we had to make deliberately, because nobody had chosen it. |
| Conflicting test ownership | The plan gave the same HTTP test files to two agents. Copilot noticed the conflict, stopped and asked instead of picking one. | Assigned one owner per test file. The HTTP behavior tests belong to `api-service`, and `data-persistence` keeps only its own generator test. |
| Trial records | The first version of the trials document contained unfilled placeholders and marked every row as passed. A teammate flagged it in review. | Replaced placeholders with real command results, recorded only what was actually run, and marked the document as historical evidence. |
| Stale agent definitions | The `frontend-dev` refusal was correct while the stack was undecided. After the frontend was chosen and built, the definition and the trial result were out of date. New service projects also needed path updates before the agents would touch them. | Updated the agent definitions as the architecture changed. The trials document now says which results are historical, and the interactive trials are still to be repeated. |
 
### What we learned
 
- **Agents follow templates literally.** A small ambiguity in the boundary wording produced either vague or wrong refusals. Specific wording, such as "choose exactly one agent", fixed it.
- **Agent summaries are fluent and not always accurate.** Fixes were sometimes reported as done when the file had not changed. The reliable checks were reading the diff, searching the files with `git grep`, and running the build and tests ourselves.
- **Agents take design decisions if nobody stops them.** The rating exception is an example. A human still has to decide what belongs in each layer.
- **Agent definitions need maintenance.** They are part of the codebase, and they go stale when the architecture, the stack or the test layout changes.
- **Boundaries work best when they are enforced by checks and not only by instructions.** The agent instructions are not enforced by tooling, so the trial prompts and the review process were what confirmed that scope held.

 
## 4. Effectiveness of the human validation gates
 
**Owner:** [name]
 
TODO. Suggested points to cover:
 
- Which gates existed: plan approval, diff inspection, build and test requirements, agent review, teammate approval.
- Defects each gate caught, with examples.
- Defects that slipped through a gate, and why.
- What we would change.
[Write here]