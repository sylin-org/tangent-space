import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const transportSource = await readFile(new URL('../src/server/web/wwwroot/activity-transport.js', import.meta.url), 'utf8');
const roomsSource = await readFile(new URL('../src/server/web/wwwroot/rooms.js', import.meta.url), 'utf8');
const settle = async () => { for (let i = 0; i < 6; i++) await new Promise(resolve => setImmediate(resolve)); };
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; };
const packet = (checkpoint, participantRef = 'alice', extra = {}) => ({ participantRef, checkpoint,
  events: [], channels: [], hasMore: false, nextCursor: null, resetRequired: false,
  channelsTruncated: false, channelsHasMore: false, channelsIncomplete: false, nextChannelCursor: null, ...extra });
const json = value => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
const frame = value => `event: activity\nid: ${value.checkpoint}\ndata: ${JSON.stringify(value)}\n\n`;

function fixture(options = {}) {
  let time = 0, nextTimer = 0;
  const timers = new Map(), requests = [], applied = [], identities = [], states = [];
  const timerApi = { setTimeout(fn, ms) { const id = ++nextTimer; timers.set(id, { fn, due: time + ms }); return id; },
    clearTimeout(id) { timers.delete(id); } };
  const window = {};
  vm.runInNewContext(transportSource, { window, AbortController, TextEncoder, TextDecoder, ReadableStream });
  const controller = window.TangentActivity.create({ timers: timerApi, now: () => time, random: () => .5,
    ...options,
    fetch(path, init) { const waiting = deferred(); requests.push({ path, init, respond: waiting.resolve }); return waiting.promise; },
    onSnapshot(value, context) { applied.push({ value, context }); return options.onSnapshot?.(value, context); },
    onIdentity(detail) { identities.push(detail); options.onIdentity?.(detail); },
    onState(state) { states.push(state); options.onState?.(state); }
  });
  return { controller, requests, applied, identities, states, timers,
    async advance(ms) {
      const end = time + ms; let executions = 0;
      while (true) {
        const next = [...timers].filter(([, timer]) => timer.due <= end).sort((a, b) => a[1].due - b[1].due)[0];
        if (!next) break;
        assert.ok(++executions < 1000, 'timer loop remains bounded');
        time = next[1].due; timers.delete(next[0]); next[1].fn(); await settle();
      }
      time = end; await settle();
    },
    async start(participant = 'alice') { controller.start({ participant }); await settle(); },
    async answer(value, index = requests.length - 1) { requests[index].respond(value instanceof Response ? value : json(value)); await settle(); },
    async stream(index = requests.length - 1) {
      let streamController;
      const response = new Response(new ReadableStream({ start(value) { streamController = value; } }),
        { headers: { 'Content-Type': 'text/event-stream' } });
      requests[index].respond(response); await settle();
      return { async push(text) { streamController.enqueue(new TextEncoder().encode(text)); await settle(); },
        async close() { streamController.close(); await settle(); } };
    }
  };
}

test('red team: quiet long-poll headers may take 15 seconds without hitting SSE connect timeout', async t => {
  const f = fixture({ supportsStreaming: false }); t.after(() => f.controller.stop());
  await f.start(); await f.answer(packet('c0'));
  const waiting = f.requests.at(-1); assert.match(waiting.path, /\/wait\?cursor=c0$/);
  await f.advance(10001);
  assert.equal(waiting.init.signal.aborted, false, 'the server deliberately waits 15 seconds before headers');
  await f.advance(4999); await f.answer(packet('c0'));
  assert.equal(f.controller.snapshot().checkpoint, 'c0');
  assert.equal(f.controller.snapshot().status, 'live');
});

test('red team: malformed frame and forged SSE id cannot poison the fallback cursor', async t => {
  const f = fixture(); t.after(() => f.controller.stop());
  await f.start(); await f.answer(packet('safe'));
  const stream = await f.stream();
  await stream.push('id: attacker-checkpoint\nevent: activity\ndata: {broken\n\n');
  assert.equal(f.controller.snapshot().checkpoint, 'safe');
  assert.equal(f.applied.length, 1);
  await f.advance(1000);
  assert.match(f.requests.at(-1).path, /\/wait\?cursor=safe$/);
});

