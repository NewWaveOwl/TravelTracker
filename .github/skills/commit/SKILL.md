---
name: commit
description: Stage and commit TravelTracker changes on a task branch with focused commits and clear messages. Use when asked to commit, save work, or prepare a branch for push; use merge for rebasing, pushing and PRs.
---

# TravelTracker commits

Follow `CONTRIBUTING.md`. A commit happens only when the task asks for it.

## Before committing

1. Run `git status` and `git branch --show-current`. Never commit on `master`; if on it, create a task branch from fresh `origin/master` first (`git fetch origin`, `git switch -c <task-name> origin/master`). An explicitly requested branch name wins. Name branches after the task (`add-route-point-lookup`), never after a person.
2. Run `git diff` (and `git diff --staged`). Read what will be committed. Every hunk must belong to the task.
3. Run the checks from `validation` for the change. Do not commit code that does not build; say so if a check is skipped.

## Stage deliberately

- Stage named files or hunks (`git add <path>`, `git add -p`). Do not use `git add -A` or `git add .` blindly.
- Never stage: secrets, `.env`, `*.user`, `bin/`, `obj/`, `TestResults/`, coverage output, or unrelated formatting churn. If something like that shows as untracked, check `.gitignore` instead of committing it.
- Keep unrelated changes out. Split into separate commits when one diff contains separable concerns (for example a refactor and a feature, or code and an unrelated docs fix).

## Write the message

- Subject: imperative, at most about 72 characters, no trailing period, describing what the change does (`Add GET route point by id`, not `fixes` or `WIP`).
- Add a body when the why is not obvious: what problem, what decision, what was deliberately left out. Wrap at about 72 columns.
- Reference the issue the work closes with `Closes #<n>` in the body or PR description. Never invent an issue number.
- Include any attribution trailer required by the tool or session instructions.
- Pass multi-line messages with a heredoc or multiple `-m` flags, not an editor.

## Rules

- Make a new commit rather than `--amend` once a commit has been pushed or reviewed. Amend only an unpushed commit you just made.
- Never `--no-verify`, never skip signing or hooks. If a hook fails, fix the cause and make a new commit.
- Never force push (see `AGENTS.md`).
- Do not push or open a PR unless asked; hand off to `merge`.
- After committing, run `git status` and `git log --oneline -n 3` and report the hash, subject, and files included.
