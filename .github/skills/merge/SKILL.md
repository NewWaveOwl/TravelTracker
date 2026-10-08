---
name: merge
description: Bring a TravelTracker task branch up to date with master using rebase, push it, open a pull request, and complete or clean up after the merge, following CONTRIBUTING.md. Use when asked to rebase, push, open a PR, resolve conflicts, or merge.
---

# TravelTracker integration and merge

`CONTRIBUTING.md` is the source of truth: never push to `master`, one branch per task, rebase instead of merging master in, a PR for every change. This skill adds the agent safety rules.

## Hard rules for agents

- Never push to `master`. Never `git merge master` / `git merge origin/master` into a task branch.
- **Never force push, including `--force-with-lease`** (`AGENTS.md`). `CONTRIBUTING.md` permits `--force-with-lease` for humans after a rebase; an agent that has rebased an already-pushed branch must stop and ask the human to push it.
- Do not push, open a PR, merge a PR, or delete a branch unless the task asks for that exact step. Approval for one step does not cover the next.
- Do not enable auto-merge unless asked.

## Update the branch

1. The working tree must be clean (commit via `commit` first).
2. `git fetch origin`, then `git rebase origin/master`.
3. Conflicts: resolve file by file, keeping both sides' intent; `git add <files>`, `git rebase --continue`. Re-run the build and tests afterwards because a clean textual merge can still break behavior. If a conflict is not clearly resolvable, `git rebase --abort` and report instead of guessing.
4. Re-run `validation` after the rebase.

## Push and open the PR

1. First push: `git push -u origin <branch>`. A branch that was already pushed and then rebased needs a human push (see above).
2. Open the PR against `master` using `gh pr create` or the GitHub MCP tools (`.vscode/mcp.json`). If neither is available, give the human the branch name and a ready-to-paste title and description.
3. Title: same style as a commit subject. Description: what changed and why, `Closes #<issue>`, the validation commands run with test counts, anything unverified, and any docs updated. Add the PR attribution line required by session instructions.
4. CI (`.github/workflows/ci.yml`) runs `dotnet test TravelTracker.Tests.slnx --configuration Release` on pull requests. Report its result; do not call the PR ready while CI is failing or pending.

## Merge

Merging is a human decision. Merge only when explicitly told to, the PR is approved if review is required, CI is green, and the branch is up to date with `master`. History on `master` currently uses merge commits (`Merge pull request #N`); use the repository's default PR merge behavior unless told otherwise.

## After the PR is merged

Only when asked, per `CONTRIBUTING.md`:

```bash
git switch master
git pull
git branch -D <branch>
git push origin --delete <branch>   # skip if GitHub already deleted it
```

`-D` is correct: the commit IDs on master differ from the branch's, so `-d` refuses. Then start the next task from a new branch. Never reuse a merged branch.

## Recovery

`git reflog` shows where the branch was; `git rebase --abort` abandons an in-progress rebase. Do not `git reset --hard` over work that is neither committed nor pushed without asking.
