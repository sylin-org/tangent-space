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

The end is Leo's, in his words: "Your own place", with Discord-like capabilities to manage articles. Three lines mark the way to it, and he checks each one himself.

1. **His own place is live.** It runs from a published build at an address he chooses. He signs in with his own Bluesky account, publishes an article, and a reply from Bluesky shows under it.
2. **Someone else runs one.** A download, a README a stranger follows, a build that carries an older data folder forward, the polish stage and the phone layouts.
3. **Beyond Bluesky.** A seam a second protocol fits, then Webmention and Mastodon.

Until the first line is crossed, nothing that works is rebuilt and no Koan change starts unless its brief names what a person or an agent newly meets, or which state, kept by hand today, it wraps where it belongs.

Each package delivers one thing a person or an agent meets, and builds the seam it needs inside it. Koan work happens only as a feature needs it, under Koan's law, and lands by rebase and fast-forward with the pinned revision following. No two packages in flight touch the same files; the lead schedules by the files. Every package lands with the tree working and carries its own words (the ARCHITECTURE paragraph, the LANGUAGE words, the FEATURES rows). No place holds data to carry before the first line, so a stored shape changes with no script; past it, a stored shape moves with a one-off script at `scripts/once/<what-it-moves>`, run once after its merge and then deleted. The crawl at `.local/walk/crawl.ts` follows every package: every page a visitor, a member and the proprietor reach, the same addresses as JSON, and `/mcp`. Leo's own walk of the running place follows every two or three, and its findings become the next work.

The packages, as `id · what you get · what it absorbs · after`:

To the first line:

1. rest-one-branch · Topics, Posts, Tangents, the applications and a profile each answer at one declared entity endpoint of Koan's, for a page's form, a JSON client and a tool alike; a stale edit keeps its sender's words, a Tangent's list neither repeats nor drops a Topic between pages, and `change_tangent` takes joining · the branches `claude/rest-posts`, `claude/rest-tangents` and `claude/rest-lists`, brought together and nothing added · none
2. the-operation-in-the-hook · koan · A declared controller's hooks are told which operation runs and whether its write was made or replayed, and one step of the controller answers a refused form with its page, so no controller keeps that state in fields of its own · — · decision 16
3. rest-finished · The place's settings answer at one declared endpoint at `/settings`, `change_settings` takes admission, appearance and first steps, every verb answers through Koan's `Answers`, and nothing the move replaced stays · the settings as a singleton and the removal of Tangent's own copies of Koan's answers · rest-one-branch, the-operation-in-the-hook
4. your-place-runs · The place runs from a published build at a public address, set up by Leo alone: the README says how, with a proxy, TLS on the default port, the Production environment and the operator named, and a public place limits how fast anyone signs in or writes · — · rest-finished

The move onto Koan's entity endpoints is finished when every create, change and delete of a row Tangent owns (a Topic, a Post, a Tangent, the place) is a declared operation, every verb that sends its own command stays a verb and answers through Koan's `Answers`, and no type stays without a caller. Roles stay with `RolesController`, since their rows are Koan's.

The check of the first line is Leo's walk with his own account, on loopback first and then at the address: what the real network shows wrong becomes a package of its own.

To the second line, in no order yet:

- export-and-archive-are-one-copy · The place as you or as a visitor, and each Tangent, as one static copy, which a retired place's agents read too · the design's lifecycle packages; Koan's snapshot
- upgrades · A newer build carries an older data folder forward, and says so where it cannot
- handing-over · The operator hands the place to someone else, and may then leave
- a-download-and-a-readme · One file to download and a README a stranger follows to a running place
- The polish stage, as [FEATURES](FEATURES.md) lists it.

To the third line:

- a-second-protocol-fits · The core hands a protocol package URIs, versions and receipts, and atproto is one such package
- webmention-and-mastodon · A Post goes out by Webmention and to Mastodon under its author's own account, and replies come back as references · the brief's protocols

A package of the 4 November design that names nothing a person or an agent meets folds into the first feature that needs it, or waits; none runs for its own sake.

## Open for Leo

Each decision is cited by its number, and each package builds the default given here until he answers.

- **1 · Positions and volume.** May a position reveal how much happened where its reader cannot see? Until he answers it may not, and events-4 keeps the scan that hides it.
- **2 · `read(address)`.** It is offered beside the thirteen named read tools. The measure, five reading tasks by a fresh agent each way, took 8 calls with `read` against 6 with the named reads and lost two tasks, so the named reads stay until he says otherwise.
- **4 · ARCH-0143.** Amending it a day after its acceptance is a Koan decision for him, not for a package. ARCH-0145, its amendment of §1, §2 and §4, is in Koan as a proposal; the §5 and §3 amendments come with the HTML projection and the answer packages, each a proposal until he answers.
- **5 · Companion Lobby's adapter.** Every form's JSON carries `token` for it (`Form.Token`), which nothing here reads. Does it stay? Until he answers it stays, and Tangent shapes nothing else for the Lobby.
- **7 · Standard MCP clients.** A client with no atproto identity stays a visitor. That is product scope, named for him.
- **8 · The profile at sign-in.** Does sign-in wait a short bound for the public profile, or does the fill run just after? Until he answers, k-atproto-1 reads the profile in the sign-in step, best-effort within a short bound, and sign-in never fails on it.
- **10 · The deferred layout.** Settings in three pages, and verbs as panels, wait for the polish stage unless he calls them now.
- **11 · Holds as the only model.** This is his second ordered Koan step. Commands judging inside their hold rely on it; until he answers, commands keep their holds as they are.
- **13 · His lines in BRIEF.** `docs/BRIEF.md:3` names ADR 0014 as the record of which earlier records bind. `:55` says background work runs in background workers. Until he answers, BRIEF stays as it is, and each package that meets the conflict reports it.
- **16 · The states kept by hand.** Each declared controller keeps fields of its own so its hooks know which operation runs, where a form is sent on and what a refused form reads again; five controllers hold eleven such fields, in three different ways. Does Koan's hook take that state (the-operation-in-the-hook), or do the plain states of the domain, a Topic closed, pinned or followed and a Tangent quieted, fold into their entity's declared write, or both? Until he answers, the-operation-in-the-hook waits, and rest-finished starts only once he has.