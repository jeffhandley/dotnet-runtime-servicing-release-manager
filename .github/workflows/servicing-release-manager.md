---
description: >
  Hourly orchestrator for servicing-release repro production and fix verification. Finds
  dotnet/runtime release/* PRs opened or merged since its last successful run, classifies which ones
  are real product fixes that need a repro (rule-in/rule-out from the servicing-release skill), and
  fans out parallel work by dispatching servicing-repro-producer (for PRs that still need a repro)
  and servicing-fix-tester (for merged fixes that have a repro and have flowed into a daily SDK
  build). Tracks a watermark and a de-dup/queue in cache-memory. Adds no labels and reacts to no PR
  events -- it is entirely schedule driven.

on:
  schedule: hourly
  workflow_dispatch:
  roles: [admin, maintainer, write]
  permissions: {}

if: ${{ github.event.repository.fork != true }}

permissions:
  contents: read
  pull-requests: read
  issues: read
  actions: read

concurrency:
  group: "servicing-release-manager"
  cancel-in-progress: false

# ###############################################################
# Select a PAT from the pool and override COPILOT_GITHUB_TOKEN.
# Run agentic jobs in an isolated `copilot-pat-pool` environment.
#
# When org-level billing is available, this will be removed.
# See `shared/pat_pool.README.md` for more information.
# ###############################################################
imports:
  - uses: shared/pat_pool.md
    with:
      environment: copilot-pat-pool

environment: copilot-pat-pool

engine:
  id: copilot
  model: claude-opus-4.8
  env:
    COPILOT_GITHUB_TOKEN: |
      ${{ case(
        needs.pat_pool.outputs.pat_number == '0', secrets.COPILOT_PAT_0,
        needs.pat_pool.outputs.pat_number == '1', secrets.COPILOT_PAT_1,
        needs.pat_pool.outputs.pat_number == '2', secrets.COPILOT_PAT_2,
        needs.pat_pool.outputs.pat_number == '3', secrets.COPILOT_PAT_3,
        needs.pat_pool.outputs.pat_number == '4', secrets.COPILOT_PAT_4,
        needs.pat_pool.outputs.pat_number == '5', secrets.COPILOT_PAT_5,
        needs.pat_pool.outputs.pat_number == '6', secrets.COPILOT_PAT_6,
        needs.pat_pool.outputs.pat_number == '7', secrets.COPILOT_PAT_7,
        needs.pat_pool.outputs.pat_number == '8', secrets.COPILOT_PAT_8,
        needs.pat_pool.outputs.pat_number == '9', secrets.COPILOT_PAT_9,
        'NO COPILOT PAT AVAILABLE')
      }}

tools:
  github:
    toolsets: [pull_requests, repos, issues, actions]
    min-integrity: approved
  cache-memory:
    key: servicing-release-manager-state
    retention-days: 30
  bash: ["gh", "jq", "curl", "tee", "sed", "awk", "grep", "head", "tail", "cat", "ls", "find", "mkdir", "echo", "date", "env", "test", "bash", "sh", "wc", "cut", "tr", "sort", "uniq", "xargs", "basename", "dirname"]

checkout: false

network:
  allowed:
    - defaults
    - github
    - dotnet
    - "aka.ms"

safe-outputs:
  dispatch-workflow:
    workflows: [servicing-repro-producer, servicing-fix-tester]
    max: 3
  noop:

timeout-minutes: 30
---

# Servicing Release Manager

You orchestrate servicing-release repro production and fix verification for
`${{ github.repository }}`. Each run you find new/updated `release/*` PRs, decide what work they
need using the **`servicing-release` skill** at `.github/skills/servicing-release/SKILL.md` (read its
**PR classification rule** and **fix-flow detection** sections), and dispatch the worker workflows.
You add no labels and post no comments.

## State (cache-memory)

Load `/tmp/gh-aw/cache-memory/state.json` if present; otherwise start fresh:

```json
{ "last_run": "<YYYY-MM-DD-HH-MM-SS>", "dispatched": [] }
```

- `last_run` is the watermark (UTC). If the file is missing, derive it from the most recent
  successful run of this workflow via `gh api repos/${{ github.repository }}/actions/workflows/servicing-release-manager.lock.yml/runs?status=success`
  (`.workflow_runs[0].updated_at`); if none, default to 48 hours ago.
- `dispatched` is a de-dup list of `"<pr_number>:<repro|fixtest>"` entries already dispatched
  (keep the most recent ~200).

Write timestamps as `YYYY-MM-DD-HH-MM-SS` (no colons, no `T`, no `Z`) -- required for the cache
artifact.

## Find candidate PRs

Query `${{ github.repository }}` for `release/*` PRs (include `release/*-staging`) **updated since
`last_run`**, both open and recently merged. Read PR metadata and bodies through the integrity-gated
`github` tool (skip `[Filtered]` items, record the count).

For each candidate, apply the skill's **PR classification rule**. Skip (do not enqueue) anything
ruled out (code-flow, infrastructure, branding, test-only, missing `Servicing-approved` /
`Servicing-consider`, bulk "Merging internal commits", etc.).

## Build the two work queues

- **Repro queue** -- a rule-in PR with **no** prior `servicing-repro-producer` comment (identify
  such comments by the gh-aw footer containing `workflow_id: servicing-repro-producer`) and no
  `"<pr>:repro"` entry in `dispatched`.
- **Fix-test queue** -- a rule-in PR that is **merged**, **has** a `servicing-repro-producer`
  comment, has **no** `servicing-fix-tester` comment (gh-aw footer
  `workflow_id: servicing-fix-tester`), no `"<pr>:fixtest"` entry in
  `dispatched`, **and** whose fix commit (the PR merge commit) has **flowed into the latest daily SDK
  build** for the target band (apply the skill's *fix-flow detection*: resolve the band's daily
  `runtime_commit` and use the GitHub compare API -- include only when status is `behind`/`identical`).
  PRs not yet flowed are left for a later run (do not enqueue, do not mark dispatched).

## Dispatch (at most 3 per run)

Dispatch up to **3** workers total this run via `dispatch-workflow`, preferring the fix-test queue
first (fixes are time-sensitive once flowed), then the repro queue. For each:

- repro: dispatch `servicing-repro-producer` with input `pr_number` = the PR number;
- fix-test: dispatch `servicing-fix-tester` with input `pr_number` = the PR number.

Add each dispatched item to `dispatched` (`"<pr>:repro"` / `"<pr>:fixtest"`). Anything left over
stays unqueued and is reconsidered next run (the watermark + de-dup prevent re-processing).

## Finish

Update `/tmp/gh-aw/cache-memory/state.json`: set `last_run` to now and append the dispatched
entries. If nothing was dispatched, call `noop` with a one-line summary (e.g. "N candidates, all
already handled or not yet flowed").
