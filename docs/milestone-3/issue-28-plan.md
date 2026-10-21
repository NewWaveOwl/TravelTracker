# M3-02 implementation plan

Issue: [#28](https://github.com/NewWaveOwl/TravelTracker/issues/28). Research base: `milestone_3` at `c644e04`, 2026-10-21.

The user selected the named milestone feature branch followed by a PR into `milestone_3` and explicitly approved this file-by-file plan in the implementation chat on 2026-10-21 (answer: "Approve this plan"). This records Artiom's Gate 1 decision only; it is not peer approval or #27 contract approval. The user is updating the existing credential's Projects access. GitHub Issues/Projects/Actions/PRs remain the chosen platform. This is tracking/documentation work; no application contracts, database or identity-provider decisions are made here.

## Research

- The approved batch is the existing 24 issues #27–#50. Each has one assignee, engineering checklists, acceptance criteria and textual blocking references. Preserve #27 = James (`5unekku`), #28 = Artiom (`NewWaveOwl`), #50 = Artiom.
- #27 remains open. Its one checked approval criterion has no linked versioned contract or member approval records in comments. Do not infer that dependent implementation is ready.
- `milestone_3` is the user's integration branch; GitHub's repository default is still `master`. This task does not change the default branch.
- Existing active rules target the default branch only. `milestone_3` is not protected by those rules. Automatic branch deletion is already off.
- The existing CI job is `test`, runs on every PR and currently covers the shared API. Wider service/client checks belong to #49.

## Changes by file / external resource

| File or resource | Implementation |
| --- | --- |
| `CONTRIBUTING.md` | Document milestone/work-item branch bases, PR targets, safe update policy, retention and three human gates; retain normal work guidance. |
| `.github/pull_request_template.md` | Require issue, versioned plan, actual approvals, exact-SHA validation, severity-based read-only findings and another member's GitHub approval. |
| `docs/milestone-3/workflow.md` | Board/status policy, ownership, dependency rules, commit conventions, branch protections, GitHub-only decision and submission evidence index. |
| `docs/milestone-3/sample-pr.md` | Clearly labelled unapproved example showing all required records without inventing human approval. |
| `docs/milestone-3/board.json` | Export the 24 issue references, owners, feature groups and dependencies for review/submission. No credentials. |
| `docs/milestone-3/issue-28-validation.md` | Actual consistency/link checks, live board/rule evidence, limitations and review status. |
| `README.md` | Link the milestone workflow and evidence. |
| GitHub Project | Reuse a matching project or create one, link this repository, add the 24 existing issues, expose Status/Assignees/Feature group/Dependencies and preserve unfinished statuses. |
| GitHub issues | Add the milestone and native dependency relationships matching existing descriptions; preserve bodies, checklists and assignees. |
| GitHub ruleset | Protect `milestone_3` and the named feature branch with PR-only updates, one other member's approval, fresh review, resolved threads, required `test`, no force pushes/deletion, no bypass actors. |
| Git branches and PR | Create the named milestone branch from `milestone_3`; open this issue's work-item PR against it. The final milestone PR is created after the feature branch has reviewed changes; retain it after merging. |

## Verification and completion

1. Verify exactly 24 distinct issues, one expected owner per item, all dependency targets valid and no cycles; compare export to live issues.
2. Verify the published board's issues, fields/statuses, repository link and dependency links. Keep blocked/unapproved items out of Done; do not auto-close on PR merge.
3. Read back active branch-rule settings and disabled automatic branch deletion. Use read-only inspection instead of attempting an unauthorized direct push to a protected branch.
4. Check Markdown links, JSON structure, branch/PR target consistency and `git diff --check`. Existing application CI runs on the PR; documentation changes add no application tests.
5. Record the read-only review and any findings. Human Gate 1/2/3 signoffs must come from the actual developers/peer; never fabricate them. #28 remains pending review/dependencies until its acceptance is evidenced.

No service code, frontend, Azure setup, member impersonation or instructor acceptance is part of this task.
