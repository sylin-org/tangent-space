# Browser retained-window contract

Status: P2 framework-independent model and deterministic tests for EPIC-005. This is
not an integrated browser, wire protocol, virtualizer, or measured heap/viewport result.
The application still uses its existing routes and history client.

Implementation: `src/client/core/window-store.mjs`. Tests:
`node --test tests/window-contract.test.mjs`.

## Ownership and bounds

The session owns a small set of retained Topic windows. A window owns bounded adjacent
pages and one canonical record per retained post ID. Pages retain their own older/newer
cursors, so removing an edge page restores the remaining edge's continuation. No array,
ID set, profile map, measured-height map, or synthetic pixel range represents the full
Topic behind this model. Records absent from all retained pages are discarded.

Initial model defaults, independently configurable for experiments:

| Resource | Bound |
| --- | --- |
| Retained Topics | 3, least recently activated inactive Topic evicted first |
| Pages per Topic | 16 |
| Unique records per Topic | 1,000 |
| Canonical UTF-8 JSON record content per Topic | 8 MiB |
| Unique records across retained Topics | 3,000 |
| Canonical UTF-8 JSON record content across retained Topics | 16 MiB |
| Records / content in one response page | 200 / 2 MiB |
| One record's canonical JSON content | 64 KiB |
| One opaque cursor | 4,096 UTF-8 bytes |
| Outstanding logical requests | One replacement, or at most one per adjacent edge |

The decoded JSON copier also limits record nesting to 32 and visited JSON values to
16,384. It accounts JSON UTF-8 bytes while traversing, before serializing the copied
record, and rejects enormous individual strings/keys by length before allocating a
second encoding. It rejects accessor properties and non-JSON data. These limits bound
this model's retained content and bookkeeping; they are not exact JavaScript heap caps.
The transport must enforce encoded and decoded response limits before JSON parsing.

Record data is frozen recursively. Snapshots have frozen records and copied/frozen
container arrays; mutating input after delivery cannot change retained data/accounting.
Consumers must release obsolete snapshots, route closures and rendered records. Holding
every old snapshot externally would defeat aggregate heap bounds. Logical ticket bounds
likewise do not abort a network request: the loader must cancel/coalesce superseded work.

## Input contract and API

```js
const store = createWindowStore();
store.beginSession({ origin: location.origin, participant: 'participant-reference' });
const context = store.openTopic('topic-key');
const request = store.beginRequest(context, { direction: 'replace', anchorId: 'post-id' });
store.applyPage(request, {
  records: [{ id: 'post-id', sequence: 42, revision: 0, text: 'Example' }],
  before: 'opaque-older-window-cursor',
  after: 'opaque-newer-window-cursor',
});
store.setAnchor(context, { id: 'post-id', offset: -12.5 });
const view = store.snapshot(context);
```

`participant` is a stable participant reference, or `null` for an anonymous session.
Origin is normalized to its HTTP(S) origin. A session change destroys every retained
Topic and invalidates all prior view/request tokens. Tokens use object identity, not
caller-reconstructable fields. The model has no credential or authentication authority.

`openTopic(topic)` returns a fresh immutable view context and preserves only that Topic's
bounded cached pages/anchor when available. `leaveTopic(context)` invalidates the view
when navigating to home/settings/etc. All requests, anchor updates and checkpoint updates
require the current context. A late callback from another Topic, a previous visit to the
same Topic, another participant or another origin cannot apply to the current view.
`snapshot(context)` returns `null` for a stale context.

`beginRequest(context, {direction, anchorId?})` supports `replace`, `before`, and `after`.
Adjacent requests take their opaque cursor from the current edge and return `null` if
that edge is at its end or a replacement is pending. `anchorId` belongs to a replacement
request, such as a direct post jump. Beginning a replacement invalidates older replacement
and adjacent requests. Beginning another request on one edge supersedes that edge's old
ticket. The route loader owns fetch cancellation; the model owns admission of results.

`applyPage(ticket, {records, before, after})` consumes its ticket and returns
`{accepted, reason}`. Both cursor fields are required and are either a nonempty string or
`null` for a verified end. A failed response leaves pages, records, anchor and cursors
unchanged; callers obtain a new ticket to retry. They must not spin automatically on a
budget/integrity failure. Stale, superseded, moved-edge, invalid, nonadjacent, stalled,
response-budget and retained-window-budget failures are distinguished.

The normalized record has a bounded nonempty `id`, a nonnegative safe-integer
`sequence`, and a nonnegative safe-integer `revision`; its remaining fields are JSON.
Sequences order posts within a Topic and are immutable and unique. Response row order
does not matter. Repeated IDs deduplicate; an existing record's sequence cannot change.
Two IDs claiming one sequence are an integrity failure. A lower revision cannot overwrite
a retained newer revision. Equal revision with different content is an integrity failure.

