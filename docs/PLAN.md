# Development plan

PLAN holds the roles, the order of the packages and the decisions they wait on. A decision that a package of the order builds a default for is a line of Open for Leo; an idea not built, waiting on his yes, is a row of [PROPOSALS](PROPOSALS.md). [FEATURES](FEATURES.md) lists the open work, a package's Closes naming the rows it removes in its commit, and the [`work-split` skill](../.claude/skills/work-split/SKILL.md) runs one package. Models are those released on 28 October 2026; the role table is re-read when the lineup changes.

## Roles

| Role | Does | Model | Effort | Runs as |
| --- | --- | --- | --- | --- |
| **Lead** | Product and architecture decisions, ADRs, a paragraph brief for every package, reading results, Koan design, the conversation with Leo | Claude Opus 5.5 | high; xhigh only while deciding a spine question or Koan semantics | the main session |
| **Builder, first of its kind** | A package that establishes a pattern the tree has not got yet: the first package of each kind, such as docs-arch, access-1, gate-1, writes-1, events-1, render-1 and client-1 | Claude Opus 5.5 | high; xhigh where the lead names it in the brief | a session of its own, in a worktree |
| **Builder, repeat** | A package that repeats an established pattern, such as gate-2 to gate-4, render-2 and render-3 | Claude Opus 5.5 | high, set explicitly (its own default is medium) | a session of its own, in a worktree |
| **Builder, Koan** | A package in the Koan checkout under Koan's contributor law; the order names this role "koan" | Claude Opus 5.5 | high; xhigh where the brief names Koan semantics | a session of its own, in a worktree of the Koan checkout; how it lands is the skill's to say |
| **Explorer** | Finds and reads: inventories, Koan source lookups, "where is X handled", research with the web | Claude Sonnet 5.5 | medium | the project's `Explore` agent (`.claude/agents/explore.md`), which pins Sonnet at medium; the built-in Explore would run on the session's own model |
| **Mechanic** | Bulk, checkable edits from an exact instruction: a rename sweep, language-check fixes, fixture data, test scaffolds from a spec, a deletion with its tests and documents | Claude Opus 5.5 for a package; Claude Haiku 5.5 for a step inside the lead's work | high for a package; low for a step, medium when the instruction has judgment in it | a session of its own for a package; a subagent for a step |
| **Scorer** | The visual loop's independent judgment on captures | Claude Opus 5.5 | high | the `scorer` agent (`.claude/agents/scorer.md`), given only captures, reference, spec and rubric |
| **Second scorer** | The tie-break within 0.3 of the gate | Claude Sonnet 5.5 | high | the same agent run with `model: "sonnet"` — a different family |
| **Reviewer** | Correctness review of a package, where the [`work-split` skill](../.claude/skills/work-split/SKILL.md) calls for one; the skill says when and for how many rounds | Claude Opus 5.5 | high; xhigh for a Koan change | the `reviewer` agent (`.claude/agents/reviewer.md`), given the diff and the brief, never the builder's reasoning |

- Claude Fable 5.1 is held back: from 29 October 2026 no role runs on it unless Leo names it for a package, because its quota is spent faster than the work it buys over Opus at `high` or `xhigh`.
- A role whose packages land in their first round at `high` is tried at `medium`; a role that escalates moves up a model, not up an effort.
- Fan out for bulk: an audit across many files, a review across many packages, research across many sources. A fan-out costs roughly fifteen times a conversation; it is for breadth, never for a chain.
- Fast mode is for interactive, mechanical stretches on Opus 5.5 only; it changes speed, not the model.
- The `Workflow` tool runs only on Leo's word.
- A settled repeat package may run on the `opusplan` alias: Opus plans and Sonnet executes inside one session.
- Five or more independent repeat packages of one shape are a `/batch` run: a worktree and a branch each, verified one by one.
- Project settings pin no model and no effort: the lead passes both per session and per subagent. An `effortLevel` setting would override Leo's own sessions and is his to set.

## The order

