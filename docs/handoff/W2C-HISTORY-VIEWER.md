# W2-C — Edit-history viewer UI

Wave 2 brief. One agent. Primarily `wwwroot` JS; one narrow C# exception (below). Runs
parallel with the connector work (W2-A) — do not touch `src/server/mcp`. The history
backend shipped in wave 1: `GET /api/history/messages?set=changelog` (generic entity
surface; every mutating verb denied; per-row gate: author or moderation-capable viewer —
other members get honest empty). Snapshots carry `OfMessageId` (the live post id),
`PreviousChangeId`, `Content.Text`, `Facets`, `ChangeClass { surfaceDistance,
tokenOverlap, facetDelta, semanticDistance, classifier }`, timestamps.

## Design

- **Affordance**: posts whose payload carries an edit marker show "Edited · view
  history" in the meta line (next to the timestamp). Removed/tombstone rows: no
  affordance. If the current message payload lacks `editedAt`/`changeId`, you may add
  exactly those two fields to the message DTO(s) — see fence.
- **Viewer**: an inline disclosure under the post (the house pattern — closed
  disclosure, keyboard accessible, matches existing `<details>`/disclosure styling in
  rooms.js). Fetch the post's snapshots from `api/history/messages?set=changelog` with a
  filter on `ofMessageId` (read the vendored framework's EntityController
  `?filter=`/query DSL at `.local/upstream/koan-framework/src/Koan.Web/Controllers/`
  to build the correct filter — do not guess the syntax; sort client-side by the chain
  `previousChangeId` walk, newest first). Render each era: verbatim text (facet
  decoration reused from facets.js where ranges are present — degrade to plain text
  otherwise), author label from the page's resolution map, timestamp, and a compact
  change chip derived from `ChangeClass`: mention changes from `facetDelta`
  (`+@who`/`−@who`/retarget as remove+add), surface distance as a percentage, semantic
  distance rendered only when non-null with the classifier id in a `title`. Bounded:
  render at most the 20 newest eras with an honest "older revisions omitted" line.
- **Empty/denied**: no snapshots (never edited) or an empty gated response → the viewer
  shows "No recorded revisions" honestly; never an error tone.
- **No new server endpoints.** Everything consumes the existing surface.

## Files allowed to touch

`wwwroot/rooms.js` (meta line + disclosure wiring), **new** `wwwroot/history.js`
(viewer; add its `<script defer>` AFTER rooms.js and facets.js in index.html — load
order is an invariant; bump the version query on every changed JS file), `index.html`
(script include only). Narrow C# exception, only if needed: adding `EditedAt`/`ChangeId`
to the message DTO(s) built in `Conversation/MessagePage.cs` / the Experience posts DTO
— two fields, populated from the entity, nothing else; if you touch them, say so
explicitly in the report. Off-limits: everything else in `src/server/web`,
`src/server/mcp`, tests (a `dotnet test` run is still required if you touch C#),
docs, `.local/`, git state.

## Finish conditions

1. `node --check` passes on every touched JS file (the repo's JS syntax gate); if C#
   touched, `dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj` green
   (baseline 385).
2. Manual-style verification via the integration fixture is out of your fence — instead
   verify against the live local server read-only if it has edited posts, else state
   honestly what was verified only by code reading.
3. Do NOT `git commit`.

## Red-team self-check (answer in your report)

- Does the viewer ever render text that is NOT from the snapshot payload (no client-side
  reconstruction)? Facet decoration only when ranges validate against the era's own text?
- Is the fetch URL always scoped by ofMessageId (no unbounded collection pull)?
- Does the disclosure work with keyboard only (Enter/Space), and does it preserve the
  composer draft when opened/closed (the disappearing-composer bug class)?
- Script load order: history.js may depend on rooms.js/facets.js globals — confirm the
  include order and that it degrades if they are absent.
