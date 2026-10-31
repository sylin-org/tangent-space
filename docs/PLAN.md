# Development plan

How the tree is built: in packages, by roles that each have a model and an effort, in parallel where packages are independent, behind the same gates every time. [FEATURES](FEATURES.md) is the list of packages and carries their status; the `work-split` skill applies this plan to one package at a time. Models and rates are those released on 28 October 2026; the role table is re-read when the lineup changes.

## Roles

| Role | Does | Model | Effort | Runs as |
| --- | --- | --- | --- | --- |
| **Lead** | Product and architecture decisions, ADRs, briefs for every package, reading results, Koan design, the conversation with Leo | Claude Opus 5.5 | high; xhigh only while deciding a spine question or Koan semantics | the main session |
| **Builder, first of its kind** | A package that establishes a pattern the tree has not got yet: the writer, the HTML projection, the evaluator, the door, the first Connection package, anything in Koan | Claude Opus 5.5 | high; xhigh for a spine package or Koan semantics | a session of its own, in a worktree |
| **Builder, repeat** | A package that repeats an established pattern: a second settings screen, a second Connection package, feeds, a phone layout | Claude Opus 5.5 | high, set explicitly (its own default is medium) | a session of its own, in a worktree |
| **Explorer** | Finds and reads: inventories, Koan source lookups, "where is X handled", research with the web | Claude Sonnet 5.5 | medium | the project's `Explore` agent (`.claude/agents/explore.md`), which pins Sonnet at medium; the built-in Explore would run on the session's own model |
| **Mechanic** | Bulk, checkable edits from an exact instruction: a rename sweep, language-check fixes, fixture data, test scaffolds from a spec | Claude Haiku 5.5 | low, medium when the instruction has judgment in it | a subagent |
| **Scorer** | The visual loop's independent judgment on captures | Claude Opus 5.5 | high | the `scorer` agent (`.claude/agents/scorer.md`), given only captures, reference, spec and rubric |
| **Second scorer** | The tie-break within 0.3 of the gate | Claude Sonnet 5.5 | high | the same agent run with `model: "sonnet"` — a different family |
| **Reviewer** | Correctness review of a package before Leo's commit word; the spine's and Koan's reviews | Claude Opus 5.5 | high; xhigh for a spine package, Connections and any Koan change | the `reviewer` agent (`.claude/agents/reviewer.md`), given the diff and the brief, never the builder's reasoning |

Claude Fable 5.1 is held back: from 29 October 2026 no role runs on it unless Leo names it for a package, because its quota is spent faster than the work it buys over Opus at `high` or `xhigh`; slice 0's packages built on Fable are the record of what it did.

Rates, first-party API, per million tokens in and out: Fable 5.1 $10 / $50 · Opus 5.5 $4 / $20 · Sonnet 5.5 $2 / $10 · Haiku 5.5 $0.10 / $0.50 (prompts up to 100K). A builder session is one model for its whole life; a model switch happens between sessions, never inside one.

Why these: Anthropic's own guidance for the current lineup — `high` for most work and `xhigh` only for the most capability-sensitive, with lower effort on the newest models often matching the top effort of the previous ones; a fresh-context verifier outperforms self-critique; parallel sub-agents are dependable and should be used for independent pieces, with a brief that states objective, output, boundaries and done; an orchestrator pays off only when there is bulk to hand off, and one dependent chain is cheaper on one model at lower effort than on a planner plus workers; measure cost per completed package, not per request. Across the industry the same split holds: the planner stays strong, the executor is the swappable part. Claude Code's documentation uses the effort levels the same way — `low` for quick, reviewed steps; `medium` for day-to-day scoped work; `high` where verification or edge cases matter; `xhigh` for deeper reasoning at a higher cost; `max` for hard problems run unattended — and defaults Opus 5.5, Sonnet 5.5 and Haiku 5.5 to `medium`, so a builder's `high` is always set explicitly.

## The loop, mechanized

The planner is the lead in the main session. The executor is a session the lead starts with the `work-split` skill: a fresh context, its own worktree and branch, the role's model and effort, the brief as its first message. The executor commits on its branch when its checks pass and ends its turn with the report; the app returns the report to the lead. The lead verifies from the diff, scores any screen, runs the reviewer, and either sends feedback to the same session — which keeps its context — or presents the branch to Leo. Leo merges; nothing is pushed on anyone's word but his. An executor run by hand works the same way from a brief file and a pasted report.

## The package cadence

