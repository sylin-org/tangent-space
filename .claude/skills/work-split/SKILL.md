---
name: work-split
description: Run one package of docs/PLAN.md through its brief, the executor session, verification from the diff, a review only where one is owed, one feedback round, and Leo's merge word. Use it before a package starts and whenever an executor reports.
---

# Work split

The lead plans and writes the brief, an executor builds in a session of its own, the lead verifies from the diff, and Leo merges. A package splits only into pieces that need nothing from each other and touch no shared file. Roles, models, efforts and the order are [PLAN](../../../docs/PLAN.md)'s; the standing rules are [AGENTS](../../../AGENTS.md)'s. The route sweep and each group's live walk are the nets across packages.

## 1. The brief

- Take the next package in PLAN's order whose "after" is met. A package whose brief would mostly retell what the lead already knows stays with the lead.
- The brief is a paragraph naming the outcome, the seam that owns the question, what breaks on the way, the words it carries, its one-off script or that no stored shape changes, and the done check. The executor preamble (section 7) sits above it, filled in.
- The lead writes it to `.local/briefs/<id>.md`.

## 2. Starting the executor

- `mcp__ccd_session__start_session` with `context: "fresh"`, `use_worktree: true`, `branch_name` and `name` set to the package id, `title` set to the package name, `model` set to the role's model id (`claude-opus-5-5`, `claude-sonnet-5-5`, `claude-haiku-5-5`), `effort` set to the role's effort, `wake_when_done: true`, `source_branch` set to the lead's own branch whenever it is ahead of the repository's default branch (a worktree is cut from the default branch otherwise, and the executor would read the wrong tree), `prompt` set to the brief and `background` to the preamble's context. `initiation` is `user_asked` when Leo has told the lead to run the package, `offer` otherwise.
- A Koan package starts with `use_worktree: false` and `cwd` at `E:\repo\github\sylin-org\tangent-space`, so the pinned checkout, its Koan worktree and its Tangent worktree all lie inside the session's working directory. Its executor makes its own worktree as section 6 says, never a branch in the pinned checkout.
- When the app cannot create a worktree (its registry fails on this machine while `HOME` is unset), the session starts with `use_worktree: false`, and the brief opens with a workspace section carrying section 6's steps for a Tangent package.
- When the app refuses to start a session because the turn began with an automated event rather than Leo's words, the executor runs as an `Agent`: `subagent_type: "general-purpose"`, the role's `model` and `effort`, the same brief with the same workspace section. Its final message is the report, and feedback goes to its agent id with `SendMessage`.
- When the executor is run by hand, on another machine or another account, it reads its brief from `.local/briefs/<id>.md` with the model and effort stated, and the pasted report is read as the executor's report.
- The lead keeps working while the executor runs, never predicts its result and never reads its transcript.
- The lead watches the clock. When the executor starts, the lead schedules one check forty-five minutes later (`CronCreate`, one shot, with the executor's id and its output file in the prompt) and deletes it when the report arrives. A note that the executor is waiting on its own background work, no sign of progress for twenty minutes, or the check firing with no report and no progress is a hang, handled as section 3 says, never waited out. Progress is the executor's worktree: its commits and the times of the files it builds and edits. An `Agent` executor's task output file is written when it finishes, so its age says nothing while it runs.

## 3. Verifying

- A hang first, if there is one: `pwsh .claude/skills/work-split/orphans.ps1` lists the processes running from this repository whose parent is gone, with their age, worktree and command, and `-Stop` ends them; the tail of the executor's task output says where it stopped. An orphan holding the task open is ended, and the executor is told the cause as a finding to fix in the package.
- When the report arrives, the lead reads the diff on the branch, re-runs the one relevant check where the report claims a pass, and compares every claim with evidence. A claim without a tool result is a finding.
- For a screen, captures and scoring follow AGENTS's rule "Function first, polish at its stage".

## 4. Review and feedback

- The `reviewer` agent runs only for a package that touches access, security or a Koan seam, at the effort of PLAN's Reviewer row. It gets the diff and the brief, never a builder's reasoning, and runs once.
- Feedback goes by `SendMessage` to the executor's id. It holds the numbered findings, each with `file:line`, what changes and its severity, then what to keep, the checks to re-run, and "report again". The executor keeps its context; a new session would have to find everything again.
- One round. A second round runs only for a finding that is both severe and likely.
- A failure escalates one step at a time: the builder re-runs the failing check at higher effort once; still failing, the lead looks; still failing, Leo hears about it with the evidence.
- Fix at the edge, smallest first.
- Weigh a fix's whole cost and measure it; never trade the common path for rare values.
- No design-attack rounds.
- A finding is fixed only when a person or an agent would plausibly meet it, scored as severity times likelihood like any item under the delight filter. A remote possibility is one line of the report.
- A threat from someone who already holds the data is no threat.
- A package lands when its common paths are right.
- Reports to Leo stay short: outcomes, not mechanisms.

## 5. Landing, on Leo's word

- At most four sessions run at once, each in its own worktree on its own branch. Two packages run together only when their briefs' Areas share no file; the lead compares them before starting the second. Where a brief says it never runs beside another, the second starts only once the first has landed: on `dev` for a Tangent package, on Koan's `dev` for a Koan package. The Koan ledger files are the one exception (section 6).
- A landed package's line leaves PLAN's order, and the lead deletes it: for a Tangent package in a commit on `dev` right after the merge, and for a Koan package in the Tangent commit that moves the pinned revision to it. No package deletes its own line. A package absent from the order has landed, and an "after" naming it is met.
- A Tangent package merges into `dev` with `--no-ff`, with a message stating the outcome. `pwsh .claude/skills/work-split/merge-check.ps1` then runs on the merged tree, and the FEATURES rows are marked. `dev` is pushed on Leo's word. The worktree, the branch and the session are removed; inside the worktree its `.local\upstream\koan-framework` junction is removed with `cmd /c rmdir` before `git worktree remove`. `main` fast-forwards from `dev` on Leo's word.
- A Koan package is rebased onto Koan's `dev`. The pinned checkout is a detached worktree of `E:\repo\github\sylin-org\koan-framework`, where Koan's `dev` is checked out, so `dev` is fast-forwarded there: `git -C E:\repo\github\sylin-org\koan-framework merge --ff-only <branch>`. Koan's `dev` and `main` require a linear history (`koan:docs/engineering/nuget-publishing.md:31-32`), so `git -C E:\repo\github\sylin-org\koan-framework rev-list --merges origin/dev..dev` prints nothing before a push. The push, from that folder, waits for Leo's word. Tangent's pinned revision then moves on his word: `$revision` in `scripts/prepare-framework.ps1`, with the pinned checkout moved to it by `git -C E:\repo\github\sylin-org\tangent-space\.local\upstream\koan-framework checkout --detach <revision>`. Two packages' moves of the pinned revision land one at a time, and the second sets `$revision` again to the revision then landed.
- A Koan package with a Tangent half (k-refuse, k-build) lands in one act: Leo lands its Koan branch and merges its `claude/<id>` together, so Tangent's `dev` never stands on a Koan `dev` it does not compile against.
- A Koan package is cut only while Koan's `dev` equals the pinned revision. After a Koan package lands, the next one waits until the pinned revision has moved to that landing.

## 6. Workspaces

These steps are exact, because executors copy them.

**A Tangent package.** A session the app gave a worktree starts at step 2.

1. From the shared checkout, run `git worktree add -b claude/<id> .local/worktrees/<id> dev`.
2. Confirm the head holds what the package starts after.
3. Work only there, and never run git in the shared checkout again.
4. For a package that builds: `New-Item -ItemType Directory -Force .local\upstream`, then `cmd /c mklink /J .local\upstream\koan-framework E:\repo\github\sylin-org\tangent-space\.local\upstream\koan-framework`. `pwsh scripts/prepare-framework.ps1` must then report the pinned revision ready.
5. A running place: set `$env:Tangent__DataFolder` to a folder under the worktree's `.local\` (`Tangent:DataFolder`, `src/server/TangentModule.cs:36-37`), run `pwsh scripts/tangent.ps1 launch -Port <5221 or above>`, and end it with `stop`.

**A Koan package.**

1. Confirm that Koan's `dev` tip is the pinned revision: `git -C E:\repo\github\sylin-org\tangent-space\.local\upstream\koan-framework rev-parse dev` prints `$revision` from `scripts/prepare-framework.ps1`. If the two differ, stop and ask the lead: the pinned revision has not yet moved to the last landing (section 5).
2. Run `git -C E:\repo\github\sylin-org\tangent-space\.local\upstream\koan-framework worktree add -b k<n>-<a few words> E:\repo\github\sylin-org\tangent-space\.local\worktrees\<id> dev`. `K<n>` is the number the lead gives the package, in the order Koan packages start, as K41's branch was `k41-no-read-writes`; every commit message opens `K<n>: `. A decision record takes the next free number of its series at the tip it was cut from, and is renumbered at the rebase if another landing took it first.
3. Begin every Koan build and test command with `$env:TEMP = $env:TMP = 'E:\repo\github\sylin-org\tangent-space\.local\worktrees\<id>-temp';`, creating that folder once: Koan's test projects build into `%TEMP%\Koan-framework\tests\<project>\` (`koan:tests/Directory.Build.props:14-19`), which every Koan checkout on the machine otherwise shares.
4. Work there under Koan's law. Its checks are the focused tests that Koan's `CLAUDE.md` asks for (`koan:CLAUDE.md:123,129`): the module that holds the change and the modules that consume it, through a real `AddKoan()` host, not the full release ratchet.
5. The ledger files are resolved at landing: `koan:docs/decisions/toc.yml`, `koan:docs/decisions/index.md`, a row of its own in `koan:docs/reference/capability-map.md`, and the agent-skill snapshot under `koan:.agents/skills/koan/references/generated/`, which `pwsh scripts/update-agent-skills.ps1` rewrites whole. Two Koan packages may run together while they share only these. The branch that lands second rebases, adds its one entry or row to each ledger it touches, and runs `pwsh scripts/update-agent-skills.ps1`, then the same with `-Check`, which Koan's pull-request workflow runs (`koan:.github/workflows/pr-gate.yml:45`). Every other shared file keeps the briefs' exclusions.
6. Prove Tangent on the branch. Write each path in full; `T` below stands for `E:\repo\github\sylin-org\tangent-space\.local\worktrees\<id>-tangent`. Run `git -C E:\repo\github\sylin-org\tangent-space worktree add --detach T dev`, then `New-Item -ItemType Directory -Force T\.local\upstream`, then `cmd /c mklink /J T\.local\upstream\koan-framework E:\repo\github\sylin-org\tangent-space\.local\worktrees\<id>`.
7. Do not run `scripts/prepare-framework.ps1` there: it refuses any Koan head but the pinned revision.
8. Run merge-check's three steps there as three foreground calls, each with a ten-minute timeout, in its order, stopping at the first failure: `dotnet build T\Tangent.sln -nologo -v q`, which builds against the junction (`Directory.Build.props:3`); `dotnet test T\tests\Tangent.Tests -nologo --no-build`; and `pwsh -NoProfile -File T\scripts\check-rules.ps1`. (`merge-check.ps1` itself runs them under bounds that add up to thirty minutes, past the preamble's ten.)
9. Afterwards, `cmd /c rmdir T\.local\upstream\koan-framework`, then `git -C E:\repo\github\sylin-org\tangent-space worktree remove T`.