**The monotonic integer `revision` is a proposed P2 wire addition or normalization
requirement, not a claim about the current history response.** Current `ChangeId` values
are not an ordering primitive. Never derive revision order from GUID lexical order,
timestamps, or response completion order. The server/API work must supply an appropriate
revision or explicitly adopt invalidation-and-refetch semantics before integration.
The store forgets versions for evicted posts; authoritative current reads remain the
server's responsibility.

An adjacent response may overlap retained IDs. New IDs must extend the requested edge;
an allegedly newer page introducing a new ID inside the retained sequence range is
rejected. Opposite-edge requests can complete in either order while their captured edge
pages remain. If another result evicts/replaces the captured edge, its late result is
rejected and a fresh cursor is required. The model cannot prove that a server cursor
omitted no authorized rows: scope, ordering, adjacency and current-policy delivery are
API correctness requirements.

An empty or fully overlapping permission-filtered page can advance its requested cursor
without retaining an extra page. A repeated unchanged cursor on such a page is rejected.
An empty result with a continuation never means end-of-history or end-of-directory.

## Eviction, anchors and checkpoints

The anchor is one retained post ID, its sequence and viewport-relative CSS-pixel offset.
`setAnchor(context, {id, offset})` can only select a retained ID. Initial replacement
chooses the requested post when present, otherwise the first returned post. If a known
anchor disappears during replacement, the model selects the surviving record nearest
its former sequence, preferring the successor on a tie, preserves its offset, and exposes
the missing ID as `anchorUnavailable`. If a never-loaded target is absent, the server's
returned window has no known target sequence here; the first returned record is the
deterministic fallback. An empty window has no invented anchor. Tombstones may retain a
post's original ID/sequence and thus preserve the anchor naturally.

Eviction removes only whole first/last pages, selecting the more distant removable edge
while keeping the anchor present. Count, bytes and page count apply together. If one
indivisible page does not fit, or all useful fetched rows would immediately be evicted
to retain the anchor, the result is rejected atomically. The loader must request a smaller
window or wait for an intentional reader/anchor move. No pinned/focused-row exception
may bypass a cap. Empty/overlap-only continuations remain constant-size metadata.

`setLiveCheckpoint(context, {cursor, sequence})` stores transient live-delivery state.
`acknowledgeRead(context, {cursor, sequence})` records a successful explicit read
acknowledgment reported by its external owner; it performs no network write. Each method
rejects backward sequence movement within that active view. Page delivery, prefetch,
eviction, `setAnchor`, and a live checkpoint never advance read acknowledgment.

These two checkpoint fields reset on view activation/deactivation. The authoritative
participant/Topic read position remains in the server/application read store; returning
to a Topic reloads/reconciles it and obtains fresh scoped continuation cursors. Clearing
view cursors does not erase a server acknowledgment. Drafts, facets, reply targets,
selection and uncertain outbound operation IDs likewise live outside this disposable
window model, scoped by origin, participant and Topic, with no automatic resend.

## P3 integration obligations

- Render at most 200 chronological message rows including overscan and retained focus;
  measure variable heights only for retained records, and evict measurement/profile
  decoration with their records. Use spacers for bounded cached neighbors only, never
  a million-row synthetic scroll track.
- Preserve post ID plus offset within 2 CSS px after layout settles on prepend/eviction,
  delayed media/fonts, edit/delete and pane-width changes. The model selects the anchor;
  a real browser virtualizer and controlled scroll surface must perform this correction.
- Keep the composer outside disposable rows. Keyboard focus, active edit and text
  selection require an explicit bounded policy when their rows approach eviction; if
  retaining them and the reading anchor cannot fit, pause paging or move intentionally.
- New posts follow only an already live-following reader. Otherwise coalesce a bounded
  activity marker and leave the historical window/anchor intact. Keep the historical
  continuation and fresh live tail independent.
- Refresh edited/deleted retained content with a bounded around-anchor request or
  versioned retained-record update. Do not refetch all history preceding the anchor.
- Preserve Back/Forward, direct around-post/unread/latest/start entry, focus/title,
  explicit acknowledgment, access revocation and an accessible history/search path for
  records absent from the DOM. Transport resets and identity changes invalidate caches
  before new content can render.

The deterministic tests cover overlap/order/revision conflicts, generation races,
edge eviction and cursor continuity, missing anchors, UTF-8 accounting, hostile large
values, mutation attempts, independent checkpoints, empty scan continuation, 1,000
two-post traversals, and 1,000 repeated Topic activations. They establish model behavior
under those fixtures. They do not prove browser heap plateau, DOM limits, 2 px anchoring,
network response limits, API revision correctness, database scale, or independent
red-team closure. Those remain the epic's explicit integration/measurement gates.