test('red team: CRLF split at chunks, multiline data and unknown events preserve checkpoint rules', async t => {
  const f = fixture(); t.after(() => f.controller.stop());
  await f.start(); await f.answer(packet('c0'));
  const stream = await f.stream();
  await stream.push('id: not-accepted\revent: unknown\rdata: {}\r\r');
  assert.equal(f.controller.snapshot().checkpoint, 'c0');
  const value = JSON.stringify(packet('c1')); const split = value.indexOf(',') + 1;
  await stream.push('event: activity\r');
  await stream.push('\ndata: ' + value.slice(0, split) + '\r');
  await stream.push('\ndata: ' + value.slice(split) + '\r\n\r');
  await stream.push('\n');
  assert.equal(f.controller.snapshot().checkpoint, 'c1');
  assert.deepEqual(f.applied.map(row => row.value.checkpoint), ['c0', 'c1']);
});

for (const mode of ['snapshot', 'sse', 'poll']) {
  test(`red team: ${mode} actor mismatch stops before private payload or cursor is applied`, async t => {
    const f = fixture({ supportsStreaming: mode !== 'poll' }); t.after(() => f.controller.stop());
    await f.start();
    if (mode !== 'snapshot') await f.answer(packet('alice-cursor'));
    const before = f.applied.length;
    if (mode === 'sse') { const stream = await f.stream(); await stream.push(frame(packet('bob-cursor', 'bob'))); }
    else await f.answer(packet('bob-cursor', 'bob'));
    assert.equal(f.applied.length, before, 'foreign payload never reaches the room renderer');
    assert.equal(f.identities.length, 1);
    assert.equal(f.controller.snapshot().checkpoint, null);
    assert.equal(f.controller.snapshot().desired, false);
    assert.equal(f.controller.snapshot().requestActive, false);
  });
}

test('red team: 403 fallback is terminal and late requests cannot revive the old actor', async t => {
  const f = fixture({ supportsStreaming: false }); t.after(() => f.controller.stop());
  await f.start(); await f.answer(packet('alice-cursor'));
  await f.answer(new Response('{}', { status: 403 }));
  assert.equal(f.identities.length, 1); assert.equal(f.identities[0].status, 403);
  assert.equal(f.controller.snapshot().checkpoint, null);
  const count = f.requests.length; await f.advance(120000); assert.equal(f.requests.length, count);
  await f.start('bob'); await f.answer(packet('bob-cursor', 'bob'));
  assert.equal(f.controller.snapshot().participant, 'bob'); assert.equal(f.controller.snapshot().checkpoint, 'bob-cursor');
});

test('red team: late bootstrap completion and finally cannot mutate a replacement generation', async t => {
  const f = fixture({ supportsStreaming: false }); t.after(() => f.controller.stop());
  await f.start(); const old = f.requests[0]; await f.start('bob');
  assert.equal(old.init.signal.aborted, true);
  await f.answer(packet('bob-cursor', 'bob'), 1);
  const generation = f.controller.snapshot().generation;
  await f.answer(packet('alice-secret', 'alice'), 0);
  assert.equal(f.applied.length, 1); assert.equal(f.controller.snapshot().checkpoint, 'bob-cursor');
  assert.equal(f.controller.snapshot().generation, generation);
  assert.equal(f.controller.snapshot().requestActive, true, 'new actor owns the outstanding poll');
});

test('red team: rejected and timed-out consumers cannot commit or later claim a live context', async t => {
  const delivery = deferred(); let oldContext;
  const f = fixture({ supportsStreaming: false, onSnapshot(_value, context) { oldContext = context; return delivery.promise; } });
  t.after(() => f.controller.stop()); await f.start(); await f.answer(packet('not-yet-accepted'));
  assert.equal(f.controller.snapshot().checkpoint, null);
  await f.advance(15000);
  assert.equal(oldContext.isCurrent(), false); assert.equal(f.controller.snapshot().checkpoint, null);
  delivery.resolve(true); await settle();
  assert.equal(f.controller.snapshot().checkpoint, null);
  await f.advance(1000); assert.equal(f.requests.at(-1).path, '/api/activity/wait');
});

test('red team: non-progressing HasMore is rejected without losing the last valid checkpoint', async t => {
  const f = fixture({ supportsStreaming: false }); t.after(() => f.controller.stop());
  await f.start(); await f.answer(packet('c0'));
  await f.answer(packet('c0', 'alice', { hasMore: true, nextCursor: 'c0' }));
  assert.equal(f.applied.length, 1); assert.equal(f.controller.snapshot().checkpoint, 'c0');
  await f.advance(1000); assert.match(f.requests.at(-1).path, /cursor=c0$/);
});

