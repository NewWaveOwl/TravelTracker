---
name: code
description: Write or refactor TravelTracker C# and ASP.NET Core code with one primary type per file, clear API/Data/Domain boundaries, dependency injection, and /** @param */ documentation. Use for feature implementation, architecture changes and code comments; use testing for tests and validation for pre-commit checks.
---

# TravelTracker code

## Before editing

1. Read `README.md`, `CONTRIBUTING.md`, the relevant `docs/` files and the existing code. Separate what is implemented from what is only planned.
2. Know the requested behavior, the owning service, the affected files, the public contract and the acceptance cases. Ask only about decisions that change behavior or compatibility.
3. Implement what was asked. No unrelated cleanup. Keep existing contracts, identifiers and names unless changing them is the task.

## Structure

- **Scope today:** Attractions, Transport entries and Route Points, served by one host (`TravelTracker.Api`, Development Swagger on port 5104). Listing, search, PATCH, scheduled journeys and stop-time calculations are future work unless requested.
- **One resource, one cohesive feature.** GET-by-ID and POST live together in that resource's controller, service and repository. No per-action partial files, no parallel hosts, no duplicate entities or interfaces.
- **One primary type per file.** Models, DTOs, requests, interfaces, repositories and strategies each get their own file. Small private helpers can stay with the type they support. Use a named feature folder when it helps.
- **`Program.cs` is the composition root:** configuration, DI registration, middleware, route mapping. Feature handlers live elsewhere.
- **No extra architecture** (mediator, CQRS, new layers, separate deployables) for a small feature. Do not split a cohesive shared interface into one-method interfaces, or reorganize files, unless the task needs it.
- `Trip`, `TripLeg`, `UserSettings` and `TripMapPoint` are empty partial declarations that keep existing interfaces compiling. They are not implemented features and must not be described as such.

## Layers and DI

| Project | Owns |
| --- | --- |
| `TravelTracker.Api` | Thin HTTP handlers (bind, validate, call, map to a response) and services (business rules, coordination). |
| `TravelTracker.Data` | Repositories and persistence. Loads and stores; does not decide permissions or trip validity. |
| `TravelTracker.Domain` | Entities, value types, enums, domain rules. No HTTP, EF Core or vendor SDK dependencies. |

- Constructor injection for services, handler parameters for endpoint dependencies. Register at the composition root; no `IServiceProvider` lookups in business code.
- Scoped for request services, repositories and `DbContext`; transient for light stateless pieces. A singleton must not capture a scoped dependency. Typed `HttpClient` for remote calls.
- Today's in-memory repositories are thread-safe singletons that return completed Tasks to satisfy the async interfaces. Mutable catalogue entities are copied on store and read; `RoutePoint` is an immutable record. Response DTOs keep the `name`/`lat`/`lon` aliases. Do not change existing interface signatures or model properties. `Id64` is `internal` and visible to Data only, so Api code cannot name or expose it.
- Nullable reference types stay on. Use async I/O and pass `CancellationToken` through new I/O. Change an existing contract only when compatible or explicitly part of the task.

## Patterns, when the feature needs them

- **REST:** resource routes, consistent semantics. `201` with a location on create; `404` for absent or inaccessible private resources; validation problem details for bad input; request DTOs without server-owned fields. Follow `docs/api.md`. Do not assume a `{id:guid}` route constraint works for `Id64`.
- **LINQ:** filter and project before materializing, order deterministically before paging, `Any` for existence, handle missing records explicitly. Queries belong in persistence, business calculations in services.
- **EF Core:** `AsNoTracking` for reads. Respect service data ownership; no cross-service table access. Review migrations, constraints, concurrency and transaction boundaries. Save a trip and its children atomically.
- **Repository / Unit of Work:** reuse the existing specific repository contracts. Add a generic one only to hide persistence or coordinate several writes. `DbContext` already is a unit of work.
- **Strategy:** only for genuinely interchangeable algorithms (for example per-mode route calculation). One file per implementation; the service picks it, passes data in, and keeps persistence outside. A simple stable rule can stay a conditional.
- **Services and events:** respect the documented owner and talk through explicit contracts. When messaging is requested: immutable past-tense events with id and version, idempotent consumers, atomic dedup plus side effects, bounded retries, a dead-letter path, a DI scope per message. Do not add a broker for an unrelated feature.
- **Security:** validate at trust boundaries; enforce authentication plus ownership, role and status. Validate token issuer, audience, signature, expiry and permissions. No secrets or personal data in source or logs. Safe problem responses; CORS and rate limits that fit the endpoint.

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

Lecture and video provenance: [skills-sources.md](https://github.com/NewWaveOwl/TravelTracker/blob/milestone_2_AI_and_development_resource/AI_evdence/atiom_evdence/skills-sources.md).
