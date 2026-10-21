## Change and work item

Refs #<!-- issue number -->

<!-- Explain the problem, resulting behavior and scope. Use Closes #N only when
all acceptance criteria are satisfied. Milestone PR: include all work-item PRs. -->

Base branch: `feature/milestone-3-layered-service-development`
<!-- Final milestone PR instead targets milestone_3. -->

## Gate 1 — specification and plan

- Issue / acceptance criteria:
- File-by-file implementation plan (permanent link / revision):
- Developer approval (member, date, evidence link):
- Contract/dependency decisions or justified not-applicable items:

- [ ] A developer approved this plan before implementation.
- [ ] Blocking dependencies and all behavior/security decisions are resolved.

## Gate 2 — code and test verification

- Reviewed commit SHA:
- Commands and results (build warnings/errors; tests discovered, passed, failed, skipped):
- Edge/failure cases, frontend or independent-host verification, if applicable:
- Developer diff review and staged-secret check (member, date, evidence link):
- Limitations / unverified acceptance criteria:

- [ ] A developer reviewed the actual diff and evidence for this commit.
- [ ] Applicable build/tests pass; code changes have zero warnings/errors.
- [ ] Layer dependencies, public DTOs and applicable security cases were verified.
- [ ] No credentials, connection-string secrets or unrelated/generated files are staged.
<!-- For documentation-only work, record link/consistency checks and explain
why application tests or architecture/security cases are not applicable. -->

## Agent review — findings, not human approval

- Read-only reviewer / tool and reviewed SHA:
- Report link (severity, file/line, impact, suggested correction):
- Critical/High finding resolutions and recheck links:
- Remaining findings and disposition:

- [ ] The read-only review was run and its report was inspected.
- [ ] All Critical/High findings are resolved and rechecked.

## Gate 3 — peer signoff before merge

- Peer member (must be another team member, not the author):
- GitHub approval review permalink and reviewed SHA:
- Checklist verification / accepted limitations:

- [ ] Another team member verified Gates 1–2 and approved this revision in GitHub.
- [ ] Required CI checks pass and review conversations are resolved.
- [ ] Every acceptance criterion is met before the issue is closed or moved to Done.

<!-- A checkbox or agent verdict is not approval. Leave unknown/unperformed
records pending. Do not merge on the strength of this template alone.
See docs/milestone-3/workflow.md. -->