test('red team: oversized JSON headers are refused before decoding or checkpoint commit', async t => {
  const f = fixture({ supportsStreaming: false }); t.after(() => f.controller.stop());
  await f.start();
  await f.answer(new Response(JSON.stringify(packet('oversized')), { headers: { 'Content-Length': '524289' } }));
  assert.equal(f.applied.length, 0); assert.equal(f.controller.snapshot().checkpoint, null);
});

test('red team: hidden page aborts its reader and preserves a verified checkpoint for resume', async t => {
  const f = fixture(); t.after(() => f.controller.stop());
  await f.start(); await f.answer(packet('c0')); const stream = await f.stream(); await stream.push(frame(packet('c1')));
  const oldRequest = f.requests.at(-1); f.controller.setVisible(false); await settle();
  assert.equal(oldRequest.init.signal.aborted, true); assert.equal(f.controller.snapshot().checkpoint, 'c1');
  const count = f.requests.length; await f.advance(120000); assert.equal(f.requests.length, count);
  f.controller.setVisible(true); await settle();
  assert.equal(f.requests.at(-1).path, '/api/activity?cursor=c1');
});

test('red team: keepalives cannot prevent absolute SSE rotation and new-cookie identity validation', async t => {
  const f = fixture(); t.after(() => f.controller.stop());
  await f.start(); await f.answer(packet('c0')); const stream = await f.stream();
  const streaming = f.requests.at(-1);
  await f.advance(30000); await stream.push(': keepalive\n\n');
  await f.advance(29999); assert.equal(streaming.init.signal.aborted, false);
  await f.advance(1);
  assert.equal(streaming.init.signal.aborted, true, 'a live socket still rotates at the absolute lifetime');
  assert.equal(f.requests.at(-1).path, '/api/activity?cursor=c0');
  assert.equal(f.requests.at(-1).init.credentials, 'same-origin');
  await f.answer(packet('new-cookie', 'bob'));
  assert.equal(f.applied.length, 1); assert.equal(f.identities.length, 1);
});

test('red team: fallback re-probe cancels a pending wait and opens exactly one stream at its current cursor', async t => {
  const f = fixture(); t.after(() => f.controller.stop());
  await f.start(); await f.answer(packet('c0'));
  await f.answer(new Response('{}', { status: 502 })); await f.advance(1000);
  await f.answer(packet('c1')); await f.advance(250);
  const waiting = f.requests.at(-1); assert.match(waiting.path, /\/wait\?cursor=c1$/);
  // A normal 45-second wait timeout retries once before the 60-second re-probe deadline.
  await f.advance(58750);
  const reprobe = f.requests.at(-1);
  assert.equal(reprobe.path, '/api/activity/events?cursor=c1');
  assert.equal(waiting.init.signal.aborted, true);
  assert.equal(f.requests.filter(row => row.path.includes('/events')).length, 2);
  assert.equal(f.requests.filter(row => !row.init.signal.aborted).length, 1);
  const stream = await f.stream(); await stream.push(frame(packet('c2')));
  assert.equal(f.controller.snapshot().mode, 'sse'); assert.equal(f.controller.snapshot().checkpoint, 'c2');
});

test('red team: malformed channel row and broken continuation cannot advance an otherwise valid snapshot', async t => {
  const f = fixture({ supportsStreaming: false }); t.after(() => f.controller.stop());
  await f.start(); await f.answer(packet('c0'));
  await f.answer(packet('bad', 'alice', { channels: [{ roomKey: 'private' }] }));
  assert.equal(f.applied.length, 1); assert.equal(f.controller.snapshot().checkpoint, 'c0');
  await f.advance(1000); await f.answer(packet('bad-again', 'alice', { hasMore: true, nextCursor: 'unrelated-edge' }));
  assert.equal(f.applied.length, 1); assert.equal(f.controller.snapshot().checkpoint, 'c0');
});

