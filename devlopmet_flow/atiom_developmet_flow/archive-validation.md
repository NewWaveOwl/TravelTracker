# Archive validation

Checked on 2026-10-07 against main `68fe7ea6bd11b7d02a0af3e2fe913d55857880fe`.

| Check | Result |
| --- | --- |
| Main evidence/workflow preservation | All **31 records** represented in the manifest; original text preserved, only Markdown link targets relocated |
| Setup snapshots | **6 exact copies**; original and archived SHA-256 values match |
| Historical verification | Restored JSON byte-for-byte from commit `2921aef`; older test counts/architectures explicitly labelled historical |
| Existing resource branch | Original README text retained; no pre-existing file deleted |
| Navigation | 54 Markdown documents checked, 193 local links checked, zero unresolved targets after writing this report |
| JSON and credential check | JSON parses; no recognised GitHub/OpenAI credential values copied |
| Conversation | Direct request excerpts distinguished from summaries; incomplete-export limits and the unconfirmed AI-setup default are explicit |

This archive task does not claim a new application build/test run. The [preserved validation](implementation/validation.md) records earlier actual results: build zero warnings/errors, 19 HTTP cases and eight live checks. The deletion PR separately checks current main source/tests and links after removing evidence.

[Manifest](../../AI_evdence/atiom_evdence/archive-manifest.md), [development process](development-process.md), [conversation record](../../AI_evdence/atiom_evdence/conversation/conversation-record.md).
