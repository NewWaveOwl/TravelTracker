# Copilot confirmed scope prompt

```text
GitHub Copilot: revise the feature issue draft for TravelTracker #15 under planning task #9 after the human answered the priming interview. This is planning only. Read docs/milestone-2/issue-9/feature-spec.md and priming-interview.md, AGENTS.md, repository instructions and both skills. Return a concise revised title and issue draft; do not edit files, implement, publish or delegate. The original search-only scope is superseded.

Human-confirmed: Attraction management and name search; VS Code with GitHub Copilot, C#/ASP.NET Core .NET10; standalone API port5104 with seeded in-memory store. Fields generated GUID, required name, optional description, optional manually entered decimal rating0-5 or null, address, lat/lon, estimatedVisitMinutes. GET health/list/search/details, POST creates with201 saved representation and Location, PATCH selected fields with200 updated representation. Blank names/rating outofrange400, unknown GUID404. Trim caseinsensitive literal name substring, emptyall/no match200[], order nameGUID. Changes persist across requests until restart. Verify every endpoint invalid input/unknownID. No DELETE.

Reviewed planning refinements in spec: PATCH omission vs explicit null; ID readonly; whole-record validation then atomic store replacement, safe shared process lifetime store/snapshots; coordinate/duration validation; no previous arbitrary100-char search bound. Distinguish these details from direct human answers. Current shared scaffold has no implemented endpoints or tests and has unrelated Id64/missing type build blockers. Respect owning independent service, one usecase/responsibility per file and /** @param @return */ comments. Reflect actual current scope, not historical unresolved interview text.

```
