import test from 'node:test';
import assert from 'node:assert/strict';
import { createWindowStore } from '../src/client/core/window-store.mjs';

const row = (sequence, extra = {}) => ({ id: 'p' + sequence, sequence, revision: 0, text: 'post ' + sequence, ...extra });
const page = (records, before = null, after = null) => ({ records, before, after });
function fixture(budgets = {}) {
  const store = createWindowStore({ maxRecords: 6, maxPages: 3, maxPageRecords: 4, ...budgets });
  store.beginSession({ origin: 'https://tangent.example', participant: 'alice' });
  const context = store.openTopic('lounge');
  const apply = (direction, data, anchorId) => store.applyPage(store.beginRequest(context, { direction, anchorId }), data);
  return { store, context, apply, view: () => store.snapshot(context), ids: () => store.snapshot(context).records.map(record => record.id) };
}

test('overlapping and reordered records retain stable chronological identities', () => {
  const f = fixture();
  assert.equal(f.apply('replace', page([row(2), row(1), row(2)], 'b1', 'a2')).accepted, true);
  assert.equal(f.apply('after', page([row(3), row(2), row(4)], 'b2', 'a4')).accepted, true);
  assert.deepEqual(f.ids(), ['p1', 'p2', 'p3', 'p4']);
  assert.equal(f.view().recordCount, 4);
  assert.equal(f.view().readCheckpoint, null);
  assert.equal(f.view().liveCheckpoint, null);
});

test('latest request on a lane wins and a response cannot be applied twice', () => {
  const f = fixture(); f.apply('replace', page([row(1), row(2)], null, 'a2'));
  const old = f.store.beginRequest(f.context, { direction: 'after' });
  const latest = f.store.beginRequest(f.context, { direction: 'after' });
  assert.equal(f.store.stats().pendingRequests, 1);
  assert.equal(f.store.applyPage(old, page([row(3)], 'b3', 'a3')).reason, 'superseded_request');
  assert.equal(f.store.applyPage(latest, page([row(3)], 'b3', 'a3')).accepted, true);
  assert.equal(f.store.applyPage(latest, page([row(4)], 'b4', 'a4')).reason, 'superseded_request');
  assert.deepEqual(f.ids(), ['p1', 'p2', 'p3']);
});

test('independent opposite edges can finish in either order while their boundaries remain', () => {
  for (const reverse of [false, true]) {
    const f = fixture(); f.apply('replace', page([row(5), row(6)], 'b5', 'a6'));
    const before = f.store.beginRequest(f.context, { direction: 'before' });
    const after = f.store.beginRequest(f.context, { direction: 'after' });
    const deliveries = [[before, page([row(3), row(4)], 'b3', 'a4')], [after, page([row(7), row(8)], 'b7', 'a8')]];
    if (reverse) deliveries.reverse();
    for (const [ticket, data] of deliveries) assert.equal(f.store.applyPage(ticket, data).accepted, true);
    assert.deepEqual(f.ids(), ['p3', 'p4', 'p5', 'p6', 'p7', 'p8']);
  }
});

test('an evicted boundary rejects its late adjacent response without moving cursors', () => {
  const f = fixture({ maxPages: 2 }); f.apply('replace', page([row(5), row(6)], 'b5', 'a6'));
  const oldBefore = f.store.beginRequest(f.context, { direction: 'before' });
  f.apply('after', page([row(7), row(8)], 'b7', 'a8'));
  f.store.setAnchor(f.context, { id: 'p8', offset: -19 });
  f.apply('after', page([row(9), row(10)], 'b9', 'a10'));
  const view = f.view();
  assert.equal(f.store.applyPage(oldBefore, page([row(3), row(4)], 'b3', 'a4')).reason, 'edge_moved');
  assert.deepEqual(f.view(), view);
  assert.equal(f.view().before, 'b7');
});

test('replacement navigation invalidates older destinations and adjacent requests', () => {
  const f = fixture(); f.apply('replace', page([row(1)], null, 'a1'));
  const adjacent = f.store.beginRequest(f.context, { direction: 'after' });
  const firstJump = f.store.beginRequest(f.context, { direction: 'replace', anchorId: 'p100' });
  const secondJump = f.store.beginRequest(f.context, { direction: 'replace', anchorId: 'p200' });
  assert.equal(f.store.beginRequest(f.context, { direction: 'after' }), null);
  assert.equal(f.store.applyPage(firstJump, page([row(100)], 'b100', 'a100')).reason, 'stale_context');
  assert.equal(f.store.applyPage(adjacent, page([row(2)], 'b2', 'a2')).reason, 'stale_context');
  assert.equal(f.store.applyPage(secondJump, page([row(200)], 'b200', 'a200')).accepted, true);
  assert.deepEqual(f.ids(), ['p200']);
});

