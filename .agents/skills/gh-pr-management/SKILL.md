# Skill: gh-pr-management

## Purpose
Facilitate the use of the GitHub CLI (gh) for automating Pull Request (PR) workflows via terminal commands.

## What this skill does
- Create PRs directly from the terminal.
- Merge accepted PRs (with merge, rebase, or squash options).
- Delete the branch after merging, if desired.
- Check PR status and details quickly.
- Organize essential commands for PR handling.

## When to use
- Anytime you need to interact with PRs without using the GitHub web interface.
- To standardize and speed up the git/GitHub collaboration cycle via command line.

## Example gh commands

### Create a PR with default commit message:
```
gh pr create --fill --base main
```

### Create a PR with custom title and body:
```
gh pr create --title "Your PR Title" --body "Detailed description" --base main
```

### Merge a PR (squash):
```
gh pr merge --squash <pr-number-or-url>
```

### Delete remote branch after merge:
```
git push origin --delete <branch-name>
```

### List open PRs:
```
gh pr list
```

### View details of a PR:
```
gh pr view <pr-number-or-url>
```

---

## Notes
- The user must be authenticated (`gh auth login`).
- It is recommended to keep your repository in sync (`git pull/push`) before acting on PRs.
- This skill can be expanded to cover more advanced automations or CI/CD integrations using GitHub Actions.

## Ready-to-copy examples:
- Create PR: `gh pr create --fill --base main`
- Approve and squash merge: `gh pr merge --squash 1`
- Delete branch: `git push origin --delete branch-name`
- List PRs: `gh pr list`
