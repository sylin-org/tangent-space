import test from 'node:test';
import assert from 'node:assert/strict';
import { createWindowStore } from '../src/client/core/window-store.mjs';

const row = (sequence, revision = 1, extra = {}) => ({ id: `post-${sequence}`, sequence, revision, text: `post ${sequence}`, ...extra });
const page = (records, before = null, after = null) => ({ records, before, after });
function setup(budgets = {}) {
  const store = createWindowStore(budgets);
  store.beginSession({ origin: 'https://space.example', participant: 'actor-a' });
  return { store, context: store.openTopic('topic-a') };
}
const replace = (store, context, records, before = null, after = null) =>
  store.applyPage(store.beginRequest(context, { direction: 'replace' }), page(records, before, after));

test('red team: tickets cannot cross session, leave/reopen or evicted Topic generations', () => {
  const { store, context } = setup({ maxTopics: 1 });
  const ticket = store.beginRequest(context, { direction: 'replace' });
  store.openTopic('topic-b');
  const reopened = store.openTopic('topic-a');
  assert.equal(store.applyPage(ticket, page([row(1)])).accepted, false);
  const reopenedTicket = store.beginRequest(reopened, { direction: 'replace' });
  store.leaveTopic(reopened);
  const third = store.openTopic('topic-a');
  assert.equal(store.applyPage(reopenedTicket, page([row(2)])).accepted, false);
  const actorTicket = store.beginRequest(third, { direction: 'replace' });
  store.beginSession({ origin: 'https://other.example', participant: 'actor-b' });
  const newActor = store.openTopic('topic-a');
  assert.equal(store.applyPage(actorTicket, page([row(3)])).accepted, false);
  assert.deepEqual(store.snapshot(newActor).records, []);
  assert.equal(store.stats().pendingRequests, 0);
});

test('red team: forged structurally equal tickets do not bypass object-capability identity', () => {
  const { store, context } = setup();
  const ticket = store.beginRequest(context, { direction: 'replace' });
  assert.equal(store.applyPage({ ...ticket }, page([row(1)])).accepted, false);
  assert.equal(store.applyPage(ticket, page([row(1)])).accepted, true);
});

test('red team: invalid and over-budget replacements preserve content, cursor and anchor', () => {
  const { store, context } = setup({ maxRecordBytes: 256, maxPageBytes: 512 });
  assert.equal(replace(store, context, [row(10)], 'before-10', 'after-10').accepted, true);
  store.setAnchor(context, { id: 'post-10', offset: -24.5 });
  const before = store.snapshot(context);
  for (const bad of [page([row(20, 1, { text: 'x'.repeat(1000) })]), { records: [row(20)], before: undefined, after: null }, page([row(20, NaN)])]) {
    const ticket = store.beginRequest(context, { direction: 'replace' });
    assert.equal(store.applyPage(ticket, bad).accepted, false);
    assert.deepEqual(store.snapshot(context), before);
  }
});

test('red team: both retained records and snapshots resist caller mutation', () => {
  const { store, context } = setup();
  const source = row(1, 1, { facets: [{ range: [0, 1], target: 'actor-a' }] });
  assert.equal(replace(store, context, [source]).accepted, true);
  source.facets[0].range[1] = 999;
  source.text = 'x'.repeat(100000);
  const state = store.snapshot(context);
  assert.equal(state.records[0].facets[0].range[1], 1);
  assert.throws(() => { state.records[0].facets[0].range.push(3); }, TypeError);
  assert.throws(() => { state.pages[0].ids.push('poison'); }, TypeError);
  assert.equal(store.snapshot(context).records[0].text, 'post 1');
});

test('red team: conflicting identity/revision pages reject atomically and cannot downgrade', () => {
  const { store, context } = setup();
  replace(store, context, [row(1, 9)], 'before-1', 'after-1');
  const before = store.snapshot(context);
  for (const records of [[row(1, 9, { text: 'conflict' })], [row(1, 10, { sequence: 2 })], [row(1, 10, { id: 'imposter' })]]) {
    assert.equal(replace(store, context, records).accepted, false);
    assert.deepEqual(store.snapshot(context), before);
  }
  assert.equal(replace(store, context, [row(1, 2, { text: 'stale' })]).accepted, true);
  assert.equal(store.snapshot(context).records[0].revision, 9);
  assert.equal(store.snapshot(context).records[0].text, 'post 1');
});

