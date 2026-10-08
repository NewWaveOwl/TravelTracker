@AGENTS.md

# Claude Code specifics

Everything shared with other agents is in `AGENTS.md` above. This file only adds what applies to Claude Code.

- **Skills:** `.claude/skills/<name>/SKILL.md` are thin pointers to `.github/skills/<name>/SKILL.md`, so the skills show up as Claude skills. Edit the skill under `.github/skills/`. When you change a skill's `description:`, copy it to its pointer too. Add a pointer when you add a skill.
- **Outward-facing steps:** commit, push, opening or merging a PR, and creating GitHub issues happen only when the user asks for that exact step, and each approval covers only that step.
- **Attribution:** end commit messages and PR descriptions with the attribution lines the session instructions give.
