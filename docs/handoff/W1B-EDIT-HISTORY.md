# W1-B — Edit-history changelog partition + change classification

Wave 1 brief. One agent. Codebase: `src/server/web` (.NET/Koan, SQLite). This mounts the
app's **first** Koan partition and **first** generic entity read surface — the framework
APIs are proven (gposingway governed reads, Koan framework tests) but unused here, so verify
signatures against the local framework source at `E:\repo\github\sylin-org\koan-framework`
(read-only) as you go. Pattern references: gposingway
`E:\repo\github\gposingway\gposingway-org\src\Gposingway\Catalog\Infrastructure\WorkAccess.cs`
(read-only).

Decision record: `docs/DECISIONS.md` — "Refinement decision round" (D2, D3, D4, D5, D6) and
REFINEMENTS item 2. Read both before coding.

## Design

### 1. Changelog snapshots

On **every** applied change to a live `Message` in `ConversationService.ChangePost`
(`src/server/web/Conversation/ConversationService.PostChanges.cs`) — author edit, author
delete, moderation removal, on both the local and Spaces branches — copy the **full pre-edit
row** into one shared `changelog` partition before mutating the live row:

- Snapshot = a `Message` entity with: `Id` = fresh GUIDv7; every pre-edit field copied
  verbatim (text, facets, authorship, removal state as it was); plus
  `OfMessageId` = live row Id; `PreviousChangeId` = the live row's current `ChangeId`
  (null for the original's first snapshot); `ChangeClass` = classification result (below).
- Live row mutation adds: `ChangeId` = new snapshot Id (pointer moves to the newest
  snapshot).
- `Message` (`src/server/web/Conversation/Message.cs`) gains nullable `OfMessageId`,
  `PreviousChangeId`, `ChangeId`, `ChangeClass` — null on live rows (except `ChangeId`).
- Write via `Entity<Message>.Insert(snapshot, "changelog")` — insert-only, so snapshots are
  write-once by construction (fresh GUIDv7 never collides). **Never** `Upsert`/`Replace` into
  the partition. SQLite materializes it as `TangentSpace.Conversation.Message#changelog`
  (adapter partition separator `#`; DATA-0094).
- Snapshot insert, live-row mutation, `PostChange` record, and the `ActivityJournal` append
  happen in the **same transaction** the method already uses. Idempotent re-delivery must
  not double-snapshot: the existing prior-`PostChange` short-circuit already returns before
  mutation — keep that ordering and assert it with a test.
- `PostChange` remains the operation ledger; the changelog is the state ledger. History
  starts at adoption; no backfill (standing wipe/PoC rule — no legacy handling).

### 2. Edit accepts facets (D2a)

`ConversationController.Edit` request (`PostChangeRequest`) gains optional facets (same DTO
shape the create path uses). Validated with `PostFacets.Check`. In `ChangePost`:

- Facets provided → store them on the new live content (local branch).
- Facets absent → **server re-detection**: mint mention and group facets from the new text
  using the same deterministic candidate rules as the digest parser
  (`src/server/web/Experience/ExperienceMentions.cs` — `Candidates`/`GroupCandidates`/
  `Addresses`, code-fence and boundary rules, exact-handle matching, ambiguity guard).
  Byte ranges come from the token offsets. Deterministic, no model. If create has no
  tag/topic minting to mirror, mint mentions+groups only and note it.
- Spaces branch live row keeps the existing honest drop (`Facets = null` on the record) —
  but the snapshot keeps the pre-edit facets, so each era's structure rides its snapshot.
- Conflict check parity with create (`UpsertLocalPost` in `ConversationService.Writes.cs`):
  prior `PostChange` found → conflict when delete/text/**or `Canonical(facets)`** differ.
  Reuse the `Canonical` helper; do not fork it.

### 3. Change classification (D5+D6: both layers, at edit time, stored on the snapshot)

`ChangeClass` carries `{ SurfaceDistance, TokenOverlap, FacetDelta, SemanticDistance,
Classifier }` (nullable semantics):

- **FacetDelta** — exact structural diff of mention facets old→new: DIDs added, removed,
  "retargeted" (label range moved to a different DID counts as remove+add). Groups: name
  added/removed. No model; this is the trusted signal.
- **Surface metrics** — normalized Levenshtein distance and token-set (Jaccard) overlap
  between old and new text. Deterministic; document the exact formulas in code comments.
- **SemanticDistance** — cosine distance between old/new text embeddings via Koan's ONNX
  connector (package `Koan.AI.Connector.Onnx`; DI auto-registers via module discovery; config
  section `Koan:Ai:Onnx` with `ModelPath`; unset path = adapter inactive and boot logs
  `inactive (model-not-configured)`). Resolve `IAiPipeline`/`IEmbedAdapter`; vectors are
  L2-normalized (`NormalizeEmbeddings`), so cosine = dot product. When the embedder is
  inactive, store `SemanticDistance = null` and mark `Classifier` accordingly ("semantic:
  inactive") — never fake a number.
- **Classifier** — id+version string, e.g. `facet+levenshtein+jaccard+onnx:all-MiniLM-L6-v2`.
  Model identity available from the adapter (`IAiSourceInspector.Name`/`DefaultModel`).
- Deleted/moderated rows: classify old text vs empty (surface/facet still meaningful;
  semantic per embedder availability).
- **Digest rule: compute once at edit time, store on the snapshot, reads are pure lookups.**
  No classification at read time, ever.

**Model artifacts:** fetch the two files (ONNX graph + wordpiece vocab) exactly per
`E:\repo\github\sylin-org\koan-framework\src\Connectors\AI\Onnx\README.md` ("Get the model
artifacts"), commit them under `src/server/web/models/`, wire
`<Content Include="models\**" CopyToOutputDirectory="PreserveNewest" />` in
`TangentSpace.csproj`, and set `Koan:Ai:Onnx:ModelPath` in `appsettings.json` (relative to
build output). Check the Dockerfile — if publish output alone doesn't carry the artifacts
into the image, adding the needed copy line is the **one** fence exception allowed; flag it
in your report. If the download is impossible (offline), ship with `ModelPath` unset,
semantic nulls, and say so loudly in the report — do not fake or skip the honesty path.

### 4. History reads on the generic entity surface (D3+D4)

- New `EntityController<Message, string>` subclass (your route choice, e.g.
  `api/history/messages`) — mounting pattern per framework: subclass
  `Koan.Web.Controllers.EntityController<TEntity,TKey>`; reads flow through
  `EntityEndpointService`; `?set=changelog` selects the partition
  (`EntityContext.With(partition: …)` internally; `?set=` is read by the framework — you do
  not parse it).
- **This surface is changelog-only and read-only.** Default-partition reads (no `set`,
  or any other `set`) must be denied here, and **all** writes/deletes through it must be
  denied — the changelog is write-once history mutated only by domain code. Choose the
  mechanism against the framework source (action overrides if virtual; realization gates;
  a `Set` check against the bound `EntityRequestContext`) — cite what you chose and why.
- Gating per D4b: new `EntityAccess<Message>` realization (auto-discovered via
  `[KoanDiscoverable]`/`IEntityAccessRealization`): reads return a snapshot row only if the
  viewer is that row's author (`AuthorDid == viewer`) **or** holds a moderation-capable role
  for that row's room (`RoomKey` is on the snapshot row — precompute the viewer's moderator
  room set, then `Constrain(q, AccessAction.Read) => q.Where(row => row.AuthorDid == viewer
  || moderatorRooms.Contains(row.RoomKey), partition: "changelog")`). Per-row control lives
  in the predicate — e.g. snapshots of moderation-removed posts are still author+moderator
  visible. If the realization cannot constructor-inject the governance service, find the
  framework-supported alternative (gposingway resolves everything from `Principal`) and
  cite it.
- Anyone else (other members, anonymous) gets an honest empty/404 per the framework's
  constrained-read behavior.

## Files allowed to touch

`src/server/web/Conversation/ConversationService.PostChanges.cs`,
`ConversationService.Writes.cs` (conflict-canonical reuse only, no behavior change to
create), `ConversationController.cs`, `Message.cs`, `PostFacet.cs` (only if the diff helper
belongs there — prefer **new** `Conversation/ChangeClassification.cs`),
`appsettings.json`, `TangentSpace.csproj`, the Dockerfile (one copy line max, flagged),
**new** `Conversation/ChangeClassification.cs`, `Conversation/MessageHistoryController.cs`,
`Conversation/MessageHistoryAccess.cs`, `models/` artifacts, and **new**
`tests/TangentSpace.Tests/ExperienceIntegration/EditHistoryTests.cs` +
`tests/TangentSpace.Tests/ChangeClassificationTests.cs` (unit).

Off-limits: everything in `wwwroot/` (the viewer is wave 2), the Rust connector, Mcp files,
`Experience/` service files (a revision marker in topic windows is wave 3), existing test
files (extend `ExperienceWebApp` seeding only additively if genuinely required), Docker
lifecycle scripts, `.local/`.

## Invariants

- Snapshots are **insert-only**; the changelog partition never sees upsert/replace/remove.
- Snapshot + live mutation + `PostChange` + activity journal in one transaction; idempotent
  re-delivery produces no second snapshot.
- Post text stays verbatim forever; facets ride their era's snapshot; the new current
  version gets provided-or-re-detected facets.
- Classification computes at edit time only; reads never classify (digest rule).
- New integration tests join `[Collection("Experience integration")]` and boot via
  `ExperienceWebApp` — the ambient-host serialization exists because these fixtures set
  `AppHost.Current` and Koan's static data-config cache; never create a competing host.
- No migration/compat code anywhere (standing PoC rule).

## Finish conditions

1. New integration tests cover: edit → one snapshot with `PreviousChangeId` null and live
   `ChangeId` pointing at it; second edit → two-deep chain; facets ride the era (snapshot
   keeps old facets, live has new); provided-facet edit stores them; absent-facet edit
   re-detects mentions deterministically; delete and moderation each snapshot; re-delivered
   edit operation → no new snapshot, same receipt; same operation key + different facets →
   409; history read: author sees own, moderator sees room's, other member sees nothing,
   anonymous denied; non-`changelog` set denied on the history surface; a write attempt via
   the history surface denied; a `?filter=` query cannot bypass the row gate.
2. Unit tests: facet diff (add/remove/retarget), Levenshtein/Jaccard bounds, semantic-null
   when embedder inactive (and a real-value test when the model is present — mark it so it
   skips honestly if artifacts are absent).
3. Full suite green: `dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj`.
4. Do **not** `git commit` — leave changes in the working tree.

## Red-team self-check (answer in your report)

- Can a client reach the changelog partition through any **other** surface with weaker
  gates? (Create no new generic surfaces; confirm the realization gates this controller.)
- Can a client write/upsert/delete through the history controller in any combination of
  `set`, verb, or body `"set"` property?
- Does the edit-time transaction hold under failure between snapshot insert and live-row
  update (same transaction = all-or-nothing — prove with the ordering in code, not hope)?
- Is the conflict check's facet comparison canonical-order-insensitive (same facets,
  different order → replay, not conflict)?
- Does the `?filter=` interplay compose with the access predicate (WEB-0068 applies
  predicates server-side — confirm the constrained count/headers stay honest)?
- Is the moderator-room precomputation done once per request, not per row?
