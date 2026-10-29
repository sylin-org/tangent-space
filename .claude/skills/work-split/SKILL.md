---
name: work-split
description: Run one package of docs/PLAN.md through the planner and executor loop — choose the role, write the brief, start the executor session with its model and effort, verify its report from the diff, review it in a fresh context, feed back or accept. Use it before a package starts and whenever an executor reports.
---

# Work split

The lead plans; an executor builds in a session of its own; the lead verifies and feeds back; Leo merges. One package, one executor session, one model. Roles, models and efforts are in [docs/PLAN.md](../../../docs/PLAN.md); this file is the procedure.

## 1. Choose

Take the next package from `FEATURES.md` whose dependencies in `PLAN.md` are met. Assign its role by the plan's rule: first of its kind → Fable 5.1 `high` (`xhigh` for the writer, the evaluator, Koan transaction semantics); a repeat → Opus 5.5 `high`; explorer-grade → Sonnet 5.5 `medium`; mechanical → Haiku 5.5 `low`. A package whose brief would mostly retell what the lead already knows stays with the lead.

## 2. Brief

Write it from [brief.md](brief.md) — the FEATURES rows, the outcome, the area, what is out of bounds, how done is checked, the report shape — with the preamble from [executor.md](executor.md) at the top, filled in. A brief under ten lines is not finished; a brief that enumerates steps is too prescriptive. State the goal, the constraints and the checks, and let the executor plan.

## 3. Start the executor

`mcp__ccd_session__start_session` with `context: "fresh"`, `use_worktree: true`, `branch_name` and `name` set to the package id (`0-7-rule-checks`), `title` set to the package name, `model` set to the role's model id (`claude-fable-5-1`, `claude-opus-5-5`, `claude-sonnet-5-5`, `claude-haiku-5-5`), `effort` set to the role's effort, `wake_when_done: true`, `source_branch` set to the lead's own branch whenever it is ahead of the repository's default branch (a worktree is cut from the default branch otherwise, and the executor would read the wrong tree), `prompt` set to the brief and `background` to the preamble's five lines of context. `initiation` follows the conversation: `user_asked` when Leo has told the lead to run the package, `offer` otherwise. A Koan package starts with `cwd` at the Koan checkout and `use_worktree: false`, on a branch the executor creates there.

When the app cannot create a worktree (its registry fails on this machine while `HOME` is unset), start the session with `use_worktree: false` and open the brief with a workspace section: the executor runs `git worktree add -b claude/<package> .local/worktrees/<package> dev` from the shared checkout, confirms the head commit, works only in that folder, and — for a package that builds — makes the Koan checkout visible there with a directory junction to `.local/upstream/koan-framework`. It commits on that branch and never runs git in the shared checkout again.

When the executor is run by hand — another machine, another account — the same brief goes to `.claude/briefs/<package>.md` with the model and effort stated, and the pasted report is read as the executor's report.

The lead keeps working while the executor runs, never predicts its result and never reads its transcript.

## 4. Verify

When the report arrives: read the diff on the branch, re-run the one relevant check where the report claims a pass, and compare every claim with evidence. A claim without a tool result is a finding.

For a screen, run the `visual-loop` skill now: the `scorer` agent and, within 0.3 of the gate, the same agent with `model: "sonnet"` as the second scorer.

Then the review: the `reviewer` agent with the diff and the brief, and `model: "fable"` for the writer, the evaluator, Connections and any Koan change. The reviewer sees no reasoning from the executor or the lead.

## 5. Feed back or accept

Findings → `SendMessage` to the executor's `local_…` id with [feedback.md](feedback.md) filled in: numbered findings with `file:line`, what changes, what stays, which checks to re-run, report again. The executor keeps its context; a new session would have to find everything again. Two rounds without convergence → the lead looks at the code itself; a third → Leo hears, with the evidence.

Accepted → merge the package branch into `dev` with `--no-ff` and a message that states the outcome, run the rule checks on the merged tree, mark the FEATURES rows `done`, push `dev`, then prune: `git worktree remove` the package worktree, `git branch -d` the branch (and `git push origin --delete` it if it was ever pushed), archive the executor session. `main` fast-forwards from `dev` when a slice closes. A Koan package merges into Koan's `dev` the same way, `dev` is pushed, and Tangent re-pins to the new revision in `scripts/prepare-framework.ps1` with the pinned checkout moved to it. Leo's standing word (28 October 2026): push as they land, no more branches than packages in flight, close and remove after merge.

## 6. Record

After the package: FEATURES status; a memory note for any lesson a later package needs; the slice report's counters (role, review findings, visual rounds, escalations, Koan corrections).

## Subagent roles

The explorer and the mechanic are `Agent` calls, not sessions. Explorer: `subagent_type: "Explore"` — the project's `Explore` agent runs on Sonnet 5.5 at `medium`. Mechanic: `subagent_type: "general-purpose"`, `model: "haiku"`, `effort: "low"`, an exact instruction and a checkable result.
