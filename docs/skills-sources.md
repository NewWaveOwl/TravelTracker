# Lecture alignment for TravelTracker skills

Reviewed 2026-10-21 against the supplied `WEB_3/lectures` folder. This is an instruction review, not certification that the application implements every course requirement. The PDFs are external course materials; teammates can follow the synthesized skills without local copies. Exercises and sample deployment commands are source material, not authorization to execute extra tasks.

## Instruction ownership

- `.github/skills/code/SKILL.md`: production coding, service boundaries, persistence, integration and security.
- `.github/skills/testing/SKILL.md`: test levels, deterministic evidence and failure paths.
- `.github/skills/validation/SKILL.md`: checks before commit/PR and requested CI/delivery work.
- `.github/skills/planning/SKILL.md`: reconcile requested milestones, product contracts and observed implementation.
- `.github/copilot-instructions.md` and `AGENTS.md`: shared guidance and routing. `.claude/skills/` wrappers point to the canonical skills; do not duplicate their bodies.

The one-primary-type-per-file and `/** @param @return */` conventions come from the user. The original one-host demo, resource/test filenames and 30-case cap are historical user constraints, not lecture standards or permanent architecture. This revision retires the obsolete 30-case cap: the current milestone_3 suite already executes 41 tests. Preserve useful organization and add meaningful coverage according to the requested behavior. Existing product/privacy contracts still apply.

## Complete supplied PDF inventory

Numbered and unnumbered versions are listed separately because the numbered lectures include additional diagrams and context. Do not assume they are identical. All 31 supplied PDFs were text-extracted for the review; visual layout was not audited. The image asset and saved-page support folder do not add coding requirements.

| Supplied file | Application to guidance |
| --- | --- |
| `ASP.NET Core Web API with Minimal APIs _ 420-5W6-Notes.pdf` | code: endpoint style, DTOs, HTTP semantics, OpenAPI; learning storage limits |
| `Authentication and OAuth2 _ 420-5W6-Notes.pdf` | code/testing: validated bearer tokens, policy permissions, middleware order, auth failure tests |
| `Azure DevOps CI_CD_ Delivery _ 420-5W6-Notes.pdf` | validation: promote one tested artifact, environment approvals, secrets, health verification, rollback |
| `Azure DevOps CI_CD_ Testing _ 420-5W6-Notes.pdf` | testing/validation: restore-build-test, TRX/coverage publishing, enforce agreed gates |
| `Databases and Entity Framework Core _ 420-5W6-Notes.pdf` | code/testing: service-owned data, tracking, migrations, relational integration, atomic writes |
| `Dependency Injection _ 420-5W6-Notes.pdf` | code/testing: composition root, lifetimes, captive dependencies, replaceable fakes |
| `Dependency Injection _ IoC Patterns _ 420-5W6-Notes.pdf` | code: constructor/method injection and appropriate keyed services |
| `Event Bus_ Foundations _ 420-5W6-Notes.pdf` | code: immutable versioned events, producer abstraction, consumer-owned state |
| `Event Bus_ Reliable Patterns _ 420-5W6-Notes.pdf` | code/testing: transactional outbox, idempotency, bounded retry, scoped consumers, dead letters, ordering |
| `Generic Repository and Unit of Work (EF Core, MVC) _ 420-5W6-Notes.pdf` | code/testing: optional wrappers, shared scoped context, one commit, factory/provider boundaries |
| `How to Setup GitHub Copilot in Your Project _ 420-5W6-Notes.pdf` | shared instructions/validation: limited context, human review, inspect generated changes, protect secrets |
| `Introduction to SOA _ 420-5W6-Notes.pdf` | code/planning: capability ownership, explicit compatible contracts and typed clients |
| `Lecture 08_ Event Bus_ Reliable Patterns _ 420-5W6-Notes.pdf` | code/testing: transactional outbox, idempotency, bounded retry, scoped consumers, dead letters, ordering |
| `Lecture 09_ Authentication and OAuth2 _ 420-5W6-Notes.pdf` | code/testing: validated bearer tokens, policy permissions, middleware order, auth failure tests |
| `Lecture 10_ Security Foundations _ 420-5W6-Notes.pdf` | code/planning: threat model, trust boundaries, least privilege, secret configuration and validation |
| `Lecture 11_ Security in Practice _ 420-5W6-Notes.pdf` | code/testing: secure transport, safe errors, correlation/audit, dependency review, CORS/rate limits |
| `Lecture 12_ Testing Fundamentals _ 420-5W6-Notes.pdf` | testing: levels, Arrange-Act-Assert, fakes/stubs/mocks, deterministic unit and HTTP contract tests |
| `Lecture 13_ Testing in Practice _ 420-5W6-Notes.pdf` | testing: dependency failures, asynchronous flows, auth failures, repeatable evidence, coverage limitations |
| `Lecture 14_ Azure DevOps CI_CD_ Testing _ 420-5W6-Notes.pdf` | testing/validation: restore-build-test, TRX/coverage publishing, enforce agreed gates |
| `Lecture 15_ Azure DevOps CI_CD_ Delivery _ 420-5W6-Notes.pdf` | validation: promote one tested artifact, environment approvals, secrets, health verification, rollback |
| `lecture_1.pdf` | code/planning: SOA introduction; same extracted text as Introduction to SOA in this review |
| `LINQ Syntax and Usage _ 420-5W6-Notes.pdf` | code: deferred execution, filter/projection, missing values, stable paging, query ownership |
| `Microservice Architecture _ 420-5W6-Notes.pdf` | code/planning: independent services/configuration, failure contracts, timeouts and health checks |
| `Milestone 1_ SOA Foundation _ 420-5W6-Notes.pdf` | code/planning: at least two independently runnable services per member when this milestone is requested |
| `Repository Pattern _ 420-5W6-Notes.pdf` | code/testing: use-case queries, business rules in services, master/detail ownership and atomicity |
| `Security Foundations _ 420-5W6-Notes.pdf` | code/planning: threat model, trust boundaries, least privilege, secret configuration and validation |
| `Security in Practice _ 420-5W6-Notes.pdf` | code/testing: secure transport, safe errors, correlation/audit, dependency review, CORS/rate limits |
| `SOAP and REST _ 420-5W6-Notes.pdf` | code: protocol chosen for consumer contract, HTTP methods/status/Location, typed adapters |
| `Strategy Pattern in a REST API _ 420-5W6-Notes.pdf` | code/testing: validated unique business codes, algorithm isolation, no save for preview, commit boundaries |
| `Testing Fundamentals _ 420-5W6-Notes.pdf` | testing: levels, Arrange-Act-Assert, fakes/stubs/mocks, deterministic unit and HTTP contract tests |
| `Testing in Practice _ 420-5W6-Notes.pdf` | testing: dependency failures, asynchronous flows, auth failures, repeatable evidence, coverage limitations |

