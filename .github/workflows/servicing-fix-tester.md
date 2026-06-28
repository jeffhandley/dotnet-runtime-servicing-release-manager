---
description: >
  Verify that a merged dotnet/runtime servicing fix actually resolves its issue. Dispatched by the
  servicing-release-manager (or run manually) with a PR number, once the fix has flowed into a daily
  SDK build. Reuses the repro built by servicing-repro-producer, runs it on a baseline SDK (still
  buggy) and a fixed SDK (contains the fix), compares to the expected result, uploads both
  version-named logs as an artifact, writes a step summary, and posts a verdict comment to the PR
  (unless run in dry-run mode).

on:
  workflow_dispatch:
    inputs:
      pr_number:
        description: "Merged servicing release PR number to verify the fix for"
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
  actions: read

concurrency:
  group: "servicing-fix-tester-${{ github.event.inputs.pr_number }}"
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
  edit:
  bash: ["dotnet", "git", "curl", "jq", "tee", "sed", "awk", "grep", "head", "tail", "cat", "ls", "find", "mkdir", "rm", "cp", "mv", "chmod", "echo", "date", "env", "test", "bash", "sh", "mktemp", "wc", "cut", "tr", "sort", "uniq", "xargs", "basename", "dirname", "unzip", "gh"]

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

timeout-minutes: 60
---

# Servicing Fix Tester

Verify that the fix in merged **PR #${{ github.event.inputs.pr_number }}** in
`${{ github.repository }}` resolves its issue, using the **`servicing-release` skill** at
`.github/skills/servicing-release/SKILL.md`. Read that skill and follow **Procedure B -- Verify a
fix**. The PR's number is `${{ github.event.inputs.pr_number }}`.

## Workspace

```bash
export WORKDIR=/tmp/gh-aw/agent/servicing-fix
rm -rf "$WORKDIR"; mkdir -p "$WORKDIR"; cd "$WORKDIR"
```

Determine the target `MAJOR.MINOR` from the PR's base branch and the fix commit (the PR's **merge
commit** SHA) via the GitHub API.

## Steps

1. **Preconditions.** Confirm PR #${{ github.event.inputs.pr_number }} is **merged** and is a
   rule-in product fix (skill classification). Read the PR's comments (integrity-gated): there must
   be a prior repro comment from `servicing-repro-producer` (identify it by the gh-aw footer
   containing `workflow_id: servicing-repro-producer`). If there is no such repro comment, or this
   workflow already posted a verdict comment (gh-aw footer `workflow_id: servicing-fix-tester`), call
   `noop` with the reason and stop.
2. **Confirm the fix has flowed** into a daily SDK build for the target band (skill's *fix-flow
   detection*). If it has **not** flowed yet (common for servicing -- e.g. before Patch Tuesday),
   call `noop` ("fix not yet in a daily build; will retry") and stop. Otherwise resolve
   `BASELINE_SDK` (latest GA, lacks the fix) and `FIXED_SDK` (daily build containing the fix commit).
3. **Obtain the repro.** Reuse the producer's repro **unchanged**: download it from the
   `servicing-repro-pr-${{ github.event.inputs.pr_number }}` artifact of the most recent successful
   `servicing-repro-producer` run for this PR (use the GitHub Actions API; unzip into `$WORKDIR`). If
   it cannot be retrieved, re-derive the identical repro via the skill (Procedure A steps 1-3).
4. **Run on both SDKs** without changing the repro: on `BASELINE_SDK` capturing
   `$WORKDIR/output-baseline-<BASELINE_SDK>.log`, then on `FIXED_SDK` capturing
   `$WORKDIR/output-fixed-<FIXED_SDK>.log`.
5. **Render the verdict** by comparing both runs to the Expected result: **Verified fixed** (buggy on
   baseline, correct on fixed), **Not fixed** (still buggy on the fixed SDK), or **Inconclusive**
   (e.g. baseline did not exhibit the bug).
6. **Upload the artifact.** Call `upload_artifact` with name
   `servicing-fix-test-pr-${{ github.event.inputs.pr_number }}` and path `/tmp/gh-aw/agent/servicing-fix`
   (the repro + both version-named logs).
7. **Step summary.** Write to `GITHUB_STEP_SUMMARY`: a description of the repro and where it came
   from, the Expected result, the Actual result **before** the fix (with `BASELINE_SDK` version), and
   the Actual result **after** the fix (with `FIXED_SDK` version).
8. **Comment (unless dry-run).** If `${{ github.event.inputs.suppress_output }}` is not `true`, post
   **one** comment on PR #${{ github.event.inputs.pr_number }} via `add-comment` (set
   `pull_request_number`). The comment body must include, in this order: (1) a reference to the repro
   used, linking the prior `servicing-repro-producer` repro comment when possible; (2) the **Expected
   Result**; (3) the **Actual result before the fix**, showing the `BASELINE_SDK` version used;
   (4) the **Actual result with the new SDK bits**, showing the `FIXED_SDK` version used; (5) the
   **verdict** on whether the fix is verified. (gh-aw automatically appends a footer identifying this
   workflow, used for dedup -- you do not need to add your own marker.) If `suppress_output` is
   `true`, skip the comment entirely.

Do not modify the repository. All work happens under `$WORKDIR`.