- **Small Koan changes first.** Access lands on k-refuse, k-gate and k-edge, not behind the HTML projection.
- **Nets before repeats.** The sweep lands after the first access package on routes and before the repeat across every route, and one render package lands before the render repeat.
- **No shared files.** No two packages in flight touch the same files. Tangent worktrees that share the pinned checkout run beside each other only once k-build lands. A Koan package in its own Koan worktree, with its own `TEMP`, shares no `obj` or `bin` with the pinned checkout, so it runs beside the Tangent packages that do not depend on it now. Group 0's Tangent packages run one at a time, in this line: docs-arch, cut-3, cut-1, cut-2, look-1 and look-2. k-refuse's Tangent half runs after look-2, or in a gap between two packages that do not touch its files.
- **Koan packages start in this line.** k-invoke and k-head start now, side by side; k-build starts once docs-0 has landed, and never runs beside docs-arch; k-refuse runs alone once k-invoke and k-head have landed; then k-page beside k-atproto-1. k-atproto-1 never runs beside K43, since both own Koan.Identity. k-data, k-jobs and k-test wait behind this line, after k-atproto-1, one at a time under the same rule of no shared files. Access waits on k-refuse, so nothing else starts ahead of it.
- **A walk closes each group.** A live walk ends each group, and its findings become packages. A walk drives the real place in a browser, and at `/mcp` as Lumen, an agent signed in with its own account. Where Companion Lobby's adapter has not caught up with what Tangent answers, the walk reaches `/mcp` through a plain MCP client, and the Lobby's breakage is written up for the Lobby, a separate product never built here.
- **The tree works.** Every package lands with the tree working.
- **Each package carries its own words:** the ARCHITECTURE seam paragraph, the LANGUAGE words, the AGENTS rule, the FEATURES rows and the ADRs its change touches.
- **Stored shapes move by script.** A package that changes a stored shape carries a one-off script for the real-run folder at `scripts/once/<what-it-moves>`. It runs right after its own package's merge, before the next merged build opens the real-run folder, and the commit after that run deletes only that package's script.

The packages, as `id · name · role · after`:

**Group 0, clearing the ground**

- docs-arch · ARCHITECTURE states the seams as they stand · first of its kind · docs-0
- k-build · Each consumer builds the pin apart · koan · docs-0 (its Tangent half merges with its landing)
- k-invoke · One action invoked in process, Leo's ordered spike · koan · none
- cut-3 · Bring your agent is a link · mechanic · docs-arch
- cut-1 · Delete what nothing in the place needs · mechanic · cut-3
- cut-2 · Delete what nothing in a conversation needs · mechanic · cut-1
- look-1 · Five layers stated once, management on the endpoint · first of its kind · cut-2
- look-2 · Slots fold into Appearance · repeat · look-1, cut-2
- The walk: the place without retirement, bring your agent, and the appearance preview

**Group 1, access, judged once**

- k-refuse · A reason may name its word and its field, and a refusal at the route carries a reason · koan · k-invoke, k-head (its Tangent half after look-2)
- k-gate · Every routed action is judged once, before binding · koan · k-refuse
- k-edge · Every answer at the web edge is the Problem · koan · k-gate
- k-mcp-1 · Koan's MCP edge fails in one shape · koan · k-refuse, k-edge, k-invoke
- access-1 · `For` words each refusal for what it refuses, and implies what it implies · first of its kind · cut-2
- access-2 · The member role is Koan's, and standing caps by the anonymous bag · first of its kind · access-1
- gate-1 · One judgement before binding, on three routes · first of its kind · k-gate, access-2
- sweep-1 · One sweep over the route table · first of its kind · gate-1, k-mcp-1
- gate-2 · Every route under `/t` declares its bit · repeat · sweep-1
- gate-3 · The door and the reader's own routes declare their bit · repeat · gate-2
- gate-4 · Every settings route declares its bit, and the start check is on · repeat · gate-3
- The walk: every route and every refusal, on both doors

**Group 2, how a write is made**

- k-head · The log's head and a command's clock · koan · none
- writes-1 · A Post write is a judgement and a verb · first of its kind · gate-4, k-refuse
- writes-2 · Take on a tangent and report are Post verbs · repeat · writes-1
- writes-3 · Every other command is a judgement and a verb · repeat · writes-2, k-head
- The walk: a reused id, take on a tangent and report, on both doors

**Group 3, what is new, and who is here**