## Other supplied files

- `LINQ Syntax and Usage _ 420-5W6-Notes.html` and its `_files` directory are a saved-page representation and supporting resources for the LINQ lecture. The PDF supplied the LINQ guidance used here; assets are not independent instructions.
- `laptop-perspective-side-isolated-white-background_187299-15988.avif` is an illustration asset, not a coding-practice source.

## Limits and historical provenance

The archived setup also cites `Skills and Agents _ 420-5W6-Notes.pdf` and two video transcripts. They were not supplied in this lecture folder and were not reverified in this review. Preserve those as historical provenance rather than asserting they were reviewed now. The 2026-10-06 build baseline is historical, not the current project status.

[Original setup source mapping](https://github.com/NewWaveOwl/TravelTracker/blob/cbb0d84e2f0994bf30b9a57838b6872d8752c4ce/AI_evdence/atiom_evdence/skills-sources.md) is retained on the evidence branch. This current document supplements it without rewriting original evidence.

## Interpretation safeguards

- Milestone 1 explicitly calls for at least two independently runnable services per team member. A single-host demo does not prove this outcome; implementation remains scoped to the user's request.
- Transactional outbox and consumer deduplication solve different failure windows. Outbox dispatch may repeat publication, so it does not remove the need for idempotent consumers.
- Lecture retry snippets and pipeline echo steps illustrate concepts; they do not prove complete dead-letter handling, quality-gate enforcement or a working deployment.
- JWT payload decoding and fake test identities do not establish cryptographic token validation. Test the real authentication boundary when that guarantee is requested.
- Migration review, provider-specific tests and post-deployment checks must match the claimed guarantee; a successful build alone is insufficient.
- OAuth2 delegates authorization; use OIDC for user authentication flows when applicable. APIs consume access tokens for their intended audience rather than treating an ID token as an API access token. These distinctions accompany the lecture's IdP/client/resource-server boundaries.