**A Koan package with a Tangent half**, which changes Tangent's files too (k-refuse, k-build). Its Koan steps are the ones above. In place of the detached proof, its Tangent worktree is a branch:

1. From the shared checkout, run `git worktree add -b claude/<id> .local/worktrees/<id>-tangent dev`, then make the junction of step 6 to the Koan worktree.
2. Commit the Koan half first. Then set `$revision` in `scripts/prepare-framework.ps1` to the Koan branch's head and commit it on `claude/<id>`; `pwsh scripts/prepare-framework.ps1` must then report that revision ready, since it refuses a Koan checkout at any other head or with uncommitted changes.
3. Run step 8's three checks there. If the Koan half changes after review, commit it, set `$revision` again, and run them again.
4. At landing (section 5) the lead sets `$revision` to the revision Koan's `dev` landed, and `claude/<id>` merges in the same act.

## 7. The executor preamble

It tops every brief, filled in.

> You are the executor for package **<id> · <name>** of Tangent Space, running on **<model>** at **<effort>**. The lead planned this package and will verify your result; Leo merges. Before touching anything, read `AGENTS.md`, `docs/BRIEF.md`, the `docs/LANGUAGE.md` words and the `docs/ARCHITECTURE.md` seam the brief names, and the code that seam lives in. For a Koan package, read in the Koan checkout its `CLAUDE.md`, its explore skill (`.codex/skills/explore/SKILL.md`), its naming decision DX-0043 (`docs/decisions/DX-0043-connector-naming-for-external-integrations.md`: `Koan.{FeaturePath}.Connector.{Vendor}`), `docs/reference/capability-map.md`, and the decisions the brief names.
>
> Plan inside this session, then build. When you have enough information to act, act. Do not re-derive what the brief settles.
>
> - `AGENTS.md`'s standing rules bind you, the delight filter, bounds, commits and Koan first among them. What follows adds to them.
> - Use or extend the seam that owns the question. A second copy of a decision, found in the tree or about to be written, is a stop-and-report, not a workaround. Build nothing speculative and nothing for a later package.
> - Before writing code for a capability, check Koan's capability map and the pillar's source. Where Koan falls short, stop and report the gap with the framework revision, the expected and observed behaviour, and a reproducer.
> - Run each check in the foreground with a timeout of twice its expected time, ten minutes at most. The lifecycle script is `scripts/tangent.ps1`: `launch` starts a server, `stop` ends it.
> - Commit with a message that states the outcome, on your own branch only; touch no other branch.
>
> Verify as you go: establish the check the brief names early and run it on a cadence. Before reporting, audit every claim against a tool result from this session; report only work you can point to, and say plainly what is not verified. If a check fails, say so with its output. When a check times out, the hang is the finding: end what the command started (the pid file under `.local/launch/`, or the process list), report the command, where its output stopped and the processes it left, and do not re-run it blind. If you are blocked on something only Leo or the lead can decide, say so in your report and stop rather than guessing. Your final message is the report, for a reader who did not watch you work: outcome first; each check with its command and result; what failed or is unverified; files changed; decisions the brief did not cover; each item held back by the delight filter with its score. Complete sentences, no working shorthand.

## 8. Record

- After each package, its FEATURES status, and one memory note for each lesson a later package needs.
- Project state lives in FEATURES status, ADRs and commits, and nowhere else.

## 9. Subagent roles

An explorer runs only when a builder needs a map it does not have, and returns conclusions, not file listings. An explorer, or a mechanic step inside the lead's own work, is an `Agent` call; its model and effort, and a mechanic package's, are PLAN's Explorer and Mechanic rows.