- k-page · Keyset pages · koan · k-refuse
- events-1 · Since you were here: one rule in two units · first of its kind · writes-3
- events-2 · One `Seen`, and what is for you read on the door · repeat · events-1
- events-3 · The door is a composition · repeat · events-2, k-page
- events-4 · The live stream on Koan's server-sent events · repeat · events-3, k-head
- k-atproto-1 · The connector knows the person · koan · k-refuse
- k-atproto-2 · atproto's identifiers and the proof door are the connector's · koan · k-atproto-1, k-edge
- people-1 · Standing in one command · first of its kind · events-3, writes-3, k-page
- people-2 · People from Koan's person · repeat · people-1, k-atproto-2
- The walk: what is new since you were here, and an idle place

**Group 4, how an address answers**

- k-html · Koan projects an application's components · koan · k-gate, k-invoke
- k-answer · A write answers in kind · koan · k-html, k-refuse
- k-forms · A form comes from its route · koan · k-answer
- render-1 · Topic and new topic on the projection · first of its kind · k-forms, gate-4, look-2, events-3
- render-2 · Every read page sheds its frame · repeat · render-1, people-2, events-4
- render-3 · Every form comes from its route · repeat · render-2
- The walk: every page and every refused form

**Group 5, the agent door and the lifecycle**

- k-mcp-2 · Reading by address, and instructions from a provider · koan · k-mcp-1, k-invoke
- tools-1 · One text, and reading is an address · mechanic · k-mcp-2, render-3, people-1
- k-feeds · Feeds are Koan's · koan · k-html
- feeds-1 · Tangent's feeds on Koan's · mechanic · k-feeds, render-2
- k-snapshot · A static copy of an application's GET routes · koan · k-answer, k-invoke
- life-1 · Export and the archive are one static copy · repeat · k-snapshot, render-3, feeds-1
- k-data · Backup and the data root are Koan's · koan · k-atproto-1
- life-2 · Backup is a copy, restore is putting it back · repeat · k-data, life-1, k-build, k-atproto-2
- k-jobs · A quiet Jobs · koan · k-atproto-1
- The walk: the agent door's instructions, with `read` measured

**Group 6, tests, client, Markdown**

- k-test · Koan's web test kit · koan · k-atproto-1
- tests-1 · Fixtures on Koan's kit · mechanic · k-test, render-3
- tests-2 · Tests only of Tangent's decisions · mechanic · tests-1
- client-1 · Islands are functions · first of its kind · tests-2, events-4
- client-2 · The live page on `EventSource` · repeat · client-1, render-3
- md-1 · One Markdown dialect · mechanic · client-2, look-2
- The walk: the live page

## Open for Leo

Each decision is cited by its number, and each package builds the default given here until he answers.

- **1 · Positions and volume.** May a position reveal how much happened where its reader cannot see? Until he answers it may not, and events-4 keeps the scan that hides it.
- **2 · `read(address)`.** It may replace the thirteen named read tools. tools-1 measures it with Lumen before they go, and he may refuse it outright.
- **3 · The direct-invocation spike.** k-invoke decides the base of the refused form's page, of `read` and of the static copy, and who owns the web pipeline. It is his next ordered step; until it lands, the packages that start after k-invoke wait.
- **4 · ARCH-0143.** Amending it a day after its acceptance is a Koan decision for him, not for a package. Until he answers, k-refuse carries ARCH-0145's amendment of §1, §2 and §4, k-html the §5 amendment and k-answer the §3 one as proposals, each landing only on his word.
- **5 · Companion Lobby's move.** Should one release keep aliases for the tools that change, and `Form.Token` past render-3? Until he answers, no: Tangent shapes nothing for the Lobby.
- **7 · Standard MCP clients.** A client with no atproto identity stays a visitor. That is product scope, named for him.
- **8 · The profile at sign-in.** Does sign-in wait a short bound for the public profile, or does the fill run just after? Until he answers, k-atproto-1 reads the profile in the sign-in step, best-effort within a short bound, and sign-in never fails on it.
- **10 · The deferred layout.** Settings in three pages, and verbs as panels, wait for the polish stage unless he calls them now.
- **11 · Holds as the only model.** This is his second ordered Koan step. Commands judging inside their hold rely on it; until he answers, commands keep their holds as they are.
- **13 · His lines in BRIEF.** `docs/BRIEF.md:3` names ADR 0014 as the record of which earlier records bind. `:55` says background work runs in background workers. Until he answers, BRIEF stays as it is, and each package that meets the conflict reports it.
- **15 · The token of a list of Tangents.** k-page's keyset token is encoded, not sealed, by default, and an encoded token's order can tell a reader how many Tangents they cannot view come before the last one they see. He answers before k-page starts.
