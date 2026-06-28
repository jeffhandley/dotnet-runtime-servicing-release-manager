---
description: >
  Build and verify a minimum reproduction for a single dotnet/runtime servicing release PR
  (a PR targeting a release/* branch). Dispatched by the servicing-release-manager (or run manually)
  with a PR number. Installs the baseline GA SDK, authors a minimal repro, runs it to confirm the
  bug, uploads the repro + output.log as an artifact, writes a step summary, and posts a repro
  comment to the PR (unless run in dry-run mode).

on:
  workflow_dispatch:
    inputs:
      pr_number:
        description: "Servicing release PR number to build a repro for"
        required: true
        type: string
      suppress_output:
        description: "Dry-run: produce the artifact + step summary but do NOT post a PR comment"
        required: false
        type: boolean
        default: false
  permissions: {}

if: ${{ github.event.repository.fork != true }}

permissions:
  contents: read
  pull-requests: read
  issues: read

concurrency:
  group: "servicing-repro-producer-${{ github.event.inputs.pr_number }}"
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
    toolsets: [pull_requests, repos, issues]
    min-integrity: approved
  edit:
  bash: ["dotnet", "git", "curl", "jq", "tee", "sed", "awk", "grep", "head", "tail", "cat", "ls", "find", "mkdir", "rm", "cp", "mv", "chmod", "echo", "date", "env", "test", "bash", "sh", "mktemp", "wc", "cut", "tr", "sort", "uniq", "xargs", "basename", "dirname", "gh"]

checkout: false

network:
  allowed:
    - defaults
    - github
    - dotnet
    - "aka.ms"
    - linux-distros

safe-outputs:
  add-comment:
    target: "*"
    max: 1
    hide-older-comments: true
  upload-artifact:
    max-uploads: 1
    retention-days: 30
    allowed-paths:
      - "/tmp/gh-aw/agent/**"
    defaults:
      if-no-files: "ignore"
  noop:

timeout-minutes: 45
---

# Servicing Repro Producer

Build and verify a minimum reproduction for **PR #${{ github.event.inputs.pr_number }}** in
`${{ github.repository }}` using the **`servicing-release` skill** at
`.github/skills/servicing-release/SKILL.md`. Read that skill and follow **Procedure A -- Produce a
minimum repro**. The PR's repository is `${{ github.repository }}`; its number is
`${{ github.event.inputs.pr_number }}`.

## Workspace

Author the repro under a fresh directory **outside any checkout** so it can be uploaded:

```bash
export WORKDIR=/tmp/gh-aw/agent/servicing-repro
rm -rf "$WORKDIR"; mkdir -p "$WORKDIR"; cd "$WORKDIR"
```

Determine the target `MAJOR.MINOR` from the PR's base branch (`release/MAJOR.MINOR` or
`release/MAJOR.MINOR-staging`).

## Steps

1. **Classify** PR #${{ github.event.inputs.pr_number }} with the skill's PR classification rule.
   If it is ruled out (code-flow, infrastructure, branding, test-only, missing
   `Servicing-approved`/`Servicing-consider`, etc.), do **not** build a repro: call `noop` with a
   one-line reason and stop.
2. **Dedup.** Read the PR's comments (integrity-gated). If a prior repro comment from this workflow
   already exists (it contains the marker `<!-- servicing-repro -->`), call `noop` ("repro already
   posted") and stop.
3. **Produce + verify** the repro per Procedure A on the **baseline GA SDK** for the target major.
   Capture combined output to `$WORKDIR/output.log`. Confirm the bug reproduces; if it does not,
   report that (step summary) and `noop` ("could not reproduce") -- do not post a comment.
4. **Upload the artifact.** Call `upload_artifact` with name
   `servicing-repro-pr-${{ github.event.inputs.pr_number }}` and path `/tmp/gh-aw/agent/servicing-repro`
   (the repro sources + `output.log`).
5. **Step summary.** Write to `GITHUB_STEP_SUMMARY`: the repro form used, the isolating code snippet,
   the Expected result, and the Actual result (quoted from `output.log`).
6. **Comment (unless dry-run).** If the bug reproduced and
   `${{ github.event.inputs.suppress_output }}` is not `true`, post **one** comment on PR
   #${{ github.event.inputs.pr_number }} via `add-comment` (set `pull_request_number` to that PR).
   Begin the body with the hidden marker on its own line:

   ```
   <!-- servicing-repro -->
   ```

   Then include, in this order: (1) a 1-2 sentence description of the issue; (2) which minimum repro
   approach was used (unit test / file-based app / csproj); (3) the code snippet that isolates the
   repro call site; (4) **Expected Result**; (5) **Actual Result** (quoted from the captured
   `output.log`); (6) a link to the uploaded workflow artifact for the repro and its output.

   If `suppress_output` is `true`, skip the comment entirely (the artifact + step summary are the
   only outputs).

Do not modify the repository. All repro work happens under `$WORKDIR`.
