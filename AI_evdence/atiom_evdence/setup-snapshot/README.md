# Main configuration snapshot

These are exact copies from the main commit identified in [the manifest](../archive-manifest.md). Relative paths inside them describe the original application repository, not this resource-only branch.

| Snapshot | Purpose |
| --- | --- |
| [AGENTS.md](AGENTS.md) | Routes agents to the canonical skills |
| [Copilot instructions](.github/copilot-instructions.md) | Repository conventions and checks |
| [Coding skill](.github/skills/traveltracker-code/SKILL.md) | Existing layers, resource files and comment style |
| [Testing skill](.github/skills/traveltracker-testing/SKILL.md) | Test boundary, isolated hosts and the 30-case cap |
| [VS Code MCP configuration](.vscode/mcp.json) | Official GitHub MCP endpoint |
| [Root MCP configuration](mcp.json) | Main's root MCP configuration after PR #17 |

The cleanup's recommended scope retains this reusable setup on main and moves evidence/history. It does not treat a configuration snapshot as an authentication export.
