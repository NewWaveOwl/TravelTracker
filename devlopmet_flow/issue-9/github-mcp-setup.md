# GitHub MCP setup for TravelTracker

The repository configures the official remote GitHub MCP server in [`.vscode/mcp.json`](https://github.com/NewWaveOwl/TravelTracker/blob/68fe7ea6bd11b7d02a0af3e2fe913d55857880fe/.vscode/mcp.json). Each teammate signs in using their own GitHub account with access to this private repository. No credential belongs in the shared configuration.

## Start in VS Code

1. Install or update VS Code and GitHub Copilot. Sign in to the account with Copilot access and access to `NewWaveOwl/TravelTracker`.
2. Open the TravelTracker repository root, not its parent course directory. Review `.vscode/mcp.json` and trust this workspace when prompted.
3. Open the command palette and run **MCP: List Servers**. Select `github`, then **Start**. The same action is available above the server definition when the configuration is open.
4. Complete the GitHub authorization prompt using your own account. If repository access needs organization approval, obtain that access first.
5. Open Copilot Chat in Agent mode and enable the GitHub server's issue and repository tools in **Configure Tools**.
6. Ask: `Use GitHub MCP to read NewWaveOwl/TravelTracker issue 9. Give its title, assignee and deliverables. Do not modify it.` Expect **Spec & Planning Lead**, assigned to **NewWaveOwl**, with specification, triage notes and implementation plan deliverables.
7. Ask: `Search all issues in NewWaveOwl/TravelTracker for attraction name search duplicates and list open pull requests. Do not change anything.` Review the returned links before proposing a new issue.

This server runs remotely; teammates do not need Docker, a local GitHub MCP executable or a shared personal access token. [GitHub's setup guide](https://docs.github.com/en/copilot/how-tos/copilot-in-your-ide/customize-copilot/extend-copilot-with-tools-and-context/set-up-the-github-mcp-server?tool=vscode) documents remote setup and authentication. [VS Code's MCP guide](https://code.visualstudio.com/docs/agent-customization/mcp-servers) documents the workspace `servers` format and tool controls.

## Other clients

GitHub Copilot CLI includes a GitHub MCP server. Authenticate with `copilot login`; use `/mcp list` and `/mcp show github-mcp-server` to inspect it. The CLI does not read VS Code's `servers` configuration. Avoid adding a second GitHub server just to reproduce the IDE configuration. See [Copilot CLI MCP configuration](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers).

In Visual Studio with MCP support, open Copilot Chat in Agent mode, add an HTTP server named `github` with URL `https://api.githubcopilot.com/mcp/`, and authenticate with your own account. Use the [Visual Studio setup instructions](https://docs.github.com/en/copilot/how-tos/copilot-in-your-ide/customize-copilot/extend-copilot-with-tools-and-context/set-up-the-github-mcp-server?tool=visualstudio) for the installed version.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No `github` server | Open the repository root and check the configuration is present on your branch. Update the editor if it lacks MCP support. |
| Server blocked | Review workspace trust and any school or organization policy for MCP. |
| Authentication error | Reauthenticate your own GitHub account; never paste a token into chat, source or screenshots. |
| Issue appears missing | This repository is private. Confirm the signed-in account can open issue #9 directly. A logged-out 404 does not establish that it is missing. |
| Tools absent in chat | Start the server and enable its tools. Inspect **Show Output** for connection errors. |
| Built-in CLI server works but VS Code does not | Their configurations and authorization sessions differ. Authenticate in VS Code separately. |

## Verification on 2026-10-06

The official remote endpoint completed MCP initialization, tool discovery and authenticated calls as `NewWaveOwl`. `issue_read` returned #9, duplicate searches for `attraction` and `search` returned zero feature issues, `list_pull_requests` returned no open PRs, and `list_branches` returned `master` and `ArtiomS_Branch`.

These were live MCP protocol calls using the repository's existing GitHub sign-in, with the credential held only in process memory. They verify server and repository access. The VS Code trust/authorization UI still needs to be completed separately on each teammate's computer; the installed extensions alone do not prove that IDE session is connected.
