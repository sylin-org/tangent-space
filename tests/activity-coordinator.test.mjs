import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const workerSource = await readFile(new URL('../src/server/web/wwwroot/activity-shared-worker.js', import.meta.url), 'utf8');
const clientSource = await readFile(new URL('../src/server/web/wwwroot/activity-coordinator.js', import.meta.url), 'utf8');
const settle = async () => { for (let turn = 0; turn < 12; turn++) await Promise.resolve(); };
const snapshot = checkpoint => ({ participantRef: 'alice', checkpoint, events: [], channels: [], hasMore: false,
  nextCursor: null, resetRequired: false, nextChannelCursor: null });

class Port {
  onmessage = null; peer = null; closed = false;
  postMessage(data) { if (!this.closed) queueMicrotask(() => this.peer?.onmessage?.({ data: structuredClone(data) })); }
  start() {}
  close() { this.closed = true; }
}
function pair() { const client = new Port(), worker = new Port(); client.peer = worker; worker.peer = client; return { client, worker }; }

function sharedFixture() {
  const transports = [], timers = new Map(), sharedPorts = []; let nextTimer = 0, id = 0;
  const fakeActivity = { create(options) {
    const value = { options, starts: [], visibility: [], stops: 0, reconnects: 0,
      start(input) { this.starts.push(input); }, stop() { this.stops++; },
      setVisible(next) { this.visibility.push(next); }, reconnect() { this.reconnects++; } };
    transports.push(value); return value;
  } };
  const self = {};
  const workerContext = { self, fetch: () => {}, encodeURIComponent, crypto: { randomUUID: () => 'port-' + ++id },
    Date, Map, Set, Promise, structuredClone,
    importScripts() { self.TangentActivity = fakeActivity; },
    setInterval() { return 1; }, clearInterval() {},
    setTimeout(fn) { const timer = ++nextTimer; timers.set(timer, fn); return timer; }, clearTimeout(timer) { timers.delete(timer); } };
  vm.runInNewContext(workerSource, workerContext);
  class SharedWorker {
    constructor() { const ports = pair(); this.port = ports.client; sharedPorts.push(ports.client); self.onconnect({ ports: [ports.worker] }); }
  }
  const clients = [];
  function client(name) {
    const events = [], applied = [], states = [], identities = [];
    const window = { TangentActivity: fakeActivity, dispatchEvent(event) { events.push(event); } };
    const context = { window, document: { hidden: false }, SharedWorker, AbortController, CustomEvent,
      setInterval() { return 1; }, clearInterval() {}, setTimeout() { return 1; }, clearTimeout() {} };
    vm.runInNewContext(clientSource, context);
    let accept = true;
    const controller = window.TangentActivityCoordinator.create({
      async onSnapshot(value, delivery) { applied.push({ value, delivery }); return accept; },
      onState(value) { states.push(value); }, onIdentity(value) { identities.push(value); }
    });
    const result = { name, controller, applied, events, states, identities, setAccept(value) { accept = value; } };
    clients.push(result); return result;
  }
  async function runTimers() { const pending = [...timers.values()]; timers.clear(); for (const fn of pending) fn(); await settle(); }
  return { client, clients, transports, sharedPorts, runTimers };
}

test('two visible tabs share one transport and both must accept before its checkpoint advances', async () => {
  const f = sharedFixture(), first = f.client('first'), second = f.client('second'); await settle();
  first.controller.start({ participant: 'alice' }); second.controller.start({ participant: 'alice' }); await settle();
  assert.equal(f.transports.length, 1); assert.equal(f.transports[0].starts.length, 1); assert.equal(f.transports[0].starts[0].participant, 'alice');
  const signal = new AbortController();
  assert.equal(await f.transports[0].options.onSnapshot(snapshot('c1'), { signal: signal.signal, isCurrent: () => true }), true);
  assert.deepEqual(first.applied.map(row => row.value.checkpoint), ['c1']);
  assert.deepEqual(second.applied.map(row => row.value.checkpoint), ['c1']);
  second.setAccept(false);
  assert.equal(await f.transports[0].options.onSnapshot(snapshot('c2'), { signal: signal.signal, isCurrent: () => true }), false);
});

