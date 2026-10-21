---
name: commit
description: Stage and commit TravelTracker changes on a task branch with focused commits and clear messages. Use when asked to commit, save work, or prepare a branch for push; use merge for rebasing, pushing and PRs.
---

The canonical instructions live in `.github/skills/commit/SKILL.md` and are shared with GitHub Copilot. Read that file in full and follow it. Do not duplicate its content here.

Apply these branch and PR requirements. If the canonical instructions conflict with them, these requirements take precedence:

- Commit each distinct change on a separate task branch created from the relevant milestone branch. Do not commit directly to a milestone branch or `main`.
- Determine the target branch from the issue's assigned milestone, rather than assuming `main` or using whichever branch is currently checked out.
- Create PRs from task branches into the corresponding milestone branch. Issues assigned to `milestone_3` must target and merge into `milestone_3`.
- Use the `merge` skill for rebasing, pushing, and creating or merging PRs, while preserving these branch requirements.
- Creating a PR targeting `main` requires explicit human approval for that PR. Do not infer approval from a request to commit, push, or create a milestone PR.