test('red team: latest lane wins and replacing a window invalidates adjacent-lane work', () => {
  const { store, context } = setup({ maxPages: 1, maxRecords: 2 });
  replace(store, context, [row(1)], 'before-1', 'after-1');
  const olderAfter = store.beginRequest(context, { direction: 'after' });
  const latestAfter = store.beginRequest(context, { direction: 'after' });
  assert.equal(store.applyPage(olderAfter, page([row(2)], 'before-2', 'after-2')).accepted, false);
  // Force a replacement: all pending adjacency requests must lose their generation.
  const replacement = store.beginRequest(context, { direction: 'replace' });
  assert.equal(store.applyPage(replacement, page([row(5)], 'before-5', 'after-5')).accepted, true);
  assert.equal(store.applyPage(latestAfter, page([row(2)], 'before-2', 'after-2')).accepted, false);
  assert.deepEqual(store.snapshot(context).records.map(r => r.sequence), [5]);
});

test('red team: a boundary-page replacement rejects a ticket tied to the previous page', () => {
  const { store, context } = setup();
  replace(store, context, [row(5)], 'scan-before-5', 'scan-after-5');
  const after = store.beginRequest(context, { direction: 'after' });
  const before = store.beginRequest(context, { direction: 'before' });
  assert.equal(store.applyPage(before, page([], 'scan-before-4', null)).accepted, true);
  const state = store.snapshot(context);
  assert.equal(store.applyPage(after, page([row(6)], 'before-6', 'after-6')).reason, 'edge_moved');
  assert.deepEqual(store.snapshot(context), state);
  const freshAfter = store.beginRequest(context, { direction: 'after' });
  assert.equal(store.applyPage(freshAfter, page([row(6)], 'before-6', 'after-6')).accepted, true);
});

test('red team: refusing an anchor-pinned growth preserves the original continuation', () => {
  const { store, context } = setup({ maxPages: 1, maxRecords: 2 });
  replace(store, context, [row(1), row(2)], 'before-1', 'after-2');
  store.setAnchor(context, { id: 'post-1', offset: -12 });
  const before = store.snapshot(context);
  const ticket = store.beginRequest(context, { direction: 'after' });
  assert.equal(store.applyPage(ticket, page([row(3)], 'before-3', 'after-3')).accepted, false);
  assert.deepEqual(store.snapshot(context), before);
});

test('red team: empty progressing pages do not accumulate and stalled cursors reject', () => {
  const { store, context } = setup();
  replace(store, context, [row(1)], null, 'scan-0');
  for (let index = 1; index <= 500; index++) {
    const ticket = store.beginRequest(context, { direction: 'after' });
    assert.equal(store.applyPage(ticket, page([], null, `scan-${index}`)).accepted, true);
    assert.equal(store.stats().pages, 1);
    assert.equal(store.stats().records, 1);
  }
  const before = store.snapshot(context);
  const ticket = store.beginRequest(context, { direction: 'after' });
  assert.equal(store.applyPage(ticket, page([], null, 'scan-500')).reason, 'cursor_stalled');
  assert.deepEqual(store.snapshot(context), before);
});

test('red team: live and read checkpoints are independent of fetched windows', () => {
  const { store, context } = setup();
  replace(store, context, [row(90), row(91)], 'b90', 'a91');
  assert.equal(store.snapshot(context).readCheckpoint, null);
  assert.equal(store.snapshot(context).liveCheckpoint, null);
  store.setLiveCheckpoint(context, { cursor: 'live100', sequence: 100 });
  store.acknowledgeRead(context, { cursor: 'read4', sequence: 4 });
  replace(store, context, [row(5)], 'b5', 'a5');
  assert.deepEqual(store.snapshot(context).readCheckpoint, { cursor: 'read4', sequence: 4 });
  assert.deepEqual(store.snapshot(context).liveCheckpoint, { cursor: 'live100', sequence: 100 });
  assert.equal(store.acknowledgeRead(context, { cursor: 'read3', sequence: 3 }), false);
});

test('red team: aggregate memory has bounded records/pages across repeated Topic churn', () => {
  const { store } = setup({ maxTopics: 3, maxPages: 2, maxRecords: 5, maxTotalRecords: 7, maxTotalBytes: 10000 });
  for (let index = 0; index < 500; index++) {
    const context = store.openTopic(`topic-${index % 13}`);
    assert.equal(replace(store, context, [row(index * 5), row(index * 5 + 1), row(index * 5 + 2)]).accepted, true);
    const stats = store.stats();
    assert.ok(stats.topics <= 3);
    assert.ok(stats.records <= 7);
    assert.ok(stats.pages <= 6);
    assert.ok(stats.bytes <= 10000);
    assert.equal(stats.pendingRequests, 0);
  }
});
