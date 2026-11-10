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

Before the first line the place is realigned, on Leo's word of 10 November 2026. Nothing it is today, a tool's name, a route, a test or a rule, is a reason to keep a second copy of anything, and every state it keeps takes the one shape [ARCHITECTURE](ARCHITECTURE.md) states in five points under "A state", with a participant's standing as the state built in it: one field on its row, one table of transitions on its entity, Koan's one rule that refuses any other save, one command, and one route with its `set_` tool, a row's `can` and the buttons drawn from the table.

A value is a field of its entity's ordinary change, `change_{entity}`, when whoever may edit the row may set it to any value at any time; anything else is a state. A tool is `read_{entity}`, `change_{entity}`, `set_{state}`, or a verb with its noun where it creates a row, carries words or removes one. No Koan change starts unless its brief names which state it wraps where it belongs, or what a person or an agent newly meets.

Each state copies what standing left, and its brief carries what the one before it taught. Standing taught three things: the table is declared to Koan with the words its tool takes, so a refusal reads in them; what is given with a state, as a timeout's length is, costs one check in the route and one in the buttons; and the row in force can differ from the stored one, so the table serves both.

Each package delivers one thing a person or an agent meets, and builds the seam it needs inside it. Koan work happens only as a feature needs it, under Koan's law, and lands by rebase and fast-forward with the pinned revision following. No two packages in flight touch the same files; the lead schedules by the files. Every package lands with the tree working and carries its own words (the ARCHITECTURE paragraph, the LANGUAGE words, the FEATURES rows). No place holds data to carry before the first line, so a stored shape changes with no script; past it, a stored shape moves with a one-off script at `scripts/once/<what-it-moves>`, run once after its merge and then deleted. The crawl at `.local/walk/crawl.ts` follows every package: every page a visitor, a member and the proprietor reach, the same addresses as JSON, and `/mcp`. Leo's own walk of the running place follows every two or three, and its findings become the next work.

The packages, as `id · what you get · what it absorbs · after`:

To the first line:

1. a-topic-and-a-post-stand-one-way · A Post is removed and brought back, and a Topic closed and pinned, each through one `set_` tool; a Tangent says in one place which Topic it pins, a Topic whose root is removed holds no pin, and its page says which keeps replies out, its moderators' closing or a removed root · `remove` and `undo_removal`, `close_topic` and `reopen_topic`, `pin_topic` and `unpin_topic`, "pinned" worked out in four places, and the table's row and the buttons written for standing alone, which become one piece every state shares · none
2. publishing-stands-one-way · An article says once whether its author wants it out, and its page, the list and the run all read that one answer; a Connection has one mode and one trouble · `publish` and `retract`, `stop_publishing` and `resume_publishing`, the second record that a grant is needed, and the three readers that disagree about a retracted article · a-topic-and-a-post-stand-one-way
3. the-place-is-one-row · The place's settings read and change at one declared endpoint, `read_place` and `change_place`, with admission, appearance and first steps among its fields, and an offered address is used or declined through one tool · the three reads and five writes of the one settings row, their page-again helpers, `use_offer` and `decline_offer` with the row that only repeats the address, and the repair for a moderators' Tangent that is never missing · a-topic-and-a-post-stand-one-way
4. people-and-their-roles · A role is given and taken through one tool, and never given to someone banned; being a member is read from standing; how the place shows you and what you quieted are fields of your own row · `give_role` and `take_role`, `choose_kind` and `quiet_tangent`, the member role kept as a copy and joined again at every sign-in, the people page's second field for the page a form returns to, with its own check of the path, and the lists' own cursors · the-place-is-one-row
5. who-is-in-a-tangent · Whether you are in a private Tangent is one row with one state, with a way back from declined and from kept out, and joining at once is told in the log as being let in is · the ask and its declined flag, being in guessed from overwrites, `join_tangent`, `leave_tangent` and `let_into_tangent` · people-and-their-roles; it builds decision 17's default
6. your-place-runs · The place runs from a published build at a public address, set up by Leo alone: the README says how, with a proxy, TLS on the default port, the Production environment and the operator named, and a public place limits how fast anyone signs in or writes · — · who-is-in-a-tangent

The realignment is finished when every create, change and delete of a row Tangent owns (a Topic, a Post, a Tangent, the place) is a declared operation, every state changes through its one `set_` route, which sends its command and answers through Koan's `Answers`, and no type stays without a caller. Roles stay with `RolesController`, since their rows are Koan's. publishing-stands-one-way and the-place-is-one-row share no file and may run together.

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
- **17 · Asking to join a private Tangent.** A role's access is one way into a private Tangent; asking, with someone who lets you in, is a second. Does asking stay? Until he answers it stays, as he called it on 6 November 2026, and who-is-in-a-tangent keeps joining, asking, letting in, declining and taking out as one state.
- **18 · Inviting by handle.** Where admission says apply, an account named by its handle is admitted when it first arrives. Does it stay? Until he answers it stays, with `invite` and `withdraw_invitation` as they are.
- **19 · Proprietor and operator.** The place keeps both, first run sets them to the same person, and no route changes either. One field, or two for handing-over? Until he answers both stay.