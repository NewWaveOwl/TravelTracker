# TravelTracker agent guidance

Read [.github/copilot-instructions.md](.github/copilot-instructions.md) for repository conventions and validation commands.

The course's canonical skills live under `.github/skills/`. Agents reading this file should load the relevant skill directly:

- Code, architecture, refactoring, or comments: [.github/skills/code/SKILL.md](.github/skills/code/SKILL.md).
- Test authoring, test investigation, or behavior verification: [.github/skills/testing/SKILL.md](.github/skills/testing/SKILL.md).
- Validating a change before commit/push/PR (build, tests, diff and docs checks): [.github/skills/validation/SKILL.md](.github/skills/validation/SKILL.md).
- Committing: [.github/skills/commit/SKILL.md](.github/skills/commit/SKILL.md).
- Rebasing, pushing, opening or merging a PR, branch cleanup: [.github/skills/merge/SKILL.md](.github/skills/merge/SKILL.md).
- Planning features and deciding what to build (ground truth is `docs/`): [.github/skills/planning/SKILL.md](.github/skills/planning/SKILL.md).
- Breaking a plan into small INVEST-style GitHub issues: [.github/skills/issues/SKILL.md](.github/skills/issues/SKILL.md).

Typical flow: planning -> issues -> (code + testing) -> validation -> commit -> merge.

- Never force push. This includes `--force-with-lease`: if a rebase of an already-pushed branch needs it, ask the human to push.

This routes agents to the same reviewed instructions without maintaining duplicate skill bodies. Run repository tasks from the TravelTracker root.