// Independent browser fixture: execute both real scripts through welcome/fetch/DOM seams.
// It models no application recovery, cursor, rendering, or permission decision logic.
function roomFixture() {
  class Element {
    constructor() {
      this.children = []; this.hidden = true; this.textContent = ''; this.value = ''; this.dataset = {}; this.listeners = new Map();
      this.classList = { add() {}, remove() {}, toggle() {} }; this.style = { setProperty() {} };
      const fields = new Map(); this.elements = { namedItem(name) { if (!fields.has(name)) fields.set(name, new Element()); return fields.get(name); } };
    }
    addEventListener(name, fn) { this.listeners.set(name, [...(this.listeners.get(name) || []), fn]); }
    emit(name, value = {}) { for (const fn of this.listeners.get(name) || []) fn({ preventDefault() {}, ...value }); }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.children = children; }
    setAttribute(name, value) { this[name] = value; }
    querySelectorAll() { return []; }
    querySelector() { return null; }
    contains() { return false; }
    click() { this.emit('click'); }
    focus() {}
  }
  const elements = new Map(), get = id => { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); };
  const window = new Element(), document = new Element(), requests = [], timers = new Map(); let timerId = 0, controller;
  const storage = new Map(); window.sessionStorage = { get length() { return storage.size; }, key: i => [...storage.keys()][i],
    getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, String(value)), removeItem: key => storage.delete(key) };
  Object.assign(document, { body: new Element(), hidden: false, getElementById: get, createElement: () => new Element() });
  const topic = { key: 'lounge', title: 'Readable muted topic', tangentKey: 'home', canRead: true, canWrite: true };
  const tangent = { key: 'home', name: 'Home', channels: [topic] };
  const route = { kind: 'topic', tangent: 'home', topic: 'lounge' };
  window.TangentPages = { route, tangentUrl: key => '/t/' + key + '/topics', topicUrl: (key, room) => '/t/' + key + '/topics/' + room,
    hero() {}, unavailable() {}, prepare() {} };
  const location = { href: 'http://127.0.0.1/t/home/topics/lounge', assign(path) { this.href = path; }, replace(path) { this.href = path; } };
  const f = { requests, get, document, window, route, failHistory: false, activity: () => controller,
    welcome(participantRef = 'alice') { window.emit('tangent:welcome', { detail: { participant: { participantRef }, tangents: { tangents: [tangent] } } }); },
    async answer(value) { const request = requests.findLast(row => row.respond && !row.answered); assert.ok(request, 'activity request exists');
      request.answered = true; request.respond(value instanceof Response ? value : json(value)); await settle(); },
    async stream() { let sender; await this.answer(new Response(new ReadableStream({ start(value) { sender = value; } }),
      { headers: { 'Content-Type': 'text/event-stream' } })); return { async push(value) { sender.enqueue(new TextEncoder().encode(value)); await settle(); } }; }
  };
  const fetch = async (path, init = {}) => {
    const request = { path, init }; requests.push(request);
    if (path.startsWith('/api/activity')) { const pending = deferred(); request.respond = pending.resolve; return pending.promise; }
    const clean = path.split('?')[0]; let value = {};
    if (clean === '/api/v1/tangents/absent') return new Response('{}', { status: 404 });
    if (clean === '/api/v1/tangents') {
      if (f.holdDirectory) { const held = f.holdDirectory; f.holdDirectory = null; return held.promise; }
      if (f.failDirectory) return new Response('{}', { status: 503 });
      value = { tangents: [tangent] };
    }
    else if (clean === '/api/server' && f.failSettings) return new Response('{}', { status: 503 });
    else if (clean === '/api/v1/tangents/home') value = tangent;
    else if (clean === '/api/v1/tangents/home/topics') value = { channels: [topic] };
    else if (clean === '/api/v1/tangents/home/topics/lounge' || clean === '/api/rooms/lounge') value = topic;
    else if (clean === '/api/rooms/lounge/messages') {
      if (f.holdHistory) { const held = f.holdHistory; f.holdHistory = null; return held.promise; }
      if (f.failHistory) return new Response('{}', { status: 503 });
      value = { messages: [], boundary: 0, resumeCursor: 'history-c0', nextCursor: null };
    }
    return json(value);
  };
  class FormData { *[Symbol.iterator]() {} }
  const setTimeout = (fn, ms) => { const id = ++timerId; timers.set(id, { fn, ms }); return id; }, clearTimeout = id => timers.delete(id);
  Object.assign(window, { fetch, setTimeout, clearTimeout });
  const context = vm.createContext({ window, document, fetch, location, URL, TextEncoder, TextDecoder, ReadableStream, AbortController, FormData,
    setTimeout, clearTimeout, crypto: { randomUUID: () => 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' },
    history: { replaceState(_state, _title, path) { location.href = new URL(path, location.href).href; } } });
  vm.runInContext(transportSource, context);
  const create = window.TangentActivity.create;
  window.TangentActivity = { create(options) { controller = create(options); return controller; } };
  vm.runInContext(roomsSource, context);
  return f;
}

