# Attraction-name-search interview

Recommendations below are **provisional defaults**, not human answers. Questions 1–3 were already sent to the human; questions 4–7 are new risk questions.

1. **Previously sent — Is attraction-name search the feature to plan?**
   **Recommended:** Yes. It is a narrow, user-visible catalogue capability and fits the documented place-search direction.

2. **Previously sent — Which workflow should the plan target: VS Code, Visual Studio, or CLI?**
   **Recommended:** Keep the plan IDE-agnostic and use the .NET CLI for reproducible build and test steps; the repository documents `dotnet` commands.

3. **Previously sent — Should the work include a minimal runnable Attractions API, or wait for the broader baseline to be fixed?**
   **Recommended:** Scope the minimal independent Attractions API prerequisite separately—seeded list, details, and health—then add search. Define completion around that service and focused tests; record full-solution blockers separately rather than expanding this feature to fix them. The current API has no routes or DI, and the repository has unrelated compile-blocking/missing types.

4. **New — Who owns the implementation, and should it follow the M1 independent-service boundary?**
   **Recommended:** Artiom, subject to confirmation. Keep the work limited to Attractions; the M1 assignment requires an independently runnable service, so it should own its types and seeded store. Do not restructure all six services or share models/repositories/context with separately owned services.

5. **New — What ID and JSON contract should search preserve?**
   **Recommended:** Use GUID identifiers serialized as JSON strings, matching the current `Attraction` model and M1 requirement. Avoid introducing `Id64` for this endpoint until the public contract is reconciled.

6. **New — Which matching behavior and repeatable checks should define search?**
   **Recommended:** Trim the query; absent or whitespace-only means the full list. Match name substrings case-insensitively using ordinal comparison for the in-memory seed; treat wildcard characters literally; reject trimmed queries over 100 UTF-16 characters with `400 ProblemDetails`; return `200 []` for no matches. Sort by name (ordinal, case-insensitive), then GUID. Add deterministic HTTP tests for these boundaries and retain the unknown-ID detail-route `404`.

7. **New — May the search expose only the public M1 seed catalogue, and what must remain out of scope?**
   **Recommended:** Yes. Use fixed public examples (Montreal Museum, Science Centre, Old Port) only; do not imply that private-catalogue access or M2 authorization exists. Keep pagination, fuzzy/geospatial search, UI, database/event work, and unrelated service repairs out of scope. Track the existing full-solution build blockers— including the `Id64` issue and missing referenced types—separately and report them accurately.
