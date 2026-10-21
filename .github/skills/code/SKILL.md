---
name: code
description: Write or refactor TravelTracker C# and ASP.NET Core code with one primary type per file, clear API/Data/Domain boundaries, dependency injection, and /** @param */ documentation. Use for feature implementation, architecture changes and code comments; use testing for tests and validation for pre-commit checks.
---

# TravelTracker code

## Before editing

1. Read `README.md`, `CONTRIBUTING.md`, the relevant `docs/` files and the existing code. Separate what is implemented from what is only planned.
2. Know the requested behavior, the owning service, the affected files, the public contract and the acceptance cases. Ask only about decisions that change behavior or compatibility.
3. Implement what was asked. No unrelated cleanup. Keep existing contracts, identifiers and names unless changing them is the task.

## Sources and scope

Course material informs the requested work; exercises are not additional tasks. Follow explicit user scope and distinguish lecture requirements from repository facts and personal conventions. The one-primary-type-per-file and `/** @param @return */` styles are user conventions, not mandated lecture syntax. See [lecture alignment](../../../docs/skills-sources.md).

## Structure

- **Historical demo context (reinspect before use):** Attractions, Transport entries and Route Points, served by one host (`TravelTracker.Api`, Development Swagger on port 5104). Listing, search, PATCH, scheduled journeys and stop-time calculations are future work unless requested.
- **One resource, one cohesive feature.** GET-by-ID and POST live together in that resource's controller, service and repository. Avoid per-action partial files and duplicate entities or interfaces. Preserve the host layout for a scoped feature; a requested SOA milestone can require additional independently runnable services.
- **One primary type per file.** Models, DTOs, requests, interfaces, repositories and strategies each get their own file. Small private helpers can stay with the type they support. Use a named feature folder when it helps.
- **`Program.cs` is the composition root:** configuration, DI registration, middleware, route mapping. Feature handlers live elsewhere.
- **No extra architecture** (mediator, CQRS, new layers, separate deployables) for a small feature. Do not split a cohesive shared interface into one-method interfaces, or reorganize files, unless the task needs it.
- **Verify current state:** `Trip`, `TripLeg`, `UserSettings` and `TripMapPoint` are empty partial declarations that keep existing interfaces compiling. They are not implemented features and must not be described as such.

## Layers and DI

| Project | Owns |
| --- | --- |
| `TravelTracker.Api` | Thin HTTP handlers (bind, validate, call, map to a response) and services (business rules, coordination). |
| `TravelTracker.Data` | Repositories and persistence. Loads and stores; does not decide permissions or trip validity. |
| `TravelTracker.Domain` | Entities, value types, enums, domain rules. No HTTP, EF Core or vendor SDK dependencies. |

- Constructor injection for services, handler parameters for endpoint dependencies. Register at the composition root; no `IServiceProvider` lookups in business code.
- Scoped for request services, repositories and `DbContext`; transient for light stateless pieces. A singleton must not capture a scoped dependency. Typed `HttpClient` for remote calls.
- **Historical demo context, verify against current source:** in-memory repositories are thread-safe singletons that return completed Tasks to satisfy the async interfaces. Mutable catalogue entities are copied on store and read; `RoutePoint` is an immutable record. Response DTOs keep the `name`/`lat`/`lon` aliases. Preserve interface signatures and model properties unless a compatible evolution or requested contract change requires otherwise. `Id64` is `internal` and visible to Data only, so Api code cannot name or expose it.
- Nullable reference types stay on. Use async I/O and pass `CancellationToken` through new I/O. Change an existing contract only when compatible or explicitly part of the task.

## Patterns, when the feature needs them