test('Topic, origin and participant transitions reject every old view callback', () => {
  const f = fixture(); f.apply('replace', page([row(1)], null, 'a1'));
  const late = f.store.beginRequest(f.context, { direction: 'after' });
  const other = f.store.openTopic('other');
  assert.equal(f.store.applyPage(late, page([row(2)], 'b2', 'a2')).reason, 'stale_context');
  assert.equal(f.store.setAnchor(f.context, { id: 'p1' }), false);
  assert.equal(f.store.setLiveCheckpoint(f.context, { cursor: 'old', sequence: 2 }), false);
  assert.equal(f.store.acknowledgeRead(f.context, { cursor: 'old', sequence: 2 }), false);
  assert.equal(f.store.snapshot(f.context), null);
  assert.equal(f.store.beginRequest(f.context, { direction: 'replace' }), null);
  f.store.beginSession({ origin: 'https://another.example', participant: 'bob' });
  assert.equal(f.store.snapshot(other), null);
  assert.deepEqual(f.store.stats(), { topics: 0, records: 0, bytes: 0, pages: 0, pendingRequests: 0 });
  const bob = f.store.openTopic('lounge');
  assert.equal(f.store.snapshot(bob).recordCount, 0);
  assert.equal(f.store.applyPage(late, page([row(2)], 'b2', 'a2')).reason, 'stale_context');
});

test('leaving to a non-Topic route invalidates transport callbacks but keeps a bounded window', () => {
  const f = fixture(); f.apply('replace', page([row(1)], null, 'a1'));
  const ticket = f.store.beginRequest(f.context, { direction: 'after' });
  assert.equal(f.store.leaveTopic(f.context), true);
  assert.equal(f.store.applyPage(ticket, page([row(2)], 'b2', 'a2')).reason, 'stale_context');
  assert.equal(f.store.stats().pendingRequests, 0);
  const back = f.store.openTopic('lounge');
  assert.equal(f.store.snapshot(back).recordCount, 1);
});

test('edge-page eviction preserves the selected ID, offset and resumable boundary', () => {
  const f = fixture({ maxRecords: 4 });
  f.apply('replace', page([row(5), row(6)], 'b5', 'a6'));
  f.apply('after', page([row(7), row(8)], 'b7', 'a8'));
  f.store.setAnchor(f.context, { id: 'p5', offset: -27.5 });
  assert.equal(f.apply('before', page([row(3), row(4)], 'b3', 'a4')).accepted, true);
  assert.deepEqual(f.ids(), ['p3', 'p4', 'p5', 'p6']);
  assert.equal(f.view().after, 'a6');
  assert.deepEqual(f.view().anchor, { id: 'p5', sequence: 5, offset: -27.5 });
  f.store.setAnchor(f.context, { id: 'p6', offset: 13 });
  const ticket = f.store.beginRequest(f.context, { direction: 'after' });
  assert.equal(ticket.cursor, 'a6');
  assert.equal(f.store.applyPage(ticket, page([row(7), row(8)], 'b7', 'a8')).accepted, true);
  assert.deepEqual(f.ids(), ['p5', 'p6', 'p7', 'p8']);
  assert.equal(f.view().anchor.id, 'p6');
});

test('anchor pressure rejects an unusable new page atomically instead of bypassing caps', () => {
  const f = fixture({ maxRecords: 2 }); f.apply('replace', page([row(1), row(2)], null, 'a2'));
  f.store.setAnchor(f.context, { id: 'p1', offset: 4 });
  const previous = f.view();
  assert.equal(f.apply('after', page([row(3), row(4)], 'b3', 'a4')).reason, 'anchor_budget');
  assert.deepEqual(f.view(), previous);
  assert.equal(f.store.stats().records, 2);
  assert.equal(f.apply('replace', page([row(10), row(11), row(12)], 'b10', 'a12'), 'p11').reason, 'window_budget');
  assert.deepEqual(f.view(), previous);
});

