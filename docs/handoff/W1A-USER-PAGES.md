# W1-A — `/u/{identifier}` participant resolver

Wave 1 brief. One agent. Codebase: `src/server/web` (.NET/Koan) + `wwwroot` JS. Today
participants are DID-keyed (`Participant.Id` = verified DID, optional `Handle`); the GUIDv7
identity collection is a later wave (0b) — build the resolver dispatch for the identifier
kinds that exist now, with the `tangent:local:` branch present but resolving nothing yet.

Decision record: `docs/DECISIONS.md` — "Participant identity: GUIDv7 spine…" and
"Refinement decision round" (D1). Read both before coding.

## Design

`/u/{identifier}` is one resolver handler in `PagesController` (the SPA-shell pattern —
today it serves `index.html` for all page routes, `[AllowAnonymous]`, `Cache-Control:
no-store`):

| Segment | Resolution | Result |
| --- | --- | --- |
| `did:*` | exact `Participant.Get` | if participant holds a handle → **302 to `/u/{handle}`**; else serve the shell (DID is top of chain) |
| `tangent:local:*` | exact (no such participants yet) | honest 404 |
| anything else | handle lookup, exact only | matches current holder → serve the shell (handle **is** the top form); no redirect needed |

Canonicalization chain (top wins): **current handle → DID → `tangent:local:`** (Bluesky
pattern: the pretty handle URL is canonical while held; perennial forms redirect to the
current top form). A handle entered with one leading `@` (`/u/@handle`) resolves and 302s to
the bare `/u/{handle}`.

Miss policy — **honest miss, never fuzzy, never a wrong-entity render**: unknown DID, stale
handle, lookalike handle, or an **ambiguous** handle (held by more than one participant)
return a plain 404. The digest's ambiguity guard (`ExperienceDigest.ResolvesUnambiguously`)
is the precedent. No fallback search.

The redirect is identity-level routing only: suspension/policy never influence it; the
profile API keeps doing all policy gating (its blocked outcomes stay 200-with-status per the
existing contract).

**Kill `/participants/{did}` outright** — server route and every client href (standing PoC
rule: no compat, no aliases).

## Files allowed to touch

- `src/server/web/Web/PagesController.cs` — remove the `/participants/{did}` attribute; add `/u/{identifier}` (resolution + redirect/shell/404 as above).
- **New** `src/server/web/Participants/ParticipantLookup.cs` — the shared exact resolver: `TryResolveByIdentifier(string identifier, CancellationToken)` → `(Participant Participant, string MatchedForm)` with the three dispatch branches. Handle matching semantics **must equal** `CompanionIdentity.Matches` in `src/server/web/Mcp/McpContexts.cs` (~line 152): trimmed, one optional leading `@`, `OrdinalIgnoreCase` against the stored handle; DID `Ordinal`. Do **not** modify the Mcp file this wave — implement the same rule here and note the twin in a doc comment for 0b to unify.
- `src/server/web/Communities/Web/ExperienceController.cs` — the profile action (`[HttpGet("/api/participants/{did}/profile")]`, ~line 73): accept either form via the lookup (route parameter rename to `{identifier}` is fine).
- `src/server/web/Experience/ExperienceService.Profile.cs` — resolve through the lookup; unknown or ambiguous → the existing blocked outcome ("No participant is registered under that identity."). Response shape otherwise unchanged.
- `wwwroot/index.html` — the `#participant-profile` section currently exists **only inside `<noscript>`**, so with JS on, `profile.js` `show()` early-returns and the profile page never renders. Fix: the profile section must exist for JS rendering (keep or adapt the noscript copy). Do not reorder the deferred scripts in `<head>`/`<body>` — load order is an invariant.
- `wwwroot/profile.js` — client route kind changes from `participants` to `u` (`parts[0] === 'u'`); handle the not-found copy for honest misses (the API returns 200-blocked on misses — render the miss copy from the blocked status, not just non-2xx).
- `wwwroot/rooms.js` (~line 387) — author byline href: `/u/` + best form (`page.authorHandles?.[message.authorDid]` when present, else the DID), `encodeURIComponent` always.
- `wwwroot/facets.js` (~lines 219–225) — mention-facet anchors: href `/u/` + best form (`resolution?.handle ?? facet.did`), label unchanged.
- **New** `tests/TangentSpace.Tests/ExperienceIntegration/UserPageResolutionTests.cs` — `[Collection("Experience integration")]`, boot via the existing `ExperienceWebApp` fixture (never a second concurrent host; the collection serializes fixture suites because they set the ambient Koan host — keep that).

Nothing else. In particular: no `ExperienceContracts.cs` changes, no Mcp files, no
`Conversation/` files (W1-B owns them), no Docker/lifecycle scripts, nothing under
`.local/`.

## Invariants

- Exact match only; case-insensitive handle with one optional leading `@`; length guards as in `CompanionIdentity.Matches` (null/whitespace/>253 → miss).
- Ambiguous handle → 404 (never pick a winner).
- `encodeURIComponent` on every path segment built in JS; identifiers contain `:`, `@`, `.`, `-`, `_`.
- UI load order: no script reordering; new markup must not depend on scripts earlier in the page than `profile.js`.
- Tests ride `ExperienceWebApp` + the serialized collection; `ResetDataConfigs` handling lives in the fixture — don't duplicate it.

## Finish conditions

1. New tests cover: DID-with-handle → 302 to `/u/{handle}`; bare handle → 200 shell; DID-without-handle → 200 shell; `@handle` → 302 to bare; case-insensitive handle match; stale handle → 404; lookalike → 404; empty/oversized identifier → 404; profile API by handle returns the canonical DID's data; profile API miss renders the client miss copy.
2. `grep -r "participants/" wwwroot` returns no profile hrefs; `/participants/` route is gone.
3. Full suite green: `dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj` (baseline 351 passing; five owner-claim cases were historical noise already fixed — any new failure is yours).
4. Do **not** `git commit` — leave changes in the working tree.

## Red-team self-check (answer in your report)

- Can any identifier form produce an open-redirect (absolute URL, `//host`, `%2f` tricks)? The redirect target is always built from a stored handle/DID — confirm no user input reaches `Redirect(...)` verbatim.
- Does case-folding the handle collision-check run against **stored** handles as trimmed at save time (`Participant.Return` stores the server-reported handle)?
- Does removing the `/participants/{did}` route break any test or noscript copy still referencing it?
