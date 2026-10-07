---
name: traveltracker-code
description: Write or refactor TravelTracker C# and ASP.NET Core code using one feature per file, service/repository boundaries, dependency injection, and /** @param */ documentation. Use for feature implementation, architecture changes, and code comments; use traveltracker-testing for test authoring and investigation.
---

# TravelTracker coding and commenting

## Establish the feature

1. Read `README.md`, `CONTRIBUTING.md`, the relevant `docs/` files, and the existing code before editing. Identify what exists versus what is only planned.
2. State the requested behavior, owning service, affected files, public contract, and acceptance cases. Keep the plan proportional to the change; clarify only decisions that affect behavior or compatibility.
3. Implement the requested feature without unrelated cleanup. Preserve existing contracts, identifiers, and naming unless their change is part of the request.

## One file, one feature

- Apply this layout to production code. Test organization follows `traveltracker-testing`: one xUnit project with test files grouped by resource/purpose and separate shared helpers, as requested by the human on 2026-10-07.
- The current demonstration uses the original TravelTracker.Api host with TravelTracker.Data repositories and TravelTracker.Domain models for all three resources and one Development Swagger page on port 5104, like LAB_07. Do not split resource controllers into separate hosts unless requested.
- For the current LAB_07-style demonstration, one resource is one cohesive feature. Keep GET-by-ID and POST together in its controller, service and repository; do not create per-action partial files. Attractions, Transport entries and Route Points have separate resource classes.
- Implement only the requested operations. Collection listing/search, PATCH, scheduled journeys and stop-time calculations are future work unless the human requests them.
- Keep one primary responsibility/type per file. Small private helpers may support that same feature. Give reusable models, DTOs, interfaces, repositories, and strategies their own files.
- A feature may use files in several layers; this rule does not put HTTP handling, business rules, and persistence into one class. Organize feature-specific files under a named feature folder when useful.
- Keep `Program.cs` as the composition root: configuration, dependency registration, middleware, and calls to feature route-mapping methods. Put feature handlers outside it.
- Extend the existing API/Data/Domain projects. Api is the only web host; do not create a parallel WebApi or duplicate Attraction/Transport interfaces/entities. Unfinished trip/settings types are empty partial declarations for compilation, and must not be described as implemented features. Do not introduce extra layers, a mediator, CQRS, or independently deployed services just to implement a small feature.
- Existing shared interfaces can describe one cohesive capability. Do not split every interface method into a new abstraction or reorganize existing files unless needed for the requested work.

## Layer responsibilities and dependency injection

- `TravelTracker.Api`: thin HTTP handlers and application services. Handlers bind/validate requests, call a business operation, and map its outcome to an HTTP response; services own business rules and coordination.
- `TravelTracker.Data`: persistence implementations, contexts, migrations, and repositories. Repositories load/store data and expose operations needed by the use case, without deciding business permissions or trip validity.
- `TravelTracker.Domain`: entities, value types, enums, and domain rules, without HTTP, EF Core, or vendor SDK dependencies.
- Use constructor injection for service dependencies and handler parameters for endpoint dependencies. Register implementations at the composition root; avoid constructing repositories or locating dependencies with `IServiceProvider` in business code.
- Use scoped lifetimes for request services, database repositories and `DbContext`; transient for lightweight stateless dependencies. In-memory repositories in Data are thread-safe singletons; services in Api use the original asynchronous interfaces. Memory methods return completed Tasks to honor those signatures. Attraction/Transport entities come from Domain; response DTOs preserve name/lat/lon aliases. Keep every existing interface signature and original model property. RoutePoint is an immutable Domain record; mutable catalogue entities are copied on storage/read. A singleton must not capture a scoped dependency. Use typed `HttpClient` registrations for remote calls.
- Keep nullable reference types enabled. Use asynchronous I/O and pass cancellation tokens through new I/O operations; update an existing contract only when the change is compatible or explicitly part of the feature.

## Apply course patterns when the feature needs them

- **REST:** use resource routes and consistent HTTP semantics. Return `201` with a location for creation, `404` for absent/inaccessible private resources, validation problem details for invalid input, and the documented forbidden/unavailable responses. Use request DTOs that omit server-owned fields. Simple public immutable records may be returned directly when their fields are exactly the approved response contract. Follow `docs/api.md`; do not assume a `{id:guid}` constraint works for `Id64`.
- **LINQ:** filter/project before materialization, order deterministically before paging, use `Any` for existence, and handle missing records explicitly. Keep database queries in persistence and business calculations in services.
- **EF Core:** use `AsNoTracking` for read-only queries and tracking for updates. Keep each service's data ownership explicit; do not add cross-service table access. Review migrations, constraints, concurrency, and transaction boundaries. Save a trip and its child records atomically within its owning store.
- **Repository/Unit of Work:** reuse the project's specific repository contracts. Add a generic repository/UoW only when it helps hide persistence or coordinate multiple writes. When using UoW, repositories share one scoped context, stage changes without saving, and the service commits once. `DbContext` already supplies UoW behavior; simple features do not require a wrapper.
- **Strategy:** introduce a strategy interface only for meaningful interchangeable algorithms, such as travel-mode route calculation. Give each implementation its own file; select an allowed business code in a service, pass in the required data, and keep persistence/commit decisions outside the algorithm. A simple stable rule can remain a conditional.
- **SOA and events:** respect the documented service owner and communicate through explicit contracts. When messaging is requested, use immutable past-tense events with identity/version information, idempotent consumers, atomic deduplication and side effects, bounded retries for transient failures, and a dead-letter path. Create a dependency scope per background message; document ordering and eventual consistency. Do not add a broker for an unrelated feature.
- **Security:** validate at trust boundaries, enforce authentication plus ownership/role/status authorization, and protect another user's private records. Validate bearer-token issuer, audience, signature, expiry, and required permissions when token authentication is used. Keep secrets outside source/logs, use safe problem responses and structured audit/correlation information, and apply CORS/rate limits appropriate to the actual endpoint.

## Required comment style

- Use `/** ... */` blocks with a leading `*` on each content line for new or substantially changed type/member documentation, including test helpers. This is the user's convention and already appears in service/repository interfaces.
- Start with a short behavior/purpose sentence. Add one `@param` per parameter using its exact name; document cancellation tokens and meaningful constraints.
- Add `@return` for a value or `Task`, describing the awaited result, null/not-found behavior, or completion. Add `@throws` only for exceptions the contract actually promises.
- Explain non-obvious decisions and invariants. Keep comments synchronized with signatures and behavior; avoid narrating obvious statements or converting unrelated existing comments.
- These blocks are ordinary C# comments, not compiler-generated XML documentation. Do not silently substitute `/// <summary>`/`<param>` for the requested format.

Example method declaration documentation:

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

## Validate and hand off

1. Review the diff for one-feature-per-file organization, correct layer boundaries, compatible contracts, and accurate comments.
2. Build `TravelTracker.slnx` and use `traveltracker-testing` for behavior changes. For a comment-only edit, validate documentation and build as appropriate without inventing tests that merely check wording.
3. Report changed behavior/files, checks and their results, pre-existing failures, and any remaining gaps. Follow `CONTRIBUTING.md` for branch work and respect an explicitly requested branch name.

Lecture and video provenance: [skills-sources.md](../../../docs/skills-sources.md).