1. **Brief.** The lead writes the package brief: the FEATURES rows it closes, the outcome, the files or area, what is out of bounds, how done is checked, the role and effort. A brief that would mostly retell what the lead already knows means the work stays with the lead.
2. **Explore**, only when the builder needs a map it does not have. The explorer returns conclusions, not file listings.
3. **Build.** The builder plans inside its own session, implements, runs the one relevant build or test plus the affected happy path, then the rule checks: language, greenfield, no dialogs, ports.
4. **Screens.** Anything a participant sees goes through the `visual-loop` skill, scorer included, before review.
5. **Review.** The reviewer gets the diff and the brief. Findings are fixed or named in the package report; nothing is hidden.
6. **Leo.** The commit word, then the push word. The package report leads with the outcome and audits every claim against a tool result.

A failure escalates one step at a time: the builder re-runs the failing check at higher effort once; still failing, the lead looks; still failing, Leo hears about it with the evidence.

## Delegation rules

- One package, one session, one model. Split a package only when its pieces are independent — no piece needs another's result, and none touch the same files.
- Fan out for bulk: an audit across many files, a review of a whole slice, research across many sources. A fan-out costs roughly fifteen times a conversation; it is for breadth, never for a chain.
- Independence is the point of a subagent review or score: the reviewer and the scorer never see the builder's reasoning.
- Parallel sessions run in worktrees, at most four at once, each on its own branch cut from `dev`. An accepted package merges into `dev` with `--no-ff`, `dev` is pushed as it lands, and the package's worktree, branch and session are removed; no more branches exist than packages in flight. `main` fast-forwards from `dev` when a slice closes. Koan packages do the same against Koan's `dev`, and Tangent re-pins.
- The lead keeps working while sessions run and reads their reports when they arrive; it intervenes when a session is off track or missing context, and never predicts a result.
- Nothing waits without a bound: an executor runs its checks in the foreground with timeouts and never ends a turn waiting on a background result; the lead schedules one check after each executor starts and treats silence past it as a hang to diagnose and end (the `work-split` skill names the steps).
- Koan work runs in the Koan checkout under Koan's contributor law, by a first-of-its-kind builder, and is reviewed at the lead's model.
- Fast mode is for interactive, mechanical stretches on Opus 5.5 only; it changes speed, not the model.
- The `Workflow` tool (ultracode) runs only on Leo's word, for slice-wide audits and reviews.
- A settled repeat package may run on the `opusplan` alias — Opus plans, Sonnet executes inside one session; the review model does not change.
- Five or more independent repeat packages of one shape are a `/batch` run: a worktree and a branch each, reviewed one by one.
- Project settings pin no model and no effort: the lead passes both per session and per subagent. An `effortLevel` setting would override Leo's own sessions and is his to set.
- Lessons that would help a later session go to memory, one per note; project state lives in FEATURES status, ADRs and commits, nowhere else.

## The tracks

Three tracks run beside each other.

- **T — the tree**, slices 0 to 9 of FEATURES.
- **K — Koan**, the four briefs: K1 SQLite posture and base URL, K2 overwrites with deny, K3 atproto content, K4 lexical search. Each is a first-of-its-kind package in the Koan checkout.
- **D — design**, mocks for the screens that have no reference (the door, take this on a tangent, bring your agent, consent, connections), each approved by Leo before it becomes a target.

Dependencies: K1 before slice 0 closes; K2 before slice 1 closes (slice 1 composes its own overwrite step until then); K3 before slice 3; K4 before slice 6; D before the first visual loop of each of its screens.

## Slice 0, package by package

| Package | After | Role | Group |
| --- | --- | --- | --- |
| 0.1 Server skeleton: `Sylin.Koan.App`, SQLite WAL with immediate write transactions, fixed port, trusted proxy, base URL under a path prefix, health | K1 or its experiment | first of its kind | A |
| 0.2 The writer, and the Koan transaction proven inside a loop | 0.11 | first of its kind, xhigh | A |
| 0.3 The HTML projection: one DTO through a Razor component or JSON by `Accept`, fragments by header | 0.1 | first of its kind | A |
| 0.4 Island bootstrap and the stream-driven fragment patch | 0.3 | repeat | A |
| 0.5 Design foundation: tokens, fonts, layout, Paper and Ink, four accents | — | repeat | B |
| 0.6 Capture script for the visual loop | 0.5 | repeat | B |
| 0.7 Rule checks: language, no dialogs, greenfield scoped to the root, fixed ports | — | explorer-grade, Sonnet 5.5 medium | B |
| 0.8 Test fixture on `Sylin.Koan.Testing.Hosting`, once under a path prefix | 0.1 | repeat | A |
| 0.9 Lifecycle: one binary and one folder; build, launch, backup by `VACUUM INTO` and copy, restore; compose | — | repeat | B |
| 0.10 Lexical search proven through the SQLite connector | 0.1 | explorer-grade, Sonnet 5.5 medium | A |
| 0.11 Re-pin to Koan's `dev` with K1: adopt `Koan:Web:ApplicationUrl`, `Koan:Web:TrustedProxies` and the SQLite posture; delete what Koan now owns | K1, 0.9 | first of its kind | A |

