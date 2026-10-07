# Attraction management priming interview

**Human interview answers received on 2026-10-06.** The user confirmed VS Code with GitHub Copilot, C# and ASP.NET Core on .NET 10, and a standalone Attractions API on port 5104 with seeded in-memory data. The feature was revised from search alone to **attraction management and name search**. These replace the earlier provisional defaults.

The [human answer record](evidence/human-interview-answers.md) preserves the supplied requirements.

## Confirmed human decisions

| Topic | Human answer |
| --- | --- |
| Feature | Attraction management and name search |
| Environment | VS Code with GitHub Copilot; C# and ASP.NET Core on .NET 10 |
| Starting point | Standalone Attractions API, port 5104, seeded in-memory data |
| Fields | Generated GUID, required name, optional description, optional manually entered rating 0-5, address, lat/lon and estimatedVisitMinutes |
| Read routes | Health, list, name search and details by ID |
| Write routes | POST creates; PATCH updates selected fields by ID |
| Create response | 201 Created, saved attraction and details URL |
| Update response | 200 OK with updated attraction |
| Validation | Names cannot be empty/whitespace; rating inclusive 0-5 or null; invalid input 400; unknown ID 404 |
| Data lifetime | In-memory changes last until restart |
| Search | Trimmed case-insensitive literal name substring; empty returns all; no match 200 []; order by name then GUID |
| Verification | Test health, list, details, search, creation and updates, including invalid inputs and unknown IDs |

## Contract details completed during planning

The [specification](feature-spec.md) distinguishes the user's answers from the additional binding decisions: PATCH omission preserves fields, explicit null clears optional description/rating, server IDs are immutable, invalid updates have no side effects, coordinate/duration validation is explicit, and duplicate search parameters are rejected. No additional human answer is attributed for those implementation choices. The old proposed search-length bound is removed to match the revised search rules.

The standalone service decision is now an explicit human requirement rather than an unresolved exception to generic shared-scaffold guidance. The service owns its model/store; the rest of the repository remains outside the feature. Product ownership and #10/#11 phase assignments are unchanged.

## Agent interview and planning record

Copilot CLI session `bcf1c82a-fe8f-495c-bbbb-c3783bb9e15f` generated the original search draft and [priming questions](evidence/copilot-prime-questions.md) before the user answered. The initial fresh-session search plan remains historical evidence. After the human expanded the scope, a revised Copilot draft and a new fresh-session management plan were generated and reviewed against these confirmed answers. See [Copilot review](copilot-review.md) for session evidence and corrections.

The human-preferences checklist in #9 can now be checked. Specification and planning completion do not imply that the future API implementation or its tests have been completed.
