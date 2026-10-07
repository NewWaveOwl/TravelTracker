# TravelTracker agent guidance

Read [.github/copilot-instructions.md](.github/copilot-instructions.md) for repository conventions and validation commands.

The course's canonical skills live under `.github/skills/`. Agents reading this file should load the relevant skill directly:

- Code, architecture, refactoring, or comments: [.github/skills/traveltracker-code/SKILL.md](.github/skills/traveltracker-code/SKILL.md).
- Test authoring, test investigation, or behavior verification: [.github/skills/traveltracker-testing/SKILL.md](.github/skills/traveltracker-testing/SKILL.md).

- Never force push.

This routes agents to the same reviewed instructions without maintaining duplicate skill bodies. Run repository tasks from the TravelTracker root.
