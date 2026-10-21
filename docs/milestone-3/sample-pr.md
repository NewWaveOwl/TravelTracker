# Example milestone work-item PR description

**Illustrative template only — all example approvals below are pending.** Replace placeholders with actual evidence before merge. This file does not record a completed review or satisfy a gate by itself.

## Change and work item

Refs #28 — M3-02: Set up the milestone board and GitHub submission workflow.

Publish the existing 24 issues on one board, preserve ownership/dependencies, document the branch strategy and require traceable developer/peer gates. No service implementation or architectural contract decision is included.

Head: `setup-milestone-board-workflow`.
Base: `feature/milestone-3-layered-service-development`.

## Gate 1 — specification/plan approval

- Specification: [#28 acceptance](https://github.com/NewWaveOwl/TravelTracker/issues/28).
- Plan: [file-by-file changes](issue-28-plan.md); replace this relative link with the reviewed commit permalink in GitHub.
- Developer: `<actual member and date>`.
- Decision/evidence: `<permanent link to explicit approval of this plan revision>`.
- Dependency: #27 remains open; this setup PR does not approve service contracts.
- Gate result: **pending actual developer record**.

## Gate 2 — code/test verification

- Reviewed revision: `<full commit SHA>`.
- Developer diff/secret check: `<member, date, evidence link>`.
- Checks: `<commands and results; counts of errors/warnings and executed tests>`.
- Documentation-only applicability: `<JSON/ownership/dependency/link checks and live board/rule readback>`.
- Runtime/security changes: none for this example; for code changes attach applicable HTTP, relational, bearer-validation and frontend results.
- Gate result: **pending actual developer verification**.

## Read-only agent review

- Reviewer/tool and exact SHA: `<actual run>`.
- Severity report: `<permanent link>`.
- Each finding: `<Critical/High/Medium/Low; file:line; impact; suggested fix; resolution/recheck link>`.
- Open Critical/High: `<actual count; must be zero>`.
- Example failure handling: a High finding that allows direct pushes keeps this item Blocked until protection is fixed and rechecked. This is an example, not a claim that such a review occurred.

## Gate 3 — other-member approval

- PR author: `<actual author>`.
- Peer: `<different member, e.g. Christel or James for Artiom's PR>`.
- GitHub approval: `<real approval-review permalink and reviewed SHA>`.
- Peer verifies: approved plan, actual validation, accepted scope, no secrets, all critical findings resolved, required CI passed.
- Gate result: **pending actual GitHub peer approval**.

Do not tick a signoff on someone else's behalf. Keep the issue In review/Blocked until evidence exists. Done requires all acceptance, resolved dependencies and merged work; this example alone authorizes no merge.
