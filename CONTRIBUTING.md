# Git Workflow

## Milestone 3 branch strategy

For #27–#50, the integration branch is **`milestone_3`**, as selected by the repository owner. The milestone development branch is **`feature/milestone-3-layered-service-development`**. This section overrides references to `master` in the general workflow below for Milestone 3 work; the repository's default branch remains unchanged.

| Change | Start / update from | PR target | Keep after merge? |
| --- | --- | --- | --- |
| Milestone bootstrap | `origin/milestone_3` | Establish the named milestone branch at that revision | Yes |
| Individual work item, e.g. `m3-02-board-workflow` | `origin/feature/milestone-3-layered-service-development` | `feature/milestone-3-layered-service-development` | Work-item branch can be removed only when requested |
| Final milestone submission | The named milestone branch | `milestone_3` | **Keep the named milestone branch** |

```powershell
git fetch origin
git switch -c m3-02-board-workflow origin/feature/milestone-3-layered-service-development
# Edit, validate and commit with an issue reference, e.g. Refs #28.
git push -u origin m3-02-board-workflow
```

Never push changes directly to `milestone_3` or the named milestone branch. Initial creation of the named branch at the integration commit is the bootstrap exception; all later updates go through reviewed PRs. Do not merge an integration branch into a work-item branch. Update an unpushed work-item branch by rebasing onto its actual PR base. Agents must never force push, including `--force-with-lease`; after a pushed branch needs a rebase, a human handles any resulting force push. Never rebase/force push a shared milestone branch.

If `milestone_3` advances during milestone development, bring those updates into the shared milestone branch through a separate reviewed synchronization PR. Do not bypass its protections or rewrite its shared history. Check for new contract changes before beginning each work item.

Both milestone branches require a PR, one approval from another team member, approval of the latest push, resolved conversations and the passing `test` check. They reject force pushes and deletion, with no bypass actors. Automatic branch deletion must remain disabled. Read back repository settings before claiming enforcement; a checklist alone does not enforce these rules.

Use the PR template and [milestone workflow](docs/milestone-3/workflow.md). Record the three human gates: developer specification/plan approval before implementation; developer diff/test/secret verification afterward; another member's GitHub review before merge. An agent report never substitutes for a human signoff. Failed checks, unapproved plans, open dependencies or unresolved Critical/High findings prevent Done.

The final milestone PR title is **Milestone 3: Layered Service Development with AI Agents**, with head `feature/milestone-3-layered-service-development` and base `milestone_3`. Create it once reviewed work exists on the feature branch; link the board/export, issues, work-item PRs, plans, validation, agent findings, actual peer approvals and #50's report. Submit the GitHub milestone PR URL. The source handout names Azure DevOps; the user's GitHub-only choice does not establish instructor acceptance.

## General workflow outside Milestone 3

## Rule

**You never push to `master`. Every change goes on a branch, gets rebased onto `origin/master`, and goes up as a PR.**

## One-time setup

```bash
git config --global pull.rebase true
git config --global rerere.enabled true
```

## The pipeline

**1. Start a branch from fresh master**

```bash
git fetch origin
git switch -c my-feature origin/master
```

One branch per task, named after the task, like `fix-login-timeout` or `add-csv-export`, and not after yourself (`alice`, `bob-dev`). A branch lives only as long as its PR does, so it's disposable by design.

**2. Work and commit**

```bash
git add <files>
git commit -m "Short description of the change"
```

**3. Rebase onto master**

```bash
git fetch origin
git rebase origin/master
```

> **What rebase does:** while you worked, other people's PRs landed on master. Rebase takes your commits off, moves your branch's starting point up to the current master, and replays your commits on top, one by one.
>
> ```
> Before:                          After git rebase origin/master:
>
>       A---B   my-feature                         A'--B'   my-feature
>      /                                          /
> ---o---X---Y   origin/master         ---o---X---Y   origin/master
> ```
>
> Your changes are the same, but `A'` and `B'` are new commits with new IDs, as if you had started your work from the latest master. That's why:
>
> - the PR merges cleanly, since your branch already contains everything on master;
> - history stays a straight line instead of a web of merges;
> - any conflicts show up on **your** machine, one commit at a time, where you can fix them;
> - you need `--force-with-lease` when pushing again, because the remote still has the old `A` and `B`.

**4. Push**

```bash
git push -u origin my-feature      # first time
git push --force-with-lease        # after any rebase
```

**5. Open a PR.** If master has moved and the PR shows conflicts or is out of date, go back to step 3.

**6. After your PR is merged, throw the branch away**

```bash
git switch master
git pull
git branch -D my-feature                 # delete local branch
git push origin --delete my-feature      # delete remote branch (skip if the PR already did)
```

Use `-D`, not `-d`. The merged commits on master have different IDs from yours, so git can't tell your branch was merged and `-d` will refuse. Your work is safe: it's on master now.

Then start your next task at step 1 with a new branch.

## Resolving conflicts during step 3

```bash
# edit the conflicting files, then:
git add <files>
git rebase --continue

# to give up and return to where you were:
git rebase --abort
```

## Never

- `git merge master` or `git merge origin/master`
- `git push --force` (always use `--force-with-lease`)
- Push to `master`
- Reuse a branch after it's merged
- Keep a personal long-lived branch (`alice`, `bob-dev`) that you carry from task to task

## Recovery

```bash
git reflog                         # find where things were before
git reset --hard <sha>             # go back there
```
