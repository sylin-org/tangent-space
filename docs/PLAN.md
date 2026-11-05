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

Each package delivers one thing a person or an agent meets, and builds the seam it needs inside it. Koan work happens only as a feature needs it, under Koan's law, and lands by rebase and fast-forward with the pinned revision following. No two packages in flight touch the same files; the lead schedules by the files. Every package lands with the tree working, carries its own words (the ARCHITECTURE paragraph, the LANGUAGE words, the FEATURES rows), and moves a stored shape with a one-off script at `scripts/once/<what-it-moves>`, run once after its merge and then deleted. A live walk of the real place, in a browser and at `/mcp` as Lumen, follows every two or three packages, and its findings become the next work.

The packages, as `id · what you get · what it absorbs from the design · after`:

1. every-door-says-what-to-do · A refusal names the next step, search never refuses, and nobody is told to fix input before being told to sign in; the gate runs once, before binding, on the routes people hit · access-1, gate-1, gate-2, k-refuse's Tangent half; Koan k-refuse and k-gate as it needs them · none
2. the-agent-reads-any-address · `read(address)`, one text behind `llms.txt`, `tangent.json` and the instructions, and the Problem on every failure at `/mcp`, measured with Lumen · k-mcp-1, k-mcp-2, tools-1 · K44 landed
3. the-places-look · The five layers stated once, one colour choice, and a Markdown description, masthead and footer; management on the endpoint · look-1, look-2 · none
4. bring-your-agent-is-a-link · The page renders for a visitor and links the Lobby; the probe goes · cut-3 · none
5. export-and-archive-are-one-copy · The place as you or as a visitor, and each Tangent, as one static copy; retirement, the restore verb and the archive's second host go · life-1, life-2, cut-1; Koan k-snapshot · the-agent-reads-any-address
6. take-on-a-tangent-and-report · Starting a Topic from a Post offers its title, and a report needs no reason; a reused id answers `id-taken` · writes-1, writes-2 · every-door-says-what-to-do
7. publication · The brief's unbuilt half: publication under your own account, Connections, Webmention and Mastodon · the design's §8 · every-door-says-what-to-do, the-agent-reads-any-address

The design's other packages (forms and pages from routes, standing in one command, people from Koan's person, the sweep, the islands, the Markdown dialect, the test kit, feeds, backup, keyset pages, the connector's profile and identifiers, each consumer's build) fold into the first feature that needs them, or wait; none runs for its own sake. K44 (one request answered in process) and K46 (the log's head and a command's clock) are built and land now.

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
