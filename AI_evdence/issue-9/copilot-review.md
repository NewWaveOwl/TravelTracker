# Copilot draft and plan review

The first two reviews record the initial search-only scope before the human answered. The confirmed management review below supersedes the old pending-answer status and proposed query-length bound.

GitHub Copilot CLI 1.0.92 generated the [initial issue draft](evidence/copilot-issue-draft.md) in session `bcf1c82a-fe8f-495c-bbbb-c3783bb9e15f` on 2026-10-06. The same session then generated the [priming questions](evidence/copilot-prime-questions.md). Both ran with file-reading tools only; no implementation, repository write or GitHub publication tool was available to Copilot.

Codex reviewed the draft against the source, M1 assignment and live issue triage before accepting the revised specification for publication. This is an agent review, not a claim that the human answered the priming questions or manually approved each edit.

## Initial search draft review

| Draft finding | Edit in the reviewed specification |
| --- | --- |
| Refers to an existing detail route | Corrected: no route exists; list/detail/health are a scoped runnable prerequisite. |
| Independent baseline should be tracked separately | Made the prerequisite a separate reviewed commit and explicit checklist phase; allow reuse if a teammate supplies it. It does not claim all M1 CRUD is complete. |
| Preserve M1 shape, without spelling out fields | Added exact GUID JSON fields, including explicit `Latitude`/`Longitude` to `lat`/`lon` mapping. |
| Broad test bullets | Added observable case/status/body matrix, length boundaries, tie ordering, literal metacharacters, repeat parameters and once-decoded query. |
| Unknown ID mentioned but malformed ID unspecified | Added unknown GUID `404` versus malformed GUID `400`, so route constraints do not accidentally decide the public contract. |
| Baseline build blocker identified | Required separate full-solution evidence and explicit fix/defer record in #11; focused service checks cannot be called a passing solution build. |
| Ownership subject to confirmation | Separated Attractions ownership from implementation/validation phase leads without changing teammate assignments. |
| Human confirmations pending | Kept planning defaults explicit in the interview record. No human answer or approval was invented. |

The 100 UTF-16-unit limit, substring semantics and deterministic order are planning decisions. The lectures supply REST, DI, repository, LINQ and test patterns; they do not prescribe those product limits. The existing [source mapping](../skills-sources.md) identifies the supplied PDFs and videos.

## Initial search plan review

The initial plan came from new session `df53555f-cd5b-4f36-b2dd-83ab434ad806` with the reviewed specification, triage and interview as context. Copilot CLI reported no modified files in either session. Its default model selected `gpt-6-luna`; no model override was requested.

| Initial plan finding | Adjustment in the final plan |
| --- | --- |
| One `AttractionEndpoints.cs` groups list and detail use cases | Split proposed endpoint files by list, detail and health, and test files by feature. |
| A binding-only commit accepts a filter before matching exists | Combine binding/validation/filtering into one complete search commit with passing checks. |
| Tests arrive after all implementation steps | Define failing search checks after the prerequisite, then implement and validate them with #11 coordination. |
| `Program.TestHost.cs` location ambiguous | Place the separate partial Program file in the API assembly. |
| Mutable process-wide seed unspecified | Require immutable seed/read snapshots and appropriate DI lifetimes. |
| Validation described only at HTTP boundary | Keep query cardinality binding in HTTP and normalization/length rules in the use case for all callers. |
| Solution tests follow build unconditionally with `--no-build` | Gate solution testing on a successful build to avoid stale assembly evidence. |
| Literal/tie/length tests could be weak | Require positive literal-symbol fixture, fixed equal-name/GUID cases and UTF-16-specific boundary cases. |

The original search plan incorporated these changes; it is superseded by the confirmed management plan below. Original response text and prompts are preserved as evidence; terminal punctuation artifacts were normalized without changing the draft's meaning. Raw runtime logs, hidden reasoning and credentials are excluded.


## Confirmed management scope review

The [human answer record](evidence/human-interview-answers.md) expands the feature to management/search and confirms the environment and standalone baseline. Copilot generated a [revised issue draft](evidence/copilot-management-draft.md) in session `e7f8b346-b11a-4909-9c48-eb4366616e6d`; a distinct new session `92e3a511-b590-4152-bb4b-afef271f5f07` generated the [revised initial plan](evidence/copilot-management-initial-plan.md). Both reported no modified files and used the CLI-selected `gpt-6-luna` model. Neither resumed an earlier planning conversation.

The reviewed specification incorporates the revised draft and the human's requirements. It spells out the exact representation, acceptance matrix and source of each decision, removes the previous proposed search-length bound, and treats unknown IDs on details/valid PATCH as 404. Malformed input is a 400 problem response; only validation failures require field-error entries, rather than attributing a field to every malformed JSON error.

| Revised initial plan finding | Adjustment in the reviewed handoff |
| --- | --- |
| Project path uses `src/Attractions.Api` | Standardized on `src/TravelTracker.Attractions.Api` to match repository naming and all commands. |
| Separate list/search endpoints could duplicate one GET route | One list route dispatches to focused list/search use cases; no competing route registration. |
| Steps lack a concrete file/commit map | Added service-owned DTO/store/validation files, per-feature endpoints/tests, and passing checkpoints for read, create, PATCH and search. |
| Compound concurrency operation described only broadly | Required synchronized read/candidate-validation/replace, or deliberate version conflict handling; omitted fields must survive concurrent changes. |
| Targeted and solution gates lack executable commands | Added project-specific commands, executed counts and a successful-build gate before no-build solution tests. |
| Restart/reset test mentioned without process evidence | Added isolated-host lifecycle tests plus actual service restart smoke evidence. |

The [current implementation plan](../../devlopmet_flow/issue-9/implementation-plan.md) contains the reviewed management scope. Historical pre-answer outputs remain evidence, not live requirements. No API code or tests were changed by this planning revision.
