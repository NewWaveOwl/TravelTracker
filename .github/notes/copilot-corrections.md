# Copilot corrections log: Issue #10 (Implement, R-PIV loop)

Feature: Attraction list/name search, PATCH, and POST hardening (Issue #9)
Branch: milestone2_christel
Tool: GitHub Copilot Agent mode in [VS Code / Visual Studio 2026]

## Corrections

| # | Phase | What Copilot did wrong | How I caught it | How I corrected it |
|---|-------|------------------------|-----------------|--------------------|
| 1 | Prime | Could not reach the GitHub MCP server and read local copies of the issue and plan in `docs/milestone-2/issue-9/` instead | Its first reply said so | [Fixed MCP setup / used local copies after checking they matched the issue] |
| 2 | Prime | The plan proposed a standalone `Attractions.Api` project, which contradicts `copilot-instructions.md` (extend the existing `TravelTracker.Api`) | Copilot flagged the conflict itself | Chose option A (extend the existing projects) and told the team the plan was out of date |
| 3 | Prime | Repeated a stale triage note about an `Id64` compile failure | Compared with the instructions, then ran the build | Baseline build passed, so the note is outdated |
| 4 | Research | Step 1 summary did not report the build result | Asked | Confirmed separately that the build succeeded |
| 5 | Plan | PATCH design was over-engineered: custom JSON converter, HasX flag DTO, new `Validation/` folder | Reviewed the design before approving | Switched to binding PATCH to `JsonElement`, validation inside `AttractionService`, and a small `UpdateResult` type |
| 6 | Plan | Validation error path was undefined (`UpdatePartialAsync` returned `Attraction?`, which cannot carry errors) | Reviewed the design | Required an explicit return type, which became `UpdateResult` |
| 7 | Implement | Ignored "list/search only" and implemented PATCH instead; claimed search was "implemented previously" | Compared the diff with my instruction | Asked it to show the GET list action and to add tests before moving on |
| 8 | Implement | Added PATCH code with no PATCH tests (the "21 passed" were old tests) | Checked what the count covered | Required PATCH tests in `AttractionTests.cs` |
| 9 | Implement | Gave a muddled answer on rejecting unknown JSON properties on POST and proposed a custom converter or model binder | Read the answer | Told it to use `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` on `CreateAttractionRequest` |
| 10 | Implement | Atomicity explanation was confidently wrong: a retry loop in the repository causes lost updates when PATCHes are built from a stale copy. It also contradicted itself about comparing to the earlier snapshot | Walked through two concurrent PATCHes by hand | Moved the fix into `AttractionService`: one static `SemaphoreSlim` around read, merge, validate and write, released in `finally` |
| 11 | Implement | Edited `InMemoryAttractionRepository.cs` during a read-only request | Checked Source Control | Told it to revert, and confirmed no diff on that file |
| 12 | Validate | Created `ServiceTests.cs`, which breaks the repo's test-file convention | Compared with `copilot-instructions.md` | Moved the tests into `AttractionTests.cs` and deleted the new file |
| 13 | Validate | Test count did not match the tests it listed (31 to 34, only two named) | Checked the arithmetic | Asked for the full list; the third was the HTTP-level concurrency test |
| 14 | Validate | Concurrency test was not shown to fail without the fix | Asked for proof | Semaphore removed temporarily and the HTTP test failed with lost updates, then the lock was restored |
| 15 | Plan | A later plan step said "implement creation" although POST already existed | Read the file list from step 1 | Replaced it with "POST hardening" (unknown property, blank name, omitted address, trimming) |
| 16 | Implement | Did not update `docs/api.md` or `requests.http` until asked | Checked the plan's file list | Asked it to update both |

## Process problems (repeated)

- Auto-started the next plan step after "completing" a step, even after I told it to stop. I added "stop and wait" to nearly every prompt.
- Skipped some questions I asked (test list, atomicity explanation, concurrency proof) and I had to ask again.
- Summaries were fluent and confident even when wrong, so reading them was not enough. I checked the diff, the test list and the behavior myself.

## What worked well

- Restating the problem before coding caught the plan vs instructions conflict early.
- Reading `feature-spec.md` instead of guessing the null rules (explicit null clears description and rating, and is invalid for the other fields).
- Using `JsonElement` and `TryGetProperty` kept PATCH presence-aware without custom infrastructure.
- Asking for a failing test before trusting a concurrency fix.

## What still needed human judgment

- Choosing option A over the plan's standalone project.
- Simplifying the PATCH design.
- Spotting the lost-update bug in the "retry" fix.
- Enforcing the repo's file and test conventions.
- Deciding when a step was really done and when to commit.

## Test count progression

21 (baseline) -> 31 (PATCH and list tests) -> 34 (service and concurrency tests) -> 36 (POST hardening) -> 38 (final)


## Known trade-offs

- The `static SemaphoreSlim` serializes all PATCH requests across the process. This is fine for an in-memory demo store but would not scale.
- The in-memory store is not durable and resets on restart.
