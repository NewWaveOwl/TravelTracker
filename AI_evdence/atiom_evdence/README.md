# AI evidence

This folder preserves the AI-assisted work behind TravelTracker Milestone 2. The source main snapshot is recorded in the [manifest](archive-manifest.md). Original prompts/responses retain the scope they had when written; the older standalone APIs, schedules, search/PATCH and 114-test suite are historical.

| Evidence | Location | What it proves |
| --- | --- | --- |
| Human requests and assistant outcome summaries | [Conversation record](conversation/conversation-record.md) | Scope decisions and later corrections, with direct excerpts distinguished from summaries |
| Issue drafting and interview prompts/responses | [Issue #9 evidence](issue-9/evidence) | Drafting, priming, supplied human answers and fresh-session planning |
| Planning review | [Issue #9 Copilot review](issue-9/copilot-review.md) | Corrections made before the reviewed planning handoff |
| Implementation plan and source review | [Session provenance](implementation/copilot-evidence.md), [plan](implementation/copilot-plan.md), [review](implementation/copilot-review.md) | Actual Copilot sessions and the limits of their contribution |
| Additional original invocations | [Prompts](implementation/prompts), [responses](implementation/responses), [metadata](implementation/session-metadata.json) | Plan/foundation/review input, public output and recorded session IDs; machine-local paths normalised |
| Full historical validation | [Verification history](verification/verification-history.json) | Earlier build failures, test reductions, architecture migrations and later passing results |
| Integration and split audits | [Layer audit](verification/integrated-layer-audit.json), [test split](verification/test-split-audit.json), [live checks](verification/test-split-live-results.json) | Source preservation, unchanged test bodies and actual live HTTP results |
| Lecture/video mapping | [Skill sources](skills-sources.md) | How the supplied teaching material informed the repository guidance |
| Main's reusable AI setup | [Snapshot index](setup-snapshot/README.md) | The configuration/instructions used; these snapshots do not configure this branch |

The [development flow](../../devlopmet_flow/atiom_developmet_flow/README.md) connects these records to the accepted specification, implementation, validation and review. The original 31 documents selected for cleanup are all represented in the manifest; earlier verification is restored from Git history.

The conversation record is a reconstruction from visible messages, not an exported full chat transcript. No missing human answer or additional Copilot session is invented. Authentication material, internal reasoning and local runtime/package folders are excluded.