Group A is a chain from 0.1; group B is independent of it. Four sessions at once: 0.1 then its chain; 0.5 then 0.6; 0.7; 0.9. Slice 0 opens with its hardest unknowns — the transaction in a loop, the formatter, the island patch, the lexical index — because that is where a Koan correction, if one is needed, is cheapest to make.

## Slice 1, package by package

| Package | After | Role |
| --- | --- | --- |
| 1.1 Nouns and tables: `TangentSpace`, `Tangent`, `Post` with the root as Topic, `Participant` facet, `Overwrite`, `Event`, `Checkpoint` | 0.2 | first of its kind |
| 1.2 Sign-in through the atproto connector, the person reconciled, the participant facet, the profile job, declared kind | 1.1 | repeat |
| 1.3 Access: roles seeded, overwrites, `Capabilities.For`, `can` on every resource, `gate` from the explainer | 1.1; K2 composes | first of its kind, xhigh |
| 1.4 First run: operator, name, base URL, proprietor and operator, pointer snippets | 1.2 | repeat |
| 1.5 The door, as one resource, both ways | 1.1–1.3 | first of its kind |
| 1.6 The Tangent page | 1.1, 1.3 | repeat |
| 1.7 Discovery: `tangent.json`, `llms.txt`, the site's pointers, the proxy rule | 1.4 | explorer-grade |
| 1.8 The delegated agent door: the consent page, the Device flow, a pre-registered client | 1.2 | repeat |
| 1.9 Phone layouts for the door and the Tangent | the polish stage | repeat |
| 1.10 Access on Koan's overwrites: Tangent's own overwrite step lifted onto K2's resolution and explainer | K2, 1.3 | repeat |
| 1.11 Tangent on Koan's K6 and K7: the pin moves, the delegated agent door composes Koan's resource edge and consent seam, and Tangent's own copies of them go | K6, K7, 1.8 | repeat |
| 1.12 First run without questions: one action sets the place up, `Tangent:Operator` names the operator by handle or DID, the name comes from the handle | 1.4, 2.8 | repeat |
| 1.13 The address learned and offered: a notification to the operator carries an Offer to use the address requests arrive on | 1.12 | first of its kind |
| 1.14 Sign in with Bluesky, and signing in sets the place up: one button starts at bsky.social, another server by handle, and the sign-in itself establishes the place | 1.12 | repeat |

Parallel once 1.1 lands: 1.2 and 1.3 together; then 1.4 and 1.6 together; then 1.5, 1.7 and 1.8 together, with K2 in the Koan checkout beside them; 1.10 once K2 merges. Phone layouts are visual work and wait for the polish stage; until then every screen is captured at 390 and must read there without a horizontal scroll.

## Slice 2, package by package

| Package | After | Role |
| --- | --- | --- |
| 2.1 The topic window: a Topic as one resource, bounded, cursors are sequence numbers, anchor first; plain bodies until 2.3 | 1.6 | first of its kind |
| 2.2 The write verbs over HTTP: start a Topic, reply, edit, remove through the writer; TID ids as idempotency keys; pre-edit snapshots in the audit partition; visible tombstones | 1.8 | first of its kind, xhigh |
| 2.3 Markdown: sanitized HTML, mentions as identity-bearing links resolved at render, `mention` events written with the Post | 2.2 | repeat |
| 2.4 The composer island: drafts by scope, an uncertain submit reconciled by `GET` of the minted Id, never auto-posts | 2.1, 2.2 | first of its kind |
| 2.5 Checkpoints and the watch flag: reading a Topic moves its checkpoint; watch on a Tangent or a Topic | 2.1 | repeat |
| 2.6 Live: the event stream, one per page, the fragment patch; the door as the poll | 2.1, 1.5 | repeat |
| 2.7 Take this on a tangent: choose the Tangent, a root Post with an origin, linked both ways | 2.2, 2.4 | first of its kind |
| 2.8 Reports: a Post taken on a tangent into the moderators' Tangent, with the origin | 2.7 | repeat |
| 2.9 Phone layout for the topic | the polish stage | repeat |
| 2.10 Mentions a person can type: a typed `@handle` of a participant here becomes their mention when the Post is written | 2.3 | repeat |
| 2.11 Moderating a Post: a mark on a Post someone else changed, and the author told | 2.2, 6.1 | repeat |