test('an unavailable anchor chooses the nearest survivor, preferring its successor on a tie', () => {
  const f = fixture(); f.apply('replace', page([row(10), row(11), row(12)], 'b10', 'a12'));
  f.store.setAnchor(f.context, { id: 'p11', offset: -12 });
  assert.equal(f.apply('replace', page([row(10), row(12)], 'b10', 'a12'), 'p11').accepted, true);
  assert.deepEqual(f.view().anchor, { id: 'p12', sequence: 12, offset: -12 });
  assert.equal(f.view().anchorUnavailable, 'p11');
  f.store.setAnchor(f.context, { id: 'p12', offset: 0 });
  assert.equal(f.view().anchorUnavailable, null);
});

test('read acknowledgment and live delivery are independent of fetching and anchoring', () => {
  const f = fixture(); f.apply('replace', page([row(1), row(2)], null, 'a2'));
  f.store.setLiveCheckpoint(f.context, { cursor: 'live-99', sequence: 99 });
  assert.equal(f.view().readCheckpoint, null);
  f.store.acknowledgeRead(f.context, { cursor: 'read-2', sequence: 2 });
  f.apply('after', page([row(3), row(4)], 'b3', 'a4'));
  f.store.setAnchor(f.context, { id: 'p4', offset: 0 });
  assert.deepEqual(f.view().readCheckpoint, { cursor: 'read-2', sequence: 2 });
  assert.deepEqual(f.view().liveCheckpoint, { cursor: 'live-99', sequence: 99 });
  assert.equal(f.store.acknowledgeRead(f.context, { cursor: 'old-read', sequence: 1 }), false);
  const restored = f.store.openTopic('lounge');
  assert.equal(f.store.snapshot(restored).recordCount, 4);
  assert.equal(f.store.snapshot(restored).readCheckpoint, null);
  assert.equal(f.store.snapshot(restored).liveCheckpoint, null);
});

test('retained revisions never regress and conflicting stable identities fail atomically', () => {
  const f = fixture(); f.apply('replace', page([row(1), row(2)], null, 'a2'));
  f.apply('after', page([row(2, { revision: 2, text: 'edited' }), row(3)], 'b2', 'a3'));
  assert.equal(f.view().records.find(record => record.id === 'p2').text, 'edited');
  f.apply('after', page([row(2), row(3)], 'b2', 'a4'));
  assert.equal(f.view().records.find(record => record.id === 'p2').revision, 2);
  const previous = f.view();
  assert.equal(f.apply('after', page([row(2, { id: 'impostor' })], 'b2', 'a5')).reason, 'identity_conflict');
  assert.deepEqual(f.view(), previous);
  assert.equal(f.apply('after', page([row(2, { revision: 2, text: 'conflicting edit' })], 'b2', 'a5')).reason, 'revision_conflict');
  assert.deepEqual(f.view(), previous);
});

test('a page cannot smuggle new IDs into the middle of a loaded adjacent window', () => {
  const f = fixture(); f.apply('replace', page([row(1), row(3)], null, 'a3'));
  const previous = f.view();
  assert.equal(f.apply('after', page([row(2)], 'b2', 'a2')).reason, 'non_adjacent_page');
  assert.deepEqual(f.view(), previous);
});

test('input, snapshots, records and nested payloads cannot mutate retained accounting', () => {
  const f = fixture(); const original = row(1, { content: { facets: [{ label: 'one' }] } });
  f.apply('replace', page([original], null, 'a1'));
  original.content.facets[0].label = 'mutated externally';
  const view = f.view(), bytes = view.serializedBytes;
  assert.equal(view.records[0].content.facets[0].label, 'one');
  assert.throws(() => { view.records[0].content.facets[0].label = 'mutated snapshot'; }, TypeError);
  assert.throws(() => { view.records.push(row(2)); }, TypeError);
  assert.throws(() => { view.pages[0].ids.push('p2'); }, TypeError);
  assert.throws(() => { view.serializedBytes = 0; }, TypeError);
  assert.equal(f.view().serializedBytes, bytes);
});