- **REST:** resource routes, consistent semantics. `201` with a location on create; `404` for absent or inaccessible private resources; validation problem details for bad input; request DTOs without server-owned fields. Follow `docs/api.md`. Do not assume a `{id:guid}` route constraint works for `Id64`.
- **LINQ:** filter and project before materializing, order deterministically before paging, `Any` for existence, handle missing records explicitly. Keep provider queries inside persistence, business calculations in services, and simple response projection at the HTTP boundary. Understand deferred execution and deliberate materialization; use `SingleOrDefault` only when uniqueness is a real invariant, and handle missing join partners explicitly.
- **EF Core:** `AsNoTracking` for read-only queries; tracking for updates. Respect service data ownership; no cross-service table access. Review migrations, constraints, concurrency and transaction boundaries. Save a trip and its children atomically within its owning database. Review generated migration SQL, keys, indexes, foreign keys and destructive changes. Commit migrations and snapshots; add migrations rather than rewriting applied history. Do not mix `EnsureCreated` with migration-managed databases.
- **Repository / Unit of Work:** reuse the existing specific repository contracts. Add a generic one only to hide persistence or coordinate several writes. `DbContext` already is a unit of work; direct context use in a service can be appropriate. With a UoW wrapper, repositories share one scoped context, stage changes without saving, and the service commits once. Keep `IQueryable` inside persistence. A repository factory may resolve scoped implementations; business services must not become service locators.
- **Strategy:** only for genuinely interchangeable algorithms (for example per-mode route calculation). One file per implementation; the service picks it, passes data in, and keeps persistence outside. Validate allowed, unique business codes; never resolve a client-supplied .NET type. A quote/preview does not save unless requested. A simple stable rule can stay a conditional.
- **Services and events:** respect the documented owner and talk through explicit contracts. When messaging is requested: immutable past-tense events with id and version, idempotent consumers, atomic dedup plus side effects, bounded retries, a dead-letter path, a DI scope per message. Events must contain enough data to avoid querying producer-owned tables. For a database change that must reliably publish an event, write business state and an outbox record in one local transaction; dispatch pending messages separately. Publication may repeat, so enforce consumer deduplication uniqueness and atomic side effects. Retry transient failures with bounded backoff, retain dead-letter reasons, and document replay, ordering and eventual consistency. Do not add a broker for an unrelated feature.
- **Security:** validate at trust boundaries; enforce authentication plus ownership, role and status. Validate access-token issuer, audience, signature, expiry and permissions. For user sign-in use OIDC where applicable; authorization-code clients use PKCE. Keep signing metadata/keys with the identity-provider integration rather than trusting decoded payloads. No secrets or personal data in source or logs. Run authentication before authorization; distinguish unauthenticated and forbidden responses according to the contract. Identify assets, trust boundaries, threats, mitigations and residual risks for security-sensitive work. Use least privilege, secure transport, safe problem responses with correlation IDs, structured durable audit records where required, restrictive CORS and justified rate limits. Review affected dependencies and secret configuration; never treat decoded JWT claims as validated identity.

- **DI variants:** use action-level `[FromServices]` for a controller dependency needed by one action, or automatic handler injection in Minimal APIs. Keyed services suit fixed internal selections; use validated business-code selection for user-selected strategies.

## SOA, HTTP contracts and operations

- The supplied Milestone 1 requires at least two independently runnable services per team member. When that milestone is in scope, identify capability ownership, contracts, independent configuration/startup and a context diagram. Resource controllers in one host do not establish independent deployability. Report gaps without silently expanding a small feature task.
- Each service owns its persistence; communicate through versioned APIs/events rather than cross-service database joins. Define backward compatibility, network timeouts, cancellation, unavailable-dependency outcomes and health checks. Use typed HTTP clients.
- Both Minimal APIs and controllers are supported; keep one style consistent per endpoint set. Document request/response schemas, status codes and security in OpenAPI. Swagger is manual exploration, not automated test evidence.
- GET is safe; PUT and DELETE have idempotent state effects. POST is not normally idempotent without an explicit mechanism. Creation returns a useful Location. Validate paging bounds and order with a deterministic tie-breaker. SOAP/WSDL can be appropriate for an existing partner contract; do not replace it just because the application otherwise uses REST.
- JSON files and in-memory stores are learning/demo storage, not evidence of production persistence. State concurrency, restart and multi-instance limitations; choose storage from actual requirements.

## Comments

New or substantially changed type and member documentation uses `/** ... */` with a leading `*` on each line, including test helpers:

```csharp
/**
 * Finds an attraction visible to the current traveller.
 *
 * @param attractionId The identifier of the attraction to find.
 * @param cancellationToken Cancels the lookup operation.
 * @return A task containing the visible attraction, or null when absent or inaccessible.
 */
Task<Attraction?> GetByIdAsync(Guid attractionId, CancellationToken cancellationToken);
```

- Start with one sentence on behavior or purpose. One `@param` per parameter using its exact name. `@return` for any value or `Task`, covering null and not-found behavior. `@throws` only for exceptions the contract promises.
- Explain non-obvious decisions and invariants. Skip comments that restate the code. Keep them in sync with the signature.
- These are ordinary comments, not XML docs: do not substitute `/// <summary>`. Leave unrelated existing comments alone.

## Finish

1. Review the diff: one-type-per-file, layer boundaries, compatible contracts, accurate comments.
2. Run the `validation` skill, and the `testing` skill for behavior changes. Do not write tests that only check comment wording.
3. Report what changed, which checks ran and their results, pre-existing failures, and gaps. Branch work follows `CONTRIBUTING.md`.

Lecture alignment and historical provenance: [skills-sources.md](../../../docs/skills-sources.md).
