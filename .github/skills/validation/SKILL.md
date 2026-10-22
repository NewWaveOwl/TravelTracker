---
name: validation
description: Validate a TravelTracker change before commit, push or PR by running the build and tests, checking the diff against scope, conventions and docs, and reporting honest evidence. Use for "validate", "check my change", "is this ready", or as the gate before commit and merge; use testing to write or investigate tests.
---

# TravelTracker validation gate

This checklist runs before a change is committed, pushed or called done. Test design and authoring live in `testing`; code conventions in `code`. Run commands from the repository root.

## 1. Build and test

```powershell
dotnet restore TravelTracker.slnx
dotnet build TravelTracker.slnx --configuration Release --no-restore
dotnet test TravelTracker.Tests.slnx --configuration Release
```

`TravelTracker.Tests.slnx` is what CI runs (`.github/workflows/ci.yml`), so it must pass. Read the output, not just the exit code:

- Tests were actually discovered and executed. Zero tests is not a pass.
- Report total, passed, failed and skipped counts as printed.
- A failure that also fails on a clean `origin/<PR-base>` is pre-existing; say so and show how you checked. Everything else is a regression you own.
- If the build fails, tests are blocked. Say that; do not claim they passed.
- Build output has **zero warnings and zero errors** for the changed projects. Report the warning count from the build summary. Pre-existing warnings are listed in issue #31 and must not be hidden by a green exit code.
- Compare discovered, executed and passed counts against the baseline. A count that dropped means coverage was removed; justify it or restore it. There is no maximum, but new behavior needs new tests.
- Code changes require zero build warnings and errors from your change and passing tests. Documentation-only changes need consistency checks instead: referenced paths and links exist, and counts and commands in docs match what you ran.

## 2. Review the diff

Resolve `<PR-base>` to the explicitly requested target branch, otherwise the repository default; verify it exists before comparing.

Run `git status` and `git diff origin/<PR-base>...HEAD` (plus uncommitted changes). Confirm:

- **Matches the issue/plan** and nothing more. Flag scope creep beyond the approved milestone issue unless requested. Check no secrets are staged (`git diff --cached`). For boundary or security-sensitive changes, run the read-only `code-review` agent on the diff and resolve every Critical and High finding before merge.
- **Conventions** from `.github/copilot-instructions.md` and `code`: layer responsibilities, one-way service and layer dependencies, HTTP-only frontend boundary, one primary type per file, `Program.cs` limited to composition, `/** @param @return */` docs on new or changed members, unrelated comments preserved.
- **Behavior has tests**, including failure paths, and the tests assert something meaningful (see `testing`). A bug fix has a regression test.
- **No secrets or personal data** in source, logs, tests or docs. No debug leftovers or commented-out code.
- **No generated files** (`bin/`, `obj/`, `TestResults/`, coverage output) or unrelated formatting churn.

## 3. Docs sync

If the contract, capabilities or limitations changed, `docs/api.md`, `docs/domain.md`, `docs/data.md` and the README tables must say the same as the code. Docs are the ground truth for planning, so a stale doc is a defect.

## 4. Behavior check (when the change is user-visible)

Run the affected service and any client that calls it (for the API: `dotnet run --project src/TravelTracker.Api --launch-profile http`, Swagger at `http://localhost:5104/swagger`) and exercise the changed endpoint, including one failure case. Passing tests is not the same as a working endpoint; say which you verified.

## 5. CI and delivery, when requested

Inspect the actual pipeline and agreed quality gates. Restore, build and execute tests; publish TRX/coverage evidence even on test failure where supported. Application artifacts require passing checks. A configured percentage or an echo step does not enforce a coverage gate.

For requested Azure DevOps delivery, build once and promote the same immutable tested artifact through environments. Keep environment settings and secrets outside artifacts, use least-privilege service connections, honor configured approvals/checks, and verify health after deployment. Define observable failure handling and a rollback approach, including migration compatibility. Example YAML echo steps do not prove deployment, and a health response alone does not prove persistence or all dependencies healthy. Do not deploy as a side effect of editing guidance.

## 6. Report

State plainly: commands run, build result, test counts, baseline versus new failures, what behavior was verified and how, and what was not verified. Do not hedge what was verified and do not claim what was not. Only after this gate passes move on to `commit`.
