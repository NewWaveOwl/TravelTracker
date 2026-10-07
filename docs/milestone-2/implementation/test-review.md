# Test overlap review and 30-case selection

The human requested at most **30 discovered xUnit cases total**, retaining the LAB_07-style single test file per independent API. Baseline: commit `628a4da972070760855407826c9aacca50c312fe`, 53 Attractions and 61 Transport cases passing. Current selection: **15 Attractions + 15 Transport = 30**, all passing.

## Duplicate check

The source audit compared the 31 original method bodies after whitespace normalization and checked repeated `[InlineData]` rows within each method. It found **zero identical bodies and zero repeated rows**. Separate health and missing-resource tests target different APIs/resources and remain meaningful. Shared setup and Location assertions are supporting code, rather than duplicate scenarios.

Manual review found repeated paths: six transport entry creations use one mode validator, six overnight route cases use one supplied-time calculation, and many invalid-field variations exercise the same reject-before-write boundary. Some omitted cases exercise distinct rules; the reduction is a deliberate smaller milestone suite, not a claim that all 84 fewer executions were duplicates.

## Case selection

| Area | Before | Now | Decision |
| --- | ---: | ---: | --- |
| Attractions health and seeded list/schema | 2 | 2 | Retained |
| Attraction detail IDs | 3 | 2 | Unknown/malformed retained; creation Location proves successful detail reads |
| Attraction creation | 21 | 4 | Saved representation/Location, rating 5, blank name, ratings below 0/above 5 |
| Name search | 7 | 3 | Combined spaces/mixed case, empty query, no match |
| Attraction PATCH | 17 | 3 | Partial/null/zero update, atomic invalid candidate, unknown ID |
| Direct attraction store checks | 2 | 0 | Rejection overlaps HTTP atomic update; earlier-snapshot immutability is omitted |
| Attraction host reset | 1 | 1 | Retained |
| **Attractions total** | **53** | **15** | |
| Transport health | 1 | 1 | Retained |
| Transport catalogue | 11 | 2 | Six seeded codes, one bus creation without a schedule, unsupported mode |
| Route points | 11 | 2 | One create-points-to-journey workflow covering all three roles and source metadata; invalid latitude |
| Transport resource IDs | 3 | 3 | Unknown/malformed IDs for each collection; Location checks prove successful reads |
| Valid scheduled routes | 7 | 2 | Multi-stop timing and one direct overnight plane route |
| Invalid schedules/references | 27 | 4 | Missing timestamp offset, equal arrival/departure, boarding exceeding dwell, unknown destination |
| Transport host reset | 1 | 1 | Retained |
| **Transport total** | **61** | **15** | |

The point workflow creates accommodation, attraction and transport-stop locations, then references those new points in one scheduled journey. This is one connected workflow, rather than a hidden copy of the former validation matrix. Ratings on both invalid sides and each transport resource remain separate theory cases. No skip attributes or test filters were added.

## Coverage tradeoff

The reduced suite omits explicit cases for malformed JSON, missing required create fields, unknown/client-owned fields, extra coordinate/duration/type/null inputs, literal-symbol decoding and repeated query keys, GUID tie ordering, concurrent PATCH and direct snapshot immutability. Transport also omits POST for every supported mode, unsupported point type, all missing-reference permutations, stop-order rejection, negative/null preparation, malformed/non-ISO times, range overflow and every mode's overnight calculation.

These implemented rules were not removed or relaxed. The remaining happy multi-stop case proves ordered calculations and leave-vehicle gating; it does not replace the omitted invalid stop-order case. The smaller suite provides representative milestone coverage and does not certify every documented boundary. Future changes can revisit the selection or the human's current limit.

## Verification

Both owned test projects were rebuilt and executed in Release: 15/15 passed for each, zero failed/skipped. There are 23 methods: 18 facts and five theories with 12 rows, yielding 30 discovered cases. [Structured evidence](verification.json) records the baseline, source hashes, duplicate checks and current counters. [Validation](validation.md) retains earlier live-process evidence separately. No additional Copilot review or live-process run is claimed for this follow-up.
