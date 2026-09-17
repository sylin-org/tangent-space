# Continuation ledger — autonomous session, 2026-09-17

Leo went to bed and handed this over: *"Fix all the issues. I'll let you take this
autonomously."* This file records every step as it happens, so a new session can resume
from a cold start if this one dies. **Read this top to bottom, then `git log --oneline -8`
and `git status --short` to confirm where reality actually is.**

The epic's own state of record stays [docs/epics/epic-007/LEDGER.md](docs/epics/epic-007/LEDGER.md).
This file is the *session* log: finer-grained, disposable once the work lands.

## Standing constraints (do not violate while resuming)

- **Claude never enters credentials.** Any atproto sign-in is Leo's. Leo is asleep, so
  anything needing a sign-in is blocked, not to be worked around.
- **Do not reach past missing UI to make a walkthrough step pass.** Leo stopped exactly
  that earlier today (N-062). A step that needs a UI that does not exist is blocked.
- Commits at task checkpoints are authorized (D10); push to
  `origin/claude/epic-007-realignment` is authorized.
- Wipes are pre-authorized but must be announced and backed up first. `full.bat` takes no
  backup by design — do not use it while the install carries walkthrough state Leo has not
  seen.

## Where things stood when this session began

- Branch `claude/epic-007-realignment`, clean, pushed through `863b148`.
- Install at `http://127.0.0.1:5220` is claimed by Leo, healthy, carrying walkthrough state:
  Tangent `home` ("Projects"), Topic `open-questions` (Public, 33 posts), one removed post,
  one edited post.