Parallel once slice 1 lands: 2.1 and 2.2 together; then 2.3, 2.4 and 2.5; then 2.6 and 2.7; 2.8 last.

## Slice 3, package by package

| Package | After | Role |
| --- | --- | --- |
| 3.4 Social cards: a page a visitor may read carries a card from what it already read, through Koan's `RenderHead` (WEB-0085) | 2.3, 4.2 | repeat |

## Slice 4, package by package

| Package | After | Role |
| --- | --- | --- |
| 4.2 Feeds: Atom and JSON Feed for each public Tangent and the place's articles, a bounded window of rendered bodies, advertised by the pages and named by discovery | 2.3, 5.7 | repeat |

## Slice 5, package by package

Slice 5 runs before slice 3. A place its proprietor cannot shape is a dead end for everyone, as [JOURNEYS](JOURNEYS.md) walks it: setting up leaves no public Tangent and nothing to make one with.

| Package | Depends on | Role |
| --- | --- | --- |
| 5.5 Creating a Tangent, and its settings: name, description, default kind, order, audience | 1.10 | repeat |
| 5.6 The proprietor's place: a first Tangent made at setup, the place's own name and description, the ready page's pointers once the place has a public address, and "Run your own" in the README | 5.5, 1.13 | repeat |
| 5.3 The people here: a directory with seek cursors; naming moderators and administrators; time out, ban and lift inline | 5.5 | repeat |
| 5.2 Admission: join or apply; standing; the one knob "new accounts post after approval" | 5.3 | repeat |
| 5.4 A participant's profile | 5.3 | repeat |
| 5.1 The roles at `/settings/roles` over Koan's order, given and taken on People; each Tangent's access by role, its overwrites beyond its audience; and the place's own audience | 5.3 | first of its kind |
| 5.7 Any number of Tangents and members, on Koan's roles as rows: every list of Tangents reaches every one the reader may view, a page at a time | 5.4 | repeat |
| 5.8 An application reaches whoever decides it: on a place set to apply, whoever holds the bit to decide reads the waiting applications on their bell, in their notifications and on their door | 5.2, 6.1 | repeat |
| 5.9 The small doors: a settings rail of what its reader may use, and a welcome on a newcomer's first door | 5.3 | repeat |

## Slice 6, package by package

| Package | Depends on | Role |
| --- | --- | --- |
| 6.1 Notifications at `/notifications`, and their count on every page | 1.10 | repeat |
| 6.2 A report reaches whoever may act on it: on their bell, in their notifications and live on their stream | 2.8, 6.1 | repeat |
| 6.3 Search at `/search`, one resource both ways, and a box in the masthead that suggests as someone types | K4, K28 | repeat |
| K28 Lexical search reads a word being typed and takes a scope (DATA-0165) | K4 | repeat (Koan) |

## Slice 7, package by package

| Package | After | Role |
| --- | --- | --- |
| 7.1 Export: one Tangent or the whole place as a zip of readable pages beside their data, streamed in bounded reads through the one renderer, at `/settings/export` | 5.7, 5.1 | first of its kind |
| 7.2 Static archive and retirement: every public page and feed in a folder under the same URLs, by the operator's `archive` verb, and `Tangent:Archive`, which answers from it and takes no writes | 7.1 | first of its kind |

## Slice 8, package by package

| Package | After | Role |
| --- | --- | --- |
| 8.3 An agent as moderator, administrator, proprietor or operator: proven end to end with no branch in access; `llms.txt` says how it signs in as itself, sends a session's token and finds what a role gives it | 1.12, 5.3, 8.1 | repeat |

## Slice 9, package by package

| Package | After | Role |
| --- | --- | --- |
| 9.1 Appearance: each reader's theme with no flash, the place's accent at `/settings/appearance`, the first four tokens named as `site.standard.theme.basic` names them | 5.1 | repeat |
| 9.2 Slots: the place's own HTML at `/settings/appearance`, sanitized on save and bounded per slot, the same on the site, in exports and in the archive | 9.1, 7.2 | first of its kind |

## Later slices

