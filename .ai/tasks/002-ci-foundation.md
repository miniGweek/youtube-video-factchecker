# Task 002: CI Foundation — verify.sh + GitHub Actions Workflow

**Status:** active
**Type:** chore
**Branch:** `chore/ci-foundation`
**Date:** 2026-03-30

---

## Objective

Create the verification script and CI pipeline so that every PR against `main` must pass build and test checks before merging. Infrastructure work only — no application code changes.

---

## Context

- The project uses `dotnet build` and `dotnet test` against `youtube-fact-checker.sln`.
- `Directory.Build.props` enforces `TreatWarningsAsErrors` — the build check is already strict.
- Branch protection will be configured manually in GitHub after the CI workflow has run at least once. That is NOT part of this task.
- Branched from `task/011-llm-client-abstraction` (includes multi-provider work, 220 tests).

---

## Deliverables

### 1. `verify.sh` (repo root)

```bash
#!/usr/bin/env bash
set -euo pipefail

echo "=== Restore ==="
dotnet restore youtube-fact-checker.sln

echo "=== Build ==="
dotnet build youtube-fact-checker.sln --no-restore

echo "=== Tests ==="
dotnet test youtube-fact-checker.sln --no-build --verbosity normal

echo "=== All checks passed ==="
```

- `set -euo pipefail` — exit on first failure, undefined variables, pipe failures.
- Must be executable: `chmod +x verify.sh`.

### 2. `.github/workflows/ci.yml`

```yaml
name: CI

on:
  pull_request:
    branches: [main]
  push:
    branches-ignore: [main]

jobs:
  verify:
    name: Build & Test
    runs-on: ubuntu-latest

    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Run verify.sh
        run: ./verify.sh
```

- .NET SDK version `9.0.x` matches all project TargetFramework `net9.0`.

### 3. `.ai/CLAUDE.md` updates

- Add `verify.sh` reference + unmodifiable constraint to "Build & Run" section.
- Add CI protection rule to "What NOT to Do" section.

---

## Acceptance Criteria

- [ ] `verify.sh` exists at repo root, is executable, passes locally.
- [ ] `.github/workflows/ci.yml` is valid YAML.
- [ ] .NET SDK version matches project target framework.
- [ ] `.ai/CLAUDE.md` updated with verify.sh reference and constraint.
- [ ] Diff limited to: `verify.sh`, `.github/workflows/ci.yml`, `.ai/CLAUDE.md`, `.ai/tasks/`, `.ai/memory/progress.json`.
- [ ] Commits use Claude Code author identity.

---

## Commit Plan

1. `chore: add task contract for CI foundation (task 002)` — this file
2. `chore(ci): add verify.sh and GitHub Actions CI workflow` — `verify.sh`, `.github/workflows/ci.yml`
3. `docs(claude): add verify.sh reference and CI protection rules` — `.ai/CLAUDE.md`
4. `chore: mark task 002 complete in progress.json` — `.ai/memory/progress.json`

---

## Post-Task (manual, NOT for Claude Code)

After merging this PR, configure branch protection on `main` in GitHub UI:
1. Require a pull request before merging.
2. Require status checks to pass → select "Build & Test".
3. Do not allow bypassing the above settings.