- Walkthrough: **W1–W3, W7, W8, W9, W10 pass.** W4, W6 and W8's live bar are blocked on a
  second author (needs W5 → Leo's sign-in). W11 is n/a until R4.7.
- Two findings recorded and pushed: **N-061** (walkthrough results), **N-062** (the join
  split brain; no user list).

## The issues Leo asked me to fix

1. **The join is dishonest.** `PUT /api/v1/experience/tangents/{key}/membership` answers
   `200 / status: ok` and writes `Tangent.Community.TangentMembership {home, <participant>,
   Member}`, while access decides from Koan roles where `role:member` carries `Members: []`.
   The caller is told "Welcome to your Tangent" and granted nothing.
2. **No user list.** No endpoint in the server enumerates `Participant`.
   `/api/roles/ui/participants` takes `id[]` and only hydrates ids the caller already holds.
   A participant holding no role is invisible to the product.
3. **The orphaned membership row** my own call created — a state no UI can produce or show.
   Must be cleared before W11 is ever run for real.

## Scope decision (made deliberately, before touching code)

**In scope tonight:** issue 2 (build the user list — Leo asked for it outright), issue 3
(cleanup), and whatever part of issue 1 is a *truthfulness* bug rather than an access-model
decision.

**Out of scope tonight: R4.7 (the arrival model) and R3.4 (the Access evaluator).** Both
have accepted decisions behind them (D11, D12, ADR 0013) and N-030 already assigns this
exact failure to R3.4. Improvising either overnight would pre-empt a settled design. If the
join's root cause turns out to *be* R3.4, the honest output is a precise diagnosis plus the
user list, not a point-fix that adds writes to a store R4.5 deletes.

## Step log

Newest last. Each entry: what I did, what happened, what it means for a resumer.

- **[1] Created this file.** No code touched yet.
- **[2] Diagnosed issue 1 conclusively. Decided NOT to fix it, on purpose.**
  `ExperienceService.ListTopics` denies when `TangentGovernance.Describe` returns null, and
  `Describe` returns null at `src/server/web/Community/TangentGovernance.cs:49`:
  `if (!owner && !TangentRoleAccess.CanDo(effective.See, bag)) return null;`
  `bag` is the **Koan role bag**. The `TangentMembership` row is loaded on line 44 and used
  only for display (`membership?.Role`) and topic filtering — never for the `See` decision.
  The home Tangent's default `See` is `@Member`.
  **Why I am not patching it:** `Member` is a role **D12 deletes** ("four built-in roles —
  everyone, participant, administrator, owner — and no more; Member is not one of them"),
  and granting a server-wide `role:member` on joining one Tangent would over-grant across
  every Tangent. N-030 already assigns this exact failure to **R3.4's Access evaluator**.
  Any point-fix here writes to a model that D11/D12 replace and R4.5 deletes. The honest
  output is the diagnosis, which is now precise enough for R3.4 to act on directly.
  **Resumer: do not "fix" this without Leo. It is a design decision already made.**
- **[3] Built the user list, server side. Builds clean.**
  - New `src/server/web/Spaces/SpaceOwnership.cs` — the owner check stated once. Two
    controllers were growing private copies; `RoleUiController` now calls the shared one and
    lost its own (net reduction, not new duplication). It also had `using Tangent.Identity;`
    twice; removed.
  - New `src/server/web/Api/PeopleController.cs` — `GET /api/people?page=N`, owner-gated,
    reads the `Participant` spine itself (the one store that knows everyone), hydrates
    profile + atproto DID + granted role tokens, paged 50 with `AllWithCount`.
    Ambient `everyone`/`authenticated` tokens are filtered out so they cannot read as grants.
  - `dotnet build` succeeds.
- **[4] Endpoint verified live.** `GET /api/people` returns both participants with handle,
  display name, avatar, arrival times, classification and roles (Leo `["owner"]`, Lumen `[]`).
- **[5] Built the People panel and found a second duplicated rule while doing it.**
  - `index.html`: a People tab in the settings strip + `#settings-people-panel`.
  - New `src/server/web/wwwroot/people.js` — renders rows; an unroled arrival gets a dashed
    `no roles` chip, which is the whole point: that state was previously invisible.
  - `landing.css`: `.people-list` / `.person-row` styles, matching the role-directory rhythm.
  - **The bug this exposed:** `access.js:246` held a *second, independent copy* of "which tab
    does the URL ask for", knowing only `access` and `roles`. For `tab=people` it fell through
    to `'server'` and **clicked** it, stripping the query — the panel loaded its data and was
    then closed by the other copy. Fixed by extracting one resolver, `requestedTab`, exported
    from roles.js as `window.TangentSettingsTabs.requested(contextKind)`; access.js now calls
    it instead of re-deciding. Same disease as the owner check in step [3].
  - Verified in the browser: `/settings?tab=people` opens the tab, lists Leo (owner) and
    Lumen (no roles, undeclared), "2 people have arrived here."
  - **Cache-busting bit me twice:** editing a `.js` after bumping its `?v=` serves the stale
    file. Bump *after* the last edit. Versions now `people3` for roles.js/access.js.
- **[6] Tests written — and they exposed a latent suite defect.**
  New `tests/.../PeopleDirectoryTests.cs`: a roleless arrival is listed; non-owner gets 403;
  page 0 is refused. All three passed alone, then the **full suite went 5 red** — in my new
  class *and* in `AccessSettingsTests`, with `TransactionException: Deferred coordination
  'tangent-topic-administration' failed`. Cause: `AccessSettingsTests` was **missing
  `[Collection("Experience integration")]`**, so it ran in parallel with other host fixtures.
  It was green only while it was the *sole* unattributed class; my second one collided with
  it. Added the attribute to both. **The suite was green by luck, not by design** — R6.3
  already owns this area.
- **[7] All suites green.** .NET **90/90**, node **7/7**, connector **52/52**,
  greenfield **508** (unchanged from HEAD).
- **[8] Cleared the orphaned membership row (issue 3).**
  Backed up first: `.local/backups/sqlite-20260917-011042-tangent.sqlite`. Stopped the
  container, deleted the single `Tangent.Community.TangentMembership` row for Lumen, restarted
  (ready 1.6 s). Verified: table empty, **both participants still listed**, owner's Tangent
  access unchanged (`status: ok`). The row was a grant obtainable through no path a person can
  take; once R3.4 makes membership authoritative it would silently have become real.
- **[9] Ledger updated:** N-063 written; R4.7's row notes that the owner's side of the door now
  exists and that it should treat both sides as one door; Resume here checkpointed.
- **[10] Next: commit + push, then final report.**