The same rule assigns every later package. First of its kind: the topic window and the write verbs (2), the composer island (2), take this on a tangent (2), the article editor (3), the Connection row and dispatch job (3), the atproto package (3), the protocol contract ADR (4), the role editor over Koan (5), the static archive (7), the DID-native door (8), the labeler (8). Repeat: Markdown rendering, live patching, checkpoints, reports, phone layouts, media on Koan, social cards, Webmention, Mastodon, the connections screen, join and standing, the people directory, the profile, Tangent settings, notifications, watching, search, backup and restore, bring your agent, `did:web`. Explorer-grade: references through Constellation, the A2A card.

A slice closes when its packages work and FEATURES says so. Until Leo calls the polish stage, screens are built from the frame and the existing components, captured, and listed in `docs/SCREENS.md` as owing the visual gate; the polish stage holds every one of them to it.

## What is measured, slice by slice

After each slice the lead records in its report: packages completed and by which role; reviewer findings per package; visual-loop rounds per screen; escalations and what resolved them; Koan corrections made. Two slices of that are an effort sweep in practice: a role whose packages pass review first time at `high` is tried at `medium`; a role that escalates is moved up a model, not up an effort. The role table changes only on that evidence.

## Slice 0 report

Closed 29 October 2026 at `e664cb0`, `main` fast-forwarded from `dev`.

| Package | Role, model, effort | Review rounds with findings | Visual rounds | Escalations | Koan |
| --- | --- | --- | --- | --- | --- |
| 0.1 Server skeleton | first of its kind, Fable high | 1 | — | — | K1 briefs 1 and 2 written from its probes |
| 0.7 Rule checks | explorer-grade, Sonnet medium | 1 (parser gaps) | — | — | — |
| 0.5 Design foundation | repeat, Opus high | 1 | — | — | — |
| 0.3 HTML projection | first of its kind, Fable high | 1 (any-method 404, exception to problem, ETag per representation, HEAD, encoded path) | — | — | non-sealed DTO loses negotiation: queued |
| 0.8 Test fixture | repeat, Opus high | 1 (binding test) | — | — | `KoanIntegrationHost` cannot host Kestrel: queued |
| 0.4 Island bootstrap | repeat, Opus high | 1 | — | — | static files under a path: fixed by K1b |
| 0.6 Capture script | repeat, Opus high | 1 | — | — | — |
| 0.9 Lifecycle | repeat, Opus high | 2 (restore under a running server) | — | — | — |
| 0.11 Re-pin | first of its kind, Fable high | 2 (launch held the caller's pipe; the handle change was permanent) | — | the hang: guardrails added | adopts K1a and K1b |
| 0.2 The writer | first of its kind, Fable xhigh | 1 (unknown commit outcome, replay announced, scope opening, a sentence) | — | — | Communication's entity-bound events do not carry a generic commit: noted |
| 0.10 Lexical search | explorer-grade, Sonnet medium | 1 (page overflow, cached reading, a forbidden word) | — | — | page overflow in `LexicalQuery`: queued; count-only `Status`: queued |
| K1a SQLite posture and atomic commit | first of its kind (Koan), Fable high | 1 (busy timeout and the driver's timeout, one native unit per connection identity, simultaneous saves) | — | — | DATA-0158 |
| K1b Canonical base URL | first of its kind (Koan), Fable high | 2 (six findings, then a `:80` issuer mismatch) | — | — | WEB-0078 |
| K4 Lexical search | first of its kind (Koan), Fable high | 1 (missing-index refusal, a warning, bounded readiness) | — | — | DATA-0159 |

No screen in this slice reached the visual loop: the door's skeleton has no reference and waits on its mock. Every package needed one review round; two needed two. No package escalated to Leo on its content; the one escalation was the 0.11 hang, answered by the no-unbounded-wait rule, the executor preamble, the lead's scheduled check and the orphan finder. Koan corrections made: K1a, K1b, K4; queued for its status task: the non-sealed DTO guard, a web-capable test host, the `TenancyAmbient` flake, the SqliteVec fixture posture, the MCP `ResourceUri` contract, the `LexicalQuery` page overflow, the count-only lexical `Status`, two `CS0419` and three `CS1573` warnings, the PostgreSQL suite's host-spelling timeouts, and the test output root shared across checkouts.

Reading for the effort sweep: Fable at `high` and `xhigh` and Opus at `high` each passed review on the second round at most, with one round the norm; Sonnet at `medium` did the same on explorer-grade packages. Nothing here moves a role yet; the second slice decides.
