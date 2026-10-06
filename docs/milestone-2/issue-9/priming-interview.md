# Attraction search priming interview

Copilot CLI session `bcf1c82a-fe8f-495c-bbbb-c3783bb9e15f` drafted the feature issue, then performed the priming interview before planning on 2026-10-06. Its questions and recommendations are preserved in [Copilot interview questions](evidence/copilot-prime-questions.md).

Three preference questions were sent to the human in the working chat: feature choice, Copilot environment, and minimal runnable API versus waiting for a baseline. No answer had arrived when these planning documents were drafted. The decisions below are explicit planning defaults selected under the request to choose a small extension; they are **not attributed to the human**. Confirm or revise them before #10 implements the feature.

| Interview question | Planning decision and reason |
| --- | --- |
| Which small extension? | Attraction-name search: Artiom owns Attractions, the product plans place search, and no matching feature issue or runtime behavior exists. |
| Which client? | Shared VS Code MCP configuration and setup, with .NET CLI validation. Actual draft/interview/plan generation uses official Copilot CLI 1.0.92. Other team members may use their IDE with equivalent repository context. |
| Runnable API or baseline dependency? | Include only a minimal standalone Attractions prerequisite in a separate commit; reuse a compliant baseline if a teammate supplies it first. Do not rebuild all six services. |
| Who owns what? | Artiom retains Attractions ownership; phase #10 owns implementation, #11 owns validation and implementation PR, #9 owns this specification/plan. No teammate assignment was changed. |
| Which identifier and representation? | M1 GUID ID and `attractionId/name/address/lat/lon/estimatedVisitMinutes` JSON fields. Avoid a repository-wide Id64 migration. |
| What counts as a match? | Trimmed ordinal case-insensitive literal name substring; empty means all; no match means `200 []`; deterministic name/GUID order; proposed 100 UTF-16-unit bound. Duplicate query values are rejected so binding is unambiguous. |
| Which data can be searched? | Only fixed public M1 seed records. Introducing private records requires a later ownership/auth design. |
| What proves completion? | Executed unit and HTTP tests against the independent service, stable contract/health smoke checks, reviewed diff, and honest full-solution build evidence. Existing build failures cannot be hidden by service-only success. |

## Feedback supplied to the planning session

The adjusted specification distinguishes the missing baseline from the new name filter, removes the draft's claim that a detail route already exists, gives an exact JSON representation and test matrix, and keeps full-solution blockers visible. The plan must follow those decisions, identify future files rather than pretend they exist, and keep one use case/responsibility per file with the repository's `/** @param @return */` commenting convention.

The implementation plan is generated in a new Copilot session with this interview record, the reviewed specification and triage attached as repository context. It does not resume the draft/interview conversation.