const loungeChannel = { roomKey: 'lounge', tangentKey: 'home', unreadCount: 0, unreadCountCapped: false,
  directReplies: 0, lastSequence: 0, readSequence: 0, lastMessageAt: null };

test('red team integration: a readable muted topic survives a complete activity overview that omits it', async t => {
  const f = roomFixture(); t.after(() => f.activity()?.stop()); f.welcome(); await settle();
  assert.equal(f.get('room-content').hidden, false);
  const before = f.requests.filter(row => row.path === '/api/v1/tangents/home/topics/lounge').length;
  await f.answer(packet('c0'));
  assert.equal(f.get('room-content').hidden, false);
  assert.equal(f.get('room-title').textContent, 'Readable muted topic');
  assert.ok(f.requests.filter(row => row.path === '/api/v1/tangents/home/topics/lounge').length > before, 'rechecks authoritative topic detail');
  assert.equal(f.activity().snapshot().checkpoint, 'c0');
  assert.equal(f.requests.some(row => row.path.includes('/updates?')), false, 'no parallel legacy room wait loop');
});

test('red team integration: actor mismatch quarantines room state before welcome finishes', async t => {
  const f = roomFixture(); t.after(() => f.activity()?.stop()); let welcomeRetries = 0;
  f.get('retry').addEventListener('click', () => welcomeRetries++); f.welcome(); await settle();
  assert.equal(f.get('room-content').hidden, false);
  await f.answer(packet('bob-secret', 'bob'));
  assert.equal(welcomeRetries, 1); assert.equal(f.get('room-content').hidden, true);
  assert.equal(f.get('messages').children.length, 0); assert.equal(f.get('place').hidden, true);
  assert.equal(f.activity().snapshot().checkpoint, null);
  assert.equal(f.requests.some(row => row.init.method === 'POST'), false);
});

test('red team integration: failed edit invalidation must not acknowledge a cursor over stale history', async t => {
  const f = roomFixture(); t.after(() => f.activity()?.stop()); f.welcome(); await settle();
  await f.answer(packet('c0', 'alice', { channels: [loungeChannel] })); const stream = await f.stream();
  f.failHistory = true;
  await stream.push(frame(packet('edit-c1', 'alice', { channels: [loungeChannel], events: [{
    sequence: '1', kind: 'MessageEdited', roomKey: 'lounge', tangentKey: 'home', actorParticipantId: 'bob',
    targetParticipantId: null, messageSequence: 1, occurredAt: '2026-09-12T12:00:00Z'
  }] })));
  assert.equal(f.activity().snapshot().checkpoint, 'c0', 'failed invalidation stays replayable');
});

test('red team integration: expired same-actor delivery cannot render its delayed history response', async t => {
  const f = roomFixture(); t.after(() => f.activity()?.stop()); f.welcome(); await settle();
  await f.answer(packet('c0', 'alice', { channels: [loungeChannel] })); const stream = await f.stream();
  const held = deferred(); f.holdHistory = held;
  await stream.push(frame(packet('edit-c1', 'alice', { channels: [loungeChannel], events: [{
    sequence: '1', kind: 'MessageEdited', roomKey: 'lounge', tangentKey: 'home', actorParticipantId: 'bob',
    targetParticipantId: null, messageSequence: 1, occurredAt: '2026-09-12T12:00:00Z'
  }] })));
  const historyRequest = f.requests.at(-1); assert.match(historyRequest.path, /\/messages\?/);
  f.document.hidden = true; f.document.emit('visibilitychange'); await settle();
  held.resolve(json({ messages: [{ id: 'expired-delivery', authorParticipantId: 'bob', sequence: 1,
    acceptedAt: '2026-09-12T12:00:00Z', content: { text: 'Stale response must not render' } }],
    boundary: 1, resumeCursor: 'old-history', nextCursor: null }));
  await settle();
  assert.equal(f.get('messages').children.length, 0, 'expired callback cannot mutate the retained room DOM');
  assert.equal(historyRequest.init.signal?.aborted, true, 'dependent history fetch shares activity delivery cancellation');
  assert.equal(f.activity().snapshot().checkpoint, 'c0');
});

