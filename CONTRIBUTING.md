# Git Workflow

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
