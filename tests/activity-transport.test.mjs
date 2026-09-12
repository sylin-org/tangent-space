import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('../src/server/web/wwwroot/activity-transport.js', import.meta.url), 'utf8');
const encoder = new TextEncoder();
const settle = async () => { for (let index = 0; index < 50; index++) await Promise.resolve(); };
function deferred() { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
function clock() {
  let time = 0, next = 1; const timers = new Map();
  return {
    now: () => time,
    setTimeout(callback, delay) { const id = next++; timers.set(id, { callback, due: time + delay }); return id; },
    clearTimeout(id) { timers.delete(id); },
    get pending() { return timers.size; },
    async advance(ms) {
      const until = time + ms; let iterations = 0;
      await settle();
      while (true) {
        const pending = [...timers].sort((a, b) => a[1].due - b[1].due).find(([, timer]) => timer.due <= until);
        if (!pending) break;
        if (++iterations > 2000) throw new Error('Runaway fake clock.');
        time = pending[1].due; timers.delete(pending[0]); pending[1].callback(); await settle();
      }
      time = until; await settle();
    }
  };
}
const snapshot = (checkpoint = 'A', participantRef = 'alice', more = false) => ({ participantRef, checkpoint,
  events: [], channels: [], hasMore: more, nextCursor: more ? checkpoint : null, resetRequired: false,
  channelsTruncated: false, nextChannelCursor: null, channelsHasMore: false, channelsIncomplete: false });
const json = value => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
const frame = (value, event = 'activity', id = value.checkpoint) => `id: ${id}\nevent: ${event}\ndata: ${JSON.stringify(value)}\n\n`;
function sse() {
  let controller, cancelled = 0;
  const body = new ReadableStream({ start(value) { controller = value; }, cancel() { cancelled++; } });
  return { response: new Response(body, { headers: { 'Content-Type': 'text/event-stream' } }),
    send(text) { controller.enqueue(typeof text === 'string' ? encoder.encode(text) : text); },
    end() { controller.close(); }, get cancelled() { return cancelled; } };
}
function fixture(options = {}) {
  const time = clock(), requests = [], delivered = [], identities = [], states = [];
  let activity;
  const window = {};
  vm.runInNewContext(source, { window, TextEncoder, TextDecoder, AbortController, ReadableStream });
  const fetch = (url, init) => {
    assert.equal(requests.filter(request => !request.init.signal.aborted).length, 0, 'Only one non-aborted request at a time.');
    const request = { url, init, at: time.now() }; requests.push(request);
    return options.respond?.(request, requests.length - 1, { activity, time }) ?? deferred().promise;
  };
  activity = window.TangentActivity.create({ fetch, timers: time, now: time.now, random: () => 0.5,
    onSnapshot(value, context) { delivered.push({ value, context }); return options.onSnapshot?.(value, context); },
    onEvent: options.onEvent, streamQuery: options.streamQuery,
    onIdentity(detail) { identities.push(detail); options.onIdentity?.(detail, activity); },
    onState(value) { states.push(value); options.onState?.(value, activity); },
    supportsStreaming: options.supportsStreaming ?? true, timings: options.timings });
  return { activity, time, requests, delivered, identities, states,
    async start(participant = 'alice') { activity.start({ participant }); await settle(); },
    async stop() { activity.stop(); await settle(); assert.equal(time.pending, 0, 'All transport timers removed.'); } };
}

test('bootstrap commits only accepted payload, opens SSE from its opaque checkpoint, ignores SSE id alone', async () => {
  const live = sse();
  const f = fixture({ respond: (_, index) => index === 0 ? json(snapshot()) : live.response });
  await f.start();
  assert.equal(f.requests[0].url, '/api/activity');
  assert.equal(f.requests[1].url, '/api/activity/events?cursor=A');
  live.send('id: untrusted-id\n\n'); await settle(); assert.equal(f.activity.snapshot().checkpoint, 'A');
  live.send(frame(snapshot('B'), 'activity', 'different-event-id')); await settle();
  assert.equal(f.activity.snapshot().checkpoint, 'B');
  assert.equal(f.activity.snapshot().status, 'live');
  assert.equal(f.delivered.length, 2);
  await f.stop(); assert.equal(live.cancelled, 1); assert.equal(live.response.body.locked, false);
});

test('the shared stream appends bounded profile subscriptions and emits profile decoration without moving activity cursor', async () => {
  const live = sse(), profiles = [];
  const f = fixture({ streamQuery: () => 'profileDid=did%3Aplc%3Aone', onEvent: (name, value) => profiles.push({ name, value }),
    respond: (_, index) => index === 0 ? json(snapshot()) : live.response });
  await f.start();
  assert.equal(f.requests[1].url, '/api/activity/events?cursor=A&profileDid=did%3Aplc%3Aone');
  live.send('event: profile\ndata: {"did":"did:plc:one","status":"loaded"}\n\n'); await settle();
  assert.equal(profiles.length, 1); assert.equal(profiles[0].name, 'profile');
  assert.equal(profiles[0].value.did, 'did:plc:one'); assert.equal(profiles[0].value.status, 'loaded');
  assert.equal(f.activity.snapshot().checkpoint, 'A');
  f.activity.reconnect(); await settle();
  assert.equal(live.cancelled, 1);
  assert.equal(f.requests[2].url, '/api/activity?cursor=A');
  await f.stop();
});

test('SSE parser handles CRLF split at every byte, multiline data, reset and Unicode', async () => {
  const live = sse(); const f = fixture({ respond: (_, i) => i === 0 ? json(snapshot()) : live.response });
  await f.start();
  const value = snapshot('opaque_café_日本語'); value.resetRequired = true;
  const payload = JSON.stringify(value).replace(',"checkpoint"', ',\r\ndata: "checkpoint"');
  const bytes = encoder.encode('event: reset\r\ndata: ' + payload + '\r\n\r\n');
  for (const byte of bytes) { live.send(Uint8Array.of(byte)); await settle(); }
  assert.equal(f.activity.snapshot().checkpoint, value.checkpoint);
  assert.equal(f.delivered.at(-1).value.resetRequired, true);
  await f.stop();
});

test('malformed frame does not commit its id or dispatch subsequent buffered frames; fallback resumes last accepted cursor', async () => {
  const live = sse(); const f = fixture({ respond: (_, i) => i === 0 ? json(snapshot()) : i === 1 ? live.response : undefined });
  await f.start(); live.send('id: lost\nevent: activity\ndata: {bad}\n\n' + frame(snapshot('also-lost'))); await settle();
  assert.equal(f.activity.snapshot().checkpoint, 'A'); assert.equal(f.delivered.length, 1);
  await f.time.advance(1000);
  assert.equal(f.requests[2].url, '/api/activity/wait?cursor=A');
  assert.equal(f.activity.snapshot().mode, 'poll'); assert.equal(live.cancelled, 1);
  await f.stop();
});

test('bounded frame and JSON parsing reject oversized input before snapshot delivery', async () => {
  for (const sourceKind of ['frame', 'json', 'length']) {
    const live = sse();
    const f = fixture({ respond: (_, i) => {
      if (sourceKind === 'json') return new Response(' '.repeat(512 * 1024 + 1));
      if (sourceKind === 'length') return new Response('{}', { headers: { 'Content-Length': '524289' } });
      return i === 0 ? json(snapshot()) : live.response;
    } });
    await f.start(); if (sourceKind === 'frame') { live.send('data: ' + 'x'.repeat(512 * 1024)); await settle(); }
    assert.equal(f.activity.snapshot().checkpoint, sourceKind === 'frame' ? 'A' : null);
    assert.equal(f.delivered.length, sourceKind === 'frame' ? 1 : 0);
    assert.match(f.activity.snapshot().reason, /too-large/); await f.stop();
  }
});

test('invalid event/channel collections and non-progressing hasMore cannot advance checkpoint', async () => {
  const bad = [
    { ...snapshot('B'), channels: [{ roomKey: 'secret', unreadCount: -1 }] },
    { ...snapshot('B'), events: Array.from({ length: 26 }, () => ({})) },
    { ...snapshot('B'), hasMore: true, nextCursor: 'other' },
    snapshot('A', 'alice', true)
  ];
  for (const value of bad) {
    const live = sse(), f = fixture({ respond: (_, i) => i === 0 ? json(snapshot()) : live.response });
    await f.start(); live.send(frame(value)); await settle();
    assert.equal(f.activity.snapshot().checkpoint, 'A'); assert.equal(f.delivered.length, 1); await f.stop();
  }
});

test('callback rejection, throw and timeout leave cursor uncommitted and invalidate delivery context', async () => {
  for (const behavior of ['false', 'throw', 'timeout']) {
    const f = fixture({ respond: () => json(snapshot('B')), timings: { callbackMs: 5 }, onSnapshot() {
      if (behavior === 'false') return false;
      if (behavior === 'throw') throw new Error('consumer-rejected');
      return deferred().promise;
    } });
    await f.start(); if (behavior === 'timeout') await f.time.advance(5);
    assert.equal(f.activity.snapshot().checkpoint, null);
    assert.equal(f.delivered[0].context.isCurrent(), false);
    assert.equal(f.activity.snapshot().status, 'reconnecting'); await f.stop();
  }
});

test('callback timeout aborts its dependent fetch before the next callback can publish UI', async () => {
  const jobs = [], ui = [];
  const abortableFetch = (_, { signal }) => new Promise((resolve, reject) => {
    const job = { signal, aborted: false }; jobs.push(job);
    signal.addEventListener('abort', () => { job.aborted = true; reject(new Error('dependent-fetch-aborted')); }, { once: true });
  });
  const f = fixture({ supportsStreaming: false, timings: { callbackMs: 5, retryMinMs: 2 },
    respond: (_, i) => i < 2 ? json(snapshot(i === 0 ? 'A' : 'B')) : undefined,
    async onSnapshot(value, context) {
      if (value.checkpoint === 'A') {
        try { await abortableFetch('/history', { signal: context.signal }); } catch (_) {}
      }
      if (context.isCurrent()) ui.push(value.checkpoint);
    } });
  await f.start(); await f.time.advance(5);
  assert.equal(jobs[0].aborted, true); assert.equal(jobs[0].signal.aborted, true);
  assert.equal(f.delivered[0].context.isCurrent(), false); assert.deepEqual(ui, []);
  await f.time.advance(2); assert.deepEqual(ui, ['B']); assert.equal(f.activity.snapshot().checkpoint, 'B');
  assert.equal(f.delivered[1].context.signal.aborted, true, 'Accepted JSON delivery work must finish before its request is released.');
  await f.stop();
});

test('stop/new identity fences stale async callback completions and late fetch responses', async () => {
  const callback = deferred(), oldFetch = deferred();
  const f = fixture({ respond: (_, i) => i === 0 ? json(snapshot('old')) : i === 1 ? oldFetch.promise : json(snapshot('new', 'bob')),
    onSnapshot(value) { if (value.checkpoint === 'old') return callback.promise; }, supportsStreaming: false });
  await f.start(); assert.equal(f.activity.snapshot().checkpoint, null);
  f.activity.stop(); f.activity.start({ participant: 'bob' }); await settle();
  callback.resolve(); await settle();
  assert.equal(f.activity.snapshot().checkpoint, null); assert.equal(f.delivered[0].context.isCurrent(), false);
  f.activity.stop(); f.activity.start({ participant: 'bob' }); await settle();
  oldFetch.resolve(json(snapshot('stale', 'alice'))); await settle();
  assert.equal(f.activity.snapshot().participant, 'bob'); assert.equal(f.activity.snapshot().checkpoint, 'new');
  assert.equal(f.identities.length, 0); await f.stop();
});

test('hidden cancels runtime, visible bootstraps with retained cursor, participant change clears it', async () => {
  const f = fixture({ supportsStreaming: false, respond: (req) => req.url.startsWith('/api/activity/wait') ? undefined : json(snapshot('A', req.url.includes('cursor') ? 'alice' : f.activity.snapshot().participant)) });
  await f.start(); assert.equal(f.requests.length, 2);
  f.activity.setVisible(false); await settle();
  assert.equal(f.activity.snapshot().status, 'paused'); assert.equal(f.activity.snapshot().checkpoint, 'A');
  assert.equal(f.requests[1].init.signal.aborted, true); assert.equal(f.time.pending, 0);
  f.activity.setVisible(true); await settle(); assert.equal(f.requests[2].url, '/api/activity?cursor=A');
  f.activity.start({ participant: 'bob' }); await settle();
  assert.equal(f.requests[4].url, '/api/activity'); assert.equal(f.activity.snapshot().participant, 'bob'); await f.stop();
});

test('same participant start is idempotent and old finally cannot clear current state on synchronous identity restart', async () => {
  const live = sse();
  const f = fixture({ respond: (_, i) => i === 0 ? json(snapshot()) : i === 1 ? live.response : i === 2 ? json(snapshot('bob-cursor', 'bob')) : undefined,
    onIdentity(_, activity) { activity.start({ participant: 'bob' }); } });
  await f.start(); await f.start(); assert.equal(f.requests.length, 2);
  live.send('event: identity_changed\ndata: {"bestLabel":"Bob"}\n\n' + frame(snapshot('evil'))); await settle();
  assert.equal(f.identities.length, 1); assert.equal(f.activity.snapshot().participant, 'bob');
  assert.equal(f.activity.snapshot().checkpoint, 'bob-cursor');
  assert.deepEqual(f.delivered.map(row => row.value.checkpoint), ['A', 'bob-cursor']);
  assert.equal(f.requests.at(-1).url, '/api/activity/events?cursor=bob-cursor'); await f.stop();
});

test('401/403 at snapshot, events and wait stop the identity generation without retries', async () => {
  for (const mode of ['snapshot', 'sse', 'poll']) for (const status of [401, 403]) {
    const f = fixture({ supportsStreaming: mode !== 'poll', respond: (_, i) => mode === 'snapshot' || i > 0 ? new Response(null, { status }) : json(snapshot()) });
    await f.start(); const count = f.requests.length; await f.time.advance(120000);
    assert.equal(f.identities.length, 1); assert.equal(f.identities[0].status, status);
    assert.equal(f.activity.snapshot().checkpoint, null); assert.equal(f.activity.snapshot().status, 'identity');
    assert.equal(f.requests.length, count); assert.equal(f.time.pending, 0); await f.stop();
  }
});

test('participantRef missing fails closed; mismatched actor reloads without delivering any snapshot', async () => {
  const missing = snapshot(); delete missing.participantRef;
  const first = fixture({ respond: () => json(missing) }); await first.start();
  assert.equal(first.delivered.length, 0); assert.equal(first.activity.snapshot().checkpoint, null); await first.stop();
  const second = fixture({ respond: () => json(snapshot('B', 'bob')) }); await second.start();
  assert.equal(second.delivered.length, 0); assert.equal(second.activity.snapshot().checkpoint, null);
  assert.equal(second.identities[0].reason, 'participant-mismatch'); assert.equal(second.identities[0].participantRef, 'bob');
  assert.equal(second.time.pending, 0); await second.stop();
});

test('invalid cursor recovers without cursor only once, then stops rather than retrying 400 forever', async () => {
  const f = fixture({ respond: (_, i) => i % 2 === 0 ? json(snapshot(i === 0 ? 'A' : 'B')) : new Response(null, { status: 400 }) });
  await f.start(); assert.equal(f.requests.length, 4);
  assert.equal(f.requests[2].url, '/api/activity'); assert.equal(f.activity.snapshot().status, 'error');
  await f.time.advance(120000); assert.equal(f.requests.length, 4); assert.equal(f.identities.length, 0); await f.stop();
});

test('no ReadableStream uses participant wait; hasMore follows checkpoint independently of channel pagination', async () => {
  const value = snapshot('B', 'alice', true); value.nextChannelCursor = 'channel-other'; value.channelsHasMore = true;
  const f = fixture({ supportsStreaming: false, respond: (_, i) => i === 0 ? json(snapshot()) : i === 1 ? json(value) : undefined });
  await f.start(); assert.equal(f.activity.snapshot().checkpoint, 'B'); assert.equal(f.activity.snapshot().mode, 'poll');
  assert.equal(f.activity.snapshot().status, 'live'); await f.time.advance(1);
  assert.equal(f.requests[2].url, '/api/activity/wait?cursor=B');
  assert.ok(f.requests.every(request => !request.url.includes('/events') && !request.url.includes('channelCursor'))); await f.stop();
});

test('legacy buffered JSON fallback still validates bytes and identity before parsing/delivery', async () => {
  const f = fixture({ supportsStreaming: false, respond: (_, i) => i === 0 ? { ok: true, status: 200, headers: { get: () => null }, text: async () => JSON.stringify(snapshot()) } : undefined });
  await f.start(); assert.equal(f.activity.snapshot().checkpoint, 'A'); assert.equal(f.requests[1].url, '/api/activity/wait?cursor=A'); await f.stop();
});

test('idle fallback permits the real 15-second server wait before headers, not a 10-second connect deadline', async () => {
  const delayed = deferred();
  const f = fixture({ supportsStreaming: false, respond: (_, i) => i === 0 ? json(snapshot()) : delayed.promise });
  await f.start(); await f.time.advance(14999);
  assert.equal(f.requests.length, 2); assert.equal(f.requests[1].init.signal.aborted, false);
  delayed.resolve(json(snapshot())); await settle();
  assert.equal(f.activity.snapshot().status, 'live'); assert.equal(f.activity.snapshot().mode, 'poll'); await f.stop();
});

test('SSE failure enters fallback with bounded jitter/backoff and reprobe aborts poll before new SSE', async () => {
  const live = sse();
  const f = fixture({ timings: { reprobeMs: 50, retryMinMs: 2, retryMaxMs: 8, pollTimeoutMs: 100 },
    respond: (_, i) => i === 0 ? json(snapshot()) : i === 1 ? new Response(null, { status: 503 }) : i === 3 ? live.response : undefined });
  await f.start(); assert.equal(f.activity.snapshot().retryInMs, 2); await f.time.advance(2);
  assert.equal(f.requests[2].url, '/api/activity/wait?cursor=A'); await f.time.advance(48);
  assert.equal(f.requests[2].init.signal.aborted, true); assert.equal(f.requests[3].url, '/api/activity/events?cursor=A'); await f.stop();
});

test('network backoff caps without spinning and header/body watchdogs abort stuck requests', async () => {
  const f = fixture({ supportsStreaming: false, timings: { retryMinMs: 2, retryMaxMs: 8 }, respond: () => Promise.reject(new Error('offline')) });
  await f.start(); assert.equal(f.activity.snapshot().retryInMs, 2);
  await f.time.advance(2); assert.equal(f.activity.snapshot().retryInMs, 4);
  await f.time.advance(4); assert.equal(f.activity.snapshot().retryInMs, 8);
  await f.time.advance(8); assert.equal(f.activity.snapshot().retryInMs, 8); await f.stop();
  const pending = fixture({ timings: { connectMs: 5 } }); await pending.start(); await pending.time.advance(5);
  assert.equal(pending.requests[0].init.signal.aborted, true); assert.equal(pending.activity.snapshot().reason, 'connect-timeout'); await pending.stop();
  const body = sse(); const stalled = fixture({ timings: { pollTimeoutMs: 5 }, respond: () => body.response });
  await stalled.start(); await stalled.time.advance(5); assert.equal(stalled.activity.snapshot().reason, 'body-timeout'); await stalled.stop();
});

test('heartbeat activity extends stall watchdog but bounded SSE lifetime rotates through a fresh snapshot', async () => {
  const live = sse(); const f = fixture({ timings: { watchdogMs: 30, streamLifetimeMs: 70 },
    respond: (_, i) => i === 0 ? json(snapshot()) : i === 1 ? live.response : i === 2 ? json(snapshot('B')) : undefined });
  await f.start(); live.send(frame(snapshot())); await settle();
  for (let step = 0; step < 3; step++) { await f.time.advance(20); live.send(': keepalive\n\n'); await settle(); }
  assert.equal(f.requests[1].init.signal.aborted, false);
  await f.time.advance(10); assert.equal(live.cancelled, 1);
  assert.equal(f.requests[2].url, '/api/activity?cursor=A'); assert.equal(f.requests[3].url, '/api/activity/events?cursor=B');
  await f.stop();
});

test('stalled SSE falls back even if headers succeeded, and malformed UTF-8 cannot commit', async () => {
  for (const corrupt of [false, true]) {
    const live = sse(); const f = fixture({ timings: { watchdogMs: 10 }, respond: (_, i) => i === 0 ? json(snapshot()) : i === 1 ? live.response : undefined });
    await f.start(); if (corrupt) { live.send(Uint8Array.of(255)); await settle(); } else await f.time.advance(10);
    assert.equal(f.activity.snapshot().checkpoint, 'A'); assert.equal(f.activity.snapshot().mode, 'poll');
    assert.equal(live.cancelled, 1); await f.stop();
  }
});