test('exact UTF-8 JSON accounting includes Unicode, escapes, lone surrogates and nested structures', () => {
  const f = fixture();
  const value = row(1, { text: '🛰中\n\t\u0000"\\\ud800', nested: { a: [true, false, null, 1.23, -0] } });
  assert.equal(f.apply('replace', page([value])).accepted, true);
  assert.equal(f.view().serializedBytes, new TextEncoder().encode(JSON.stringify(f.view().records[0])).length);
});

test('page count, per-record bytes and aggregate encoded response bytes reject hostile input', () => {
  const f = fixture({ maxRecordBytes: 100, maxPageBytes: 140 });
  assert.equal(f.apply('replace', page([row(1, { text: 'x'.repeat(1000000) })])).reason, 'response_budget');
  assert.equal(f.apply('replace', page([row(1), row(2), row(3), row(4), row(5)])).reason, 'response_budget');
  assert.equal(f.apply('replace', page([row(1), row(2), row(3)])).reason, 'response_budget');
  assert.equal(f.store.stats().records, 0);
  const wideKey = { ...row(1), ['x'.repeat(1000000)]: '' };
  assert.equal(f.apply('replace', page([wideKey])).reason, 'response_budget');
  assert.equal(f.store.stats().bytes, 0);
});

test('serialized-byte pressure evicts complete pages and inactive windows respect an aggregate cap', () => {
  const encoded = new TextEncoder().encode(JSON.stringify(row(1))).length;
  const f = fixture({ maxBytes: encoded * 3, maxTotalBytes: encoded * 4, maxTotalRecords: 4 });
  f.apply('replace', page([row(1), row(2)], null, 'a2'));
  const other = f.store.openTopic('other');
  f.store.applyPage(f.store.beginRequest(other, { direction: 'replace' }), page([row(3), row(4)]));
  const third = f.store.openTopic('third');
  f.store.applyPage(f.store.beginRequest(third, { direction: 'replace' }), page([row(5), row(6)]));
  assert.equal(f.store.stats().topics, 2);
  assert.equal(f.store.stats().records, 4);
  assert.ok(f.store.stats().bytes <= encoded * 4);
  const evicted = f.store.openTopic('lounge');
  assert.equal(f.store.snapshot(evicted).recordCount, 0);
});

test('empty permission-scan pages advance with constant metadata and reject stalled cursors', () => {
  const f = fixture(); f.apply('replace', page([row(100)], 'b100', 'a100'));
  for (let index = 0; index < 1000; index++) {
    assert.equal(f.apply('before', page([], 'scan-' + index, 'ignored')).accepted, true);
    assert.equal(f.store.stats().pages, 1);
    assert.equal(f.store.stats().records, 1);
  }
  const previous = f.view();
  assert.equal(f.apply('before', page([], 'scan-999', 'ignored')).reason, 'cursor_stalled');
  assert.deepEqual(f.view(), previous);
  assert.equal(f.apply('before', page([], null, 'ignored')).accepted, true);
  assert.equal(f.store.beginRequest(f.context, { direction: 'before' }), null);
});

test('long deterministic traversal reaches fixed retained bounds with no historical ID index', () => {
  const f = fixture(); f.apply('replace', page([row(1), row(2)], null, 'a2'));
  for (let sequence = 3; sequence < 2003; sequence += 2) {
    const last = f.view().records.at(-1);
    f.store.setAnchor(f.context, { id: last.id, offset: -3 });
    assert.equal(f.apply('after', page([row(sequence), row(sequence + 1)], 'b' + sequence, 'a' + (sequence + 1))).accepted, true);
    const count = f.store.stats();
    assert.ok(count.records <= 6); assert.ok(count.pages <= 3); assert.equal(count.pendingRequests, 0);
    assert.ok(f.view().pages.flatMap(entry => entry.ids).length <= 6);
  }
  assert.deepEqual(f.ids(), ['p1997', 'p1998', 'p1999', 'p2000', 'p2001', 'p2002']);
  assert.equal(f.view().records.some(record => record.id === 'p1'), false);
});

test('request slots and retained Topic scopes do not grow with repeated navigation', () => {
  const f = fixture();
  for (let index = 0; index < 1000; index++) {
    const context = f.store.openTopic('topic-' + index);
    for (let retry = 0; retry < 10; retry++) f.store.beginRequest(context, { direction: 'replace' });
    assert.ok(f.store.stats().topics <= 3);
    assert.equal(f.store.stats().pendingRequests, 1);
  }
});