for (const subject of ['Directory', 'Settings']) {
  test(`red team integration: failed ${subject.toLowerCase()} invalidation must remain replayable`, async t => {
    const f = roomFixture(); t.after(() => f.activity()?.stop()); f.welcome(); await settle();
    await f.answer(packet('c0', 'alice', { channels: [loungeChannel] })); const stream = await f.stream();
    f['fail' + subject] = true;
    await stream.push(frame(packet('changed-c1', 'alice', { channels: [loungeChannel], events: [{
      sequence: '1', kind: subject === 'Settings' ? 'ParticipantChanged' : 'TangentChanged', roomKey: '', tangentKey: 'home',
      actorParticipantId: 'alice', targetParticipantId: null, messageSequence: null, occurredAt: '2026-09-12T12:00:00Z'
    }] })));
    assert.equal(f.activity().snapshot().checkpoint, 'c0', 'failed invalidation stays replayable');
  });
}

test('red team integration: expired directory callback cannot replace the visible Tangent list', async t => {
  const f = roomFixture(); t.after(() => f.activity()?.stop()); f.welcome(); await settle();
  await f.answer(packet('c0', 'alice', { channels: [loungeChannel] })); const stream = await f.stream();
  const held = deferred(); f.holdDirectory = held;
  await stream.push(frame(packet('changed-c1', 'alice', { channels: [loungeChannel], events: [{
    sequence: '1', kind: 'TangentChanged', roomKey: '', tangentKey: 'home', actorParticipantId: 'alice',
    targetParticipantId: null, messageSequence: null, occurredAt: '2026-09-12T12:00:00Z'
  }] })));
  const directoryRequest = f.requests.at(-1); assert.equal(directoryRequest.path, '/api/v1/tangents');
  const before = f.get('tangent-list').children;
  f.document.hidden = true; f.document.emit('visibilitychange'); await settle();
  held.resolve(json({ tangents: [{ key: 'stale', name: 'Expired directory', channels: [] }] })); await settle();
  assert.equal(f.get('tangent-list').children, before, 'expired callback does not replace DOM nodes');
  assert.equal(directoryRequest.init.signal?.aborted, true);
  assert.equal(f.activity().snapshot().checkpoint, 'c0');
});

test('red team integration: reset without replayable events revalidates the directory before committing', async t => {
  const f = roomFixture(); t.after(() => f.activity()?.stop()); f.welcome(); await settle();
  await f.answer(packet('c0', 'alice', { channels: [loungeChannel] })); const stream = await f.stream();
  const before = f.requests.filter(row => row.path === '/api/v1/tangents').length;
  f.failDirectory = true;
  await stream.push(frame(packet('reset-c1', 'alice', { channels: [loungeChannel], resetRequired: true, events: [] })));
  assert.ok(f.requests.filter(row => row.path === '/api/v1/tangents').length > before, 'reset cannot rely on lost governance events');
  assert.equal(f.activity().snapshot().checkpoint, 'c0', 'failed reset invalidation remains replayable');
});

test('red team integration: missing tangent route does not prevent other participant activity on reset', async t => {
  const f = roomFixture(); t.after(() => f.activity()?.stop()); f.route.tangent = 'absent'; f.welcome(); await settle();
  assert.equal(f.get('room-content').hidden, true);
  const before = f.requests.filter(row => row.path === '/api/v1/tangents/absent').length; assert.equal(before, 1);
  await f.answer(packet('c0')); const stream = await f.stream();
  await stream.push(frame(packet('reset-c1', 'alice', { resetRequired: true })));
  assert.equal(f.activity().snapshot().checkpoint, 'reset-c1', 'valid global reset is accepted despite missing local route');
  assert.equal(f.requests.filter(row => row.path === '/api/v1/tangents/absent').length, before, 'does not repeatedly try to re-enter the failed route');
  assert.equal(f.get('room-content').hidden, true, 'reset is not automatic route navigation');
});
