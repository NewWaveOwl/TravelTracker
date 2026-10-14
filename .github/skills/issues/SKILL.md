---
name: issues
description: Break an approved TravelTracker plan or feature into small GitHub issues that satisfy the INVEST criteria, and create or update them in GitHub. Use when asked to create issues, tasks, a backlog, or to split a feature into work items.
---

# TravelTracker issues as tasks

GitHub issues are the task list. One issue is one small piece of work that one person can finish in one branch and one PR. Source material is the approved plan from `planning` and the `docs/` contracts.

## INVEST checklist (every issue must pass)

| Letter | Check |
| --- | --- |
| **I**ndependent | Can be built and merged without another open issue, or the dependency is explicit ("Blocked by #n") and kept to a minimum. Order slices so each leaves `master` working. |
| **N**egotiable | States the outcome and constraints, not a code-level script. Implementation detail goes in a suggested approach, not the requirement. |
| **V**aluable | Delivers a visible, testable behavior (an endpoint, a validation rule, a doc contract), not "create the service layer". |
| **E**stimable | Scope is clear enough to size; unknowns are split into a separate question or spike issue. |
| **S**mall | One resource or one behavior, roughly one PR and about a day or less, few files. If the title needs "and", or there are more than about 5-7 acceptance criteria, split it. |
| **T**estable | Acceptance criteria are checkable by a test or a Swagger/HTTP call, including at least one failure path. |

Typical split of a feature: contract/docs, domain model, repository, service with validation, controller/endpoint, and each large edge behavior. Tests ride along with the behavior they test; do not make "write tests" its own issue.

## Issue template

Title: imperative and specific, under about 70 characters (`Add GET route point by id`).

```markdown
## Summary
One or two sentences: what changes for the user or API consumer, and why.
Source: docs/<file>.md section <n> (the ground truth this comes from).

## Scope
- In: ...
- Out (explicitly): ...

## Acceptance criteria
- [ ] Given ..., when ..., then ... (status code / body / side effect)
- [ ] Failure path: ... returns ... and persists nothing
- [ ] Tests added in <TestFile>.cs; `dotnet test TravelTracker.Tests.slnx --configuration Release` passes
- [ ] docs/api.md (or other doc) updated if the contract changed

## Notes
Affected projects/files, suggested approach, constraints from copilot-instructions.md.

## Dependencies
Blocked by #n (omit if none)
```

## Rules

- Do not create issues for features outside the current scope without the human's say-so; mark them "needs scope decision" in the draft.
- Search existing issues first (`gh issue list --search "<keywords>"` or the GitHub MCP tools) to avoid duplicates; update an existing issue instead of creating a twin.
- Show the human the full list of proposed issues (title, one-line summary, dependencies, order) and **wait for a clear yes before creating anything**. Creating issues is outward-facing, and approval for one batch does not cover a later batch.
- Create with `gh issue create --title ... --body-file ...` or the GitHub MCP tools (`.vscode/mcp.json`). Apply labels that already exist; do not create new labels unasked. If neither `gh` nor MCP is usable, output the issues as ready-to-paste markdown and say that nothing was created.
- Never put credentials or personal data in an issue.
- After creating, report each issue number, title and URL, and the suggested order. Branches and PRs link back with `Closes #<n>`.
- Keep issues in sync with reality: close or edit them when scope changes; do not leave stale tasks.
