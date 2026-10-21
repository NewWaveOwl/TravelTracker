# Milestone 3 tracking and submission workflow

Scope: [M3-02 / #28](https://github.com/NewWaveOwl/TravelTracker/issues/28), the existing approved 24-item batch #27–#50, and [M3-24 / #50](https://github.com/NewWaveOwl/TravelTracker/issues/50) for final submission evidence.

## Platform and decisions

The repository owner selected GitHub Issues/Projects, Actions and pull requests on 2026-10-14, reconfirmed in this task on 2026-10-21. No Azure migration is required. The supplied Milestone 3 handout names Azure DevOps PRs and Lea submission; instructor acceptance of GitHub has not been independently verified. The coordinator must confirm acceptance before final submission without inventing instructor signoff.

On 2026-10-21 the owner explicitly selected `milestone_3` as integration and the named `feature/milestone-3-layered-service-development` branch for milestone development. Work-item PRs target the named feature branch; the final milestone PR targets `milestone_3`. [CONTRIBUTING](../../CONTRIBUTING.md) defines commands and retention.

#27 remains the architectural dependency. Preparing this board/workflow does not resolve the database, identity provider, frontend or trip-contract choices owned by #27. Dependent production implementation stays blocked until its actual approved contract is linked.

## Board and ownership

The live board link and verified configuration are recorded in [validation](issue-28-validation.md). [board.json](board.json) is the submission-friendly issue/owner/group/dependency export. The issue's live engineering checklist is the task breakdown; do not duplicate or replace the 24 issues with draft cards.

One accountable assignee is required per issue. Preserve these selected owners:

| Member | Account | Milestone issues |
| --- | --- | --- |
| Artiom | `NewWaveOwl` | #28, #31, #34, #35, #36, #43, #44, #50 |
| Christel | `ChristelEdee` | #29, #32, #33, #37, #38, #45, #46 |
| James | `5unekku` | #27, #30, #39, #40, #41, #42, #47, #48, #49 |

Use a single-select **Feature group** matching each issue and the original Assignees field. Show **Dependencies** as linked issue references; native blocked-by links are the navigable source of truth. Preserve any additional pre-existing dependencies. Do not mark all items Done because the board exists or because a related PR merged.

| Status | Entry / exit rule |
| --- | --- |
| Backlog | No open blockers; research or Gate 1 approval is not yet complete. |
| Blocked | An unresolved prerequisite, contract, failed check or Critical/High finding prevents the next step; link the reason. |
| Ready | Dependencies resolved and a developer approved the versioned implementation plan. |
| In progress | Approved implementation or independent setup is underway. |
| In review | Implementation/evidence is available in a PR; Gate 2 or peer signoff remains pending. |
| Done | All acceptance criteria, Gates 1–3, required CI and review resolutions are evidenced, and the PR is merged. |

Status changes are evidence-based and owned by the accountable developer/coordinator. Do not enable an automatic "merged PR/closed issue → Done" workflow: closing a GitHub issue cannot prove its human gates. Leave missing signoffs pending. If a completed item regresses, move it to Blocked with a defect link.

## Three human-gate records

Use [.github/pull_request_template.md](../../.github/pull_request_template.md) for each work-item and milestone PR. Each record identifies the human, date, revision and permanent evidence link; unchecked placeholders are not evidence.

GitHub automatically offers PR templates only from the repository default branch, which remains `master`. Until this template reaches that branch through a separately approved PR, copy this milestone template into the PR description explicitly (or supply the file as the PR tool's body). This work does not change the repository default or claim automatic template discovery is active. See [GitHub's template rules](https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/creating-a-pull-request-template-for-your-repository).

1. **Gate 1 — specification/plan review, before implementation.** Link the issue acceptance, current contract/dependencies and a file-by-file plan. Record the developer's approval of that revision. Any material scope or contract change returns to Gate 1. For #28, the approved [plan](issue-28-plan.md) records the user's actual chat decision.
2. **Gate 2 — code/test verification, after implementation.** The developer inspects the diff, verifies actual executed tests and failure cases, checks layering/security as applicable and confirms no staged secrets. Record exact commands, commit, warning/error counts, discovered/passed/failed/skipped counts and limitations. Code changes require zero warnings/errors; documentation-only work uses appropriate consistency/link checks and explains non-applicable runtime checks. Existing warnings remain visible under #31; an incremental build does not prove they are fixed.
3. **Gate 3 — peer review/signoff, before merge.** Run the read-only review agent, inspect the severity report and resolve/recheck Critical/High findings. Another team member verifies Gates 1–2 and the human checklist and submits an actual GitHub approval for the latest revision. Artiom's PR can be reviewed by Christel or James; authors cannot approve their own PRs. Record the review permalink, SHA and any accepted lower-severity findings. An agent verdict or typed checkbox is not peer approval.

The reviewer verifies endpoint/DTO behavior, dependency direction, domain/application rules, EF ownership/atomicity, frontend isolation and the real bearer-token security matrix when those are affected. For this tracking-only change these application checks are not applicable; branch rules, issue ownership and board/record accuracy are applicable.

Severity: **Critical** (immediate severe exposure/data loss), **High** (security/correctness/acceptance blocker), **Medium** (material weakness) and **Low** (minor). Critical/High findings block merge. Resolve Medium findings or obtain an explicit peer disposition; record Low follow-up decisions. Never suppress findings or assert an unperformed agent/peer review.

## Traceability and protections

Every implementation commit includes `Refs #N` or `Closes #N` in its subject/body, and every PR links its work item. Use `Refs #N` while acceptance/dependencies remain incomplete. GitHub closing keywords only auto-close issues when the PR merges into the repository default branch; because these milestone PRs have other bases, explicitly verify issue state after merge and close manually only after all gates pass.

Both milestone branches use PR-only updates with one peer approval, stale approval dismissal, latest-push approval, conversation resolution, required `test`, and no force pushes/deletion or bypass actors. Repository automatic head-branch deletion stays off. The existing default-branch rules remain unchanged. A human reviews gate evidence before approving; GitHub's branch rules enforce review/CI, not the truth of prose in a plan or checklist.

See the verified rule links in [validation](issue-28-validation.md). Do not probe protection with a write that changes or deletes a shared branch.

## Sample and final evidence index

[sample-pr.md](sample-pr.md) demonstrates the required description and signoff fields. It is explicitly a template with pending human evidence, not a fabricated approved PR.

| Required evidence | Source / completion responsibility |
| --- | --- |
| Repository | [NewWaveOwl/TravelTracker](https://github.com/NewWaveOwl/TravelTracker) |
| Published board / export | Live link in [validation](issue-28-validation.md); [board.json](board.json) |
| Approved issue batch and engineering tasks | #27–#50, preserved checklists and assignees |
| Versioned specification and team decisions | #27; link actual approvals, not inferred checklist state |
| Work-item PRs / plans / agent reports / gate logs | Each issue's linked PR; gather through #50 |
| CI and security evidence | Exact-SHA check-run/test links; #49 and each service issue |
| Architecture/reflection report | #50; 2–3 pages, actual service/client allocation and outcomes |
| Milestone PR | Head `feature/milestone-3-layered-service-development` → base `milestone_3`; title **Milestone 3: Layered Service Development with AI Agents** |
| Submission | Coordinator supplies the actual milestone PR URL and checks reviewer access; retain the milestone branch after merge |

Create the milestone PR when the named branch contains reviewed work. At bootstrap it can match `milestone_3` exactly, so there is no honest non-empty milestone PR yet. Do not link a compare page as if it were a created PR or claim final submission is complete. The #28 work-item PR and any remaining blockers are recorded in its validation evidence.