test('hidden tabs leave the shared connection, then receive one forced reset on return', async () => {
  const f = sharedFixture(), first = f.client('first'), second = f.client('second'); await settle();
  first.controller.start({ participant: 'alice' }); second.controller.start({ participant: 'alice' }); await settle();
  const signal = new AbortController();
  await f.transports[0].options.onSnapshot(snapshot('c1'), { signal: signal.signal, isCurrent: () => true });
  first.controller.setVisible(false); await settle();
  await f.transports[0].options.onSnapshot(snapshot('c2'), { signal: signal.signal, isCurrent: () => true });
  assert.deepEqual(first.applied.map(row => row.value.checkpoint), ['c1']);
  second.controller.setVisible(false); await settle();
  assert.equal(f.transports[0].visibility.at(-1), false);
  first.controller.setVisible(true); await settle();
  assert.equal(first.applied.at(-1).value.checkpoint, 'c2');
  assert.equal(first.applied.at(-1).value.resetRequired, true);
  assert.equal(f.transports.length, 1);
});

test('profile subscriptions are unioned into the shared activity stream and fan out as decoration', async () => {
  const f = sharedFixture(), first = f.client('first'), second = f.client('second'); await settle();
  first.controller.start({ participant: 'alice' }); second.controller.start({ participant: 'alice' }); await settle();
  assert.equal(first.controller.setProfiles(['did:plc:a']), true);
  assert.equal(second.controller.setProfiles(['did:plc:b', 'did:plc:a']), true); await settle(); await f.runTimers();
  assert.match(f.transports[0].options.streamQuery(), /profileDid=did%3Aplc%3Aa/);
  assert.match(f.transports[0].options.streamQuery(), /profileDid=did%3Aplc%3Ab/);
  assert.equal(f.transports[0].reconnects, 1);
  f.transports[0].options.onEvent('profile', { did: 'did:plc:a', status: 'loaded' }); await settle();
  assert.equal(first.events.filter(event => event.type === 'tangent:profile-stream').length, 1);
  assert.equal(second.events.filter(event => event.type === 'tangent:profile-stream').length, 1);
});

test('unsupported SharedWorker falls back to the single-tab controller and local profile stream', async () => {
  let created = 0;
  const direct = { starts: [], visible: [], start(value) { this.starts.push(value); }, stop() {},
    setVisible(value) { this.visible.push(value); }, snapshot() { return { mode: 'direct' }; } };
  const window = { TangentActivity: { create() { created++; return direct; } }, dispatchEvent() {} };
  vm.runInNewContext(clientSource, { window, document: { hidden: false }, SharedWorker: undefined, AbortController, CustomEvent,
    setInterval() { return 1; }, clearInterval() {}, setTimeout() { return 1; }, clearTimeout() {} });
  const controller = window.TangentActivityCoordinator.create({ onSnapshot() {} });
  controller.start({ participant: 'alice' });
  assert.equal(created, 1); assert.equal(direct.starts.length, 1); assert.equal(direct.starts[0].participant, 'alice');
  assert.equal(controller.setProfiles(['did:plc:a']), false);
  assert.equal(controller.snapshot().mode, 'direct');
});

test('a stale participant heartbeat cannot seize the origin transport from the newest identity', async () => {
  const f = sharedFixture(), first = f.client('first'), second = f.client('second'); await settle();
  first.controller.start({ participant: 'alice' }); await settle();
  second.controller.start({ participant: 'bob' }); await settle();
  assert.equal(f.transports.length, 2);
  assert.equal(f.transports.at(-1).starts[0].participant, 'bob');
  f.sharedPorts[0].postMessage({ type: 'heartbeat' }); await settle();
  assert.equal(f.transports.length, 2);
  assert.equal(f.transports.at(-1).starts[0].participant, 'bob');
  assert.equal(first.identities.at(-1).reason, 'participant-generation-changed');
});
