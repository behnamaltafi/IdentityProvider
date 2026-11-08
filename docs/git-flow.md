# Git Flow and Conventional Commits

Use `master` for stable releases and `develop` for integration. Both are long-lived.

| Branch | Start from | Merge into |
| --- | --- | --- |
| `feature/*` | `develop` | `develop` |
| `bugfix/*` | `develop` | `develop` |
| `release/*` | `develop` | `master`, then `develop` |
| `hotfix/*` | `master` | `master`, then `develop`, and reconcile with every active release branch |

Release and hotfix merges use `--no-ff`. Keep meaningful Conventional Commits in the ancestry so semantic-release can analyze them. Commitlint permits ordinary Git-generated merge/revert messages; CI validates all developer commits too. Do not squash a feature into a generic chore message. Never force-push, rewrite history, delete branches, or manually invent release tags.

```powershell
git switch develop
git switch -c feature/inventory
# Edit, test, then commit:
git add <paths>
git commit -m "feat(inventory): add stock lookup"
git switch develop
git merge --no-ff feature/inventory -m "chore(merge): integrate inventory feature"

# Stabilize on a release branch created from develop.
git switch -c release/next
# Make stabilization commits and run CI.
git switch master
git merge --no-ff release/next -m "chore(merge): integrate release candidate"
git switch develop
git merge --no-ff release/next -m "chore(merge): reconcile release fixes"

# Urgent production fix starts from master.
git switch master
git switch -c hotfix/stock-total
# Commit using: fix(inventory): correct stock total
git switch master
git merge --no-ff hotfix/stock-total -m "chore(merge): integrate stock hotfix"
git switch develop
git merge --no-ff hotfix/stock-total -m "chore(merge): reconcile stock hotfix"
# Also merge/reconcile hotfix/stock-total into any active release/* branch.
```

Use `type(scope)!: description`; scope and `!` are optional. Types: feat, fix, perf, refactor, chore, docs, style, test, ci, build, revert, migration. Examples:

- `feat(inventory): add stock lookup` -> minor
- `fix(api): handle missing product` -> patch
- `perf: reduce database round trips` -> patch
- `feat(api)!: change product response` -> major
- `chore: remove legacy format` with a blank line and `BREAKING CHANGE: remove old format` footer -> major
- Non-breaking docs, style, chore, test, ci, build, refactor and migration -> no release

Install Node.js ^22.14.0 or >=24.10.0, then `npm ci` and `pwsh -File scripts/Install-GitHooks.ps1` per clone. Hooks use the installed commitlint binary without downloading tools. CI independently runs `scripts/ci/Test-Commits.ps1`, checking all history by default (or every commit in `-From <base> -To <head>`). CI requires full history and fails rather than silently accepting a shallow checkout. On the initial unborn branch there are no commits to validate; the first commit will be checked by its hook.

The local lab origin is `D:/Work/DevOpsLab/git/IdentityProvider.git`. It is independent of this checkout. After the approved initial commit, create develop from master and push both with `git push -u origin master develop`. A local filesystem remote is accessible only to agents with access to that filesystem; Linux agents need an accessible clone source or mounted/shared equivalent configured later.
