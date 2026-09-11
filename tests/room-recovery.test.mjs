import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import vm from 'node:vm';

const source = await readFile(new URL('../src/server/web/wwwroot/rooms.js', import.meta.url), 'utf8');
const saved = { operationId: 'c2b64a14-f4b8-444e-8a1d-2a3053ebc6bb', text: 'My original message',
  replyTo: { uri: 'at://example/reply', cid: 'original-cid' }, detail: 'reauthorization-required' };
const later = { operationId: 'e395b4f6-6d06-4c21-b64a-04777c48993b', text: 'Another saved message', replyTo: null, detail: 'source-unavailable' };
const settle = async () => { for (let turn = 0; turn < 5; turn++) await new Promise(resolve => setImmediate(resolve)); };
function deferred() { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; }

// Exercise the browser script's event/request boundary without a live account or PDS.
// The DOM stub only supplies rendering primitives; it contains no recovery logic.
function fixture(respond = () => ({ messages: [] })) {
  class Element {
    constructor() {
      this.children = []; this.listeners = new Map(); this.value = ''; this.hidden = true; this.textContent = ''; this.dataset = {};
      this.classList = { add() {}, remove() {}, toggle() {} };
      this.style = { setProperty() {} };
      const fields = new Map();
      this.elements = { namedItem: name => { if (!fields.has(name)) fields.set(name, new Element()); return fields.get(name); } };
    }
    addEventListener(name, handler) { const all = this.listeners.get(name) || []; all.push(handler); this.listeners.set(name, all); }
    emit(name, values = {}) { for (const listener of this.listeners.get(name) || []) listener({ preventDefault() {}, ...values }); }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.children = [...children]; }
    setAttribute(name, value) { this[name] = value; }
    focus() {}
  }
  const elements = new Map();
  const get = id => { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); };
  const window = new Element(), document = new Element(), requests = [];
  Object.assign(document, { hidden: true, body: new Element(), getElementById: get, createElement: () => new Element(), querySelectorAll: () => get('room-list').children });
  const location = { href: 'http://127.0.0.1:5220/?room=lounge' };
  class FormData { constructor() {} *[Symbol.iterator]() {} }
  vm.runInNewContext(source, { window, document, location, URL, TextEncoder, AbortController, FormData, crypto: { randomUUID },
    history: { replaceState: (_state, _title, path) => { location.href = new URL(path, location.href).href; } },
    fetch: async (path, options) => {
      const request = { path, method: options.method, headers: options.headers, body: options.body && JSON.parse(options.body) }; requests.push(request);
      let data;
      if (/^\/api\/rooms\/[^/]+$/.test(path)) data = { key: path.split('/').at(-1), title: path.split('/').at(-1), canRead: true, canWrite: true };
      else if (path.includes('/messages?')) data = { messages: [], freshness: 'checked' };
      else data = await respond(request);
      if (data instanceof Response) return data;
      return { ok: true, status: 200, json: async () => data };
    }
  });
  return { get, requests, welcome(did = 'did:plc:alice', key = 'lounge', tangents) {
    location.href = 'http://127.0.0.1:5220/?room=' + key;
    window.emit('tangent:welcome', { detail: { participant: { did }, rooms: { rooms: [{ key: 'lounge', title: 'Lounge' }, { key: 'workshop', title: 'Workshop' }] }, ...(tangents ? { tangents } : {}) } });
  }, choose(key) { get('room-list').children.find(element => element.dataset.key === key).emit('click'); },
  submit() { get('message-form').emit('submit'); },
  type(value) { get('message-text').value = value; get('message-text').emit('input'); } };
}

test('server recovery shows permission guidance and reconnect link without automatically posting', async () => {
  const f = fixture(() => ({ messages: [saved] })); f.welcome(); await settle();
  assert.equal(f.get('message-text').value, saved.text);
  assert.equal(f.get('message-text').readOnly, true);
  assert.equal(f.get('send-message').textContent, 'Retry saved message');
  assert.match(f.get('pending-message').textContent, /Your message is saved.*has not granted permission/);
  assert.equal(f.get('reconnect-room-access').hidden, false);
  assert.equal(f.get('reconnect-room-access').href, '/api/connections/rooms?room=lounge');
  assert.equal(f.requests.filter(request => request.method === 'POST').length, 0);
});

test('fresh owner setup is bound to the persisted home key, then returns to an editable blank key', async () => {
  const f = fixture();
  const home = { tangents: [{ key: 'home', name: '', description: '', motto: '', accent: '#d88957', artwork: '', channels: [] }], canCreate: true, setupRequired: true };
  f.welcome('did:plc:owner', 'home', home); await settle();
  const key = f.get('create-tangent').elements.namedItem('key');
  assert.equal(key.value, 'home'); assert.equal(key.readOnly, true);
  f.welcome('did:plc:owner', 'home', { tangents: [{ ...home.tangents[0], name: 'Home' }], canCreate: true, setupRequired: false }); await settle();
  assert.equal(key.value, ''); assert.equal(key.readOnly, false);
});

test('retry submits only the original write fields and completion restores the next saved message', async () => {
  let posted = false;
  const f = fixture(request => {
    if (request.method === 'POST') { posted = true; return { state: 'accepted' }; }
    return { messages: posted ? [later] : [saved] };
  }); f.welcome(); await settle(); f.submit(); await settle();
  assert.deepEqual(f.requests.find(request => request.method === 'POST').body, {
    operationId: saved.operationId, text: saved.text, replyTo: saved.replyTo
  });
  assert.equal(f.get('message-text').value, later.text);
  assert.equal(f.get('reconnect-room-access').hidden, true);
  assert.equal(f.requests.filter(request => request.path.endsWith('/messages/pending')).length, 2);
  assert.equal(f.requests.filter(request => request.method === 'POST').length, 1);
});

test('a new draft typed while recovery loads survives completing the older pending message', async () => {
  const lookup = deferred(); let posted = false;
  const f = fixture(request => {
    if (request.method === 'POST') { posted = true; return { state: 'accepted' }; }
    return posted ? { messages: [] } : lookup.promise;
  }); f.welcome(); await settle(); f.type('My separate new draft'); f.submit();
  assert.equal(f.requests.some(request => request.method === 'POST'), false);
  lookup.resolve({ messages: [saved] }); await settle(); f.submit(); await settle();
  assert.equal(f.get('message-text').value, 'My separate new draft');
  assert.equal(f.get('message-text').readOnly, false);
});

test('revisiting a room preserves its local operation and loads the next saved message when sending finishes', async () => {
  const sending = deferred(); let posted = false;
  const f = fixture(request => {
    if (request.method === 'POST') return sending.promise.then(() => { posted = true; return { state: 'accepted' }; });
    return { messages: request.path.includes('/workshop/') ? [] : [posted ? later : saved] };
  });
  f.welcome(); await settle(); f.submit(); await settle();
  f.choose('workshop'); await settle(); f.choose('lounge'); await settle();
  assert.equal(f.get('message-text').value, saved.text);
  assert.equal(f.requests.filter(request => request.path === '/api/rooms/lounge/messages/pending').length, 1);
  sending.resolve(); await settle();
  assert.equal(f.get('message-text').value, later.text);
  assert.equal(f.requests.filter(request => request.path === '/api/rooms/lounge/messages/pending').length, 2);
});

test('a late recovery response cannot replace the draft in another room', async () => {
  const lookup = deferred();
  const f = fixture(request => request.path.includes('/lounge/') ? lookup.promise : { messages: [] });
  f.welcome(); await settle(); f.choose('workshop'); await settle(); f.type('Workshop draft');
  lookup.resolve({ messages: [saved] }); await settle();
  assert.equal(f.get('room-title').textContent, 'workshop');
  assert.equal(f.get('message-text').value, 'Workshop draft');
  assert.equal(f.get('message-text').readOnly, false);
  assert.equal(f.get('reconnect-room-access').hidden, true);
});

test('a late recovery response cannot expose the previous account message after identity changes', async () => {
  const lookup = deferred(); let lookups = 0;
  const f = fixture(() => ++lookups === 1 ? lookup.promise : { messages: [] });
  f.welcome(); await settle(); f.welcome('did:plc:bob'); await settle(); f.type('Bob draft');
  lookup.resolve({ messages: [saved] }); await settle();
  assert.equal(f.get('message-text').value, 'Bob draft');
  assert.equal(f.get('reconnect-room-access').hidden, true);
});

test('late send completion cannot remove another account pending message in the same room', async () => {
  const sending = deferred(); let lookups = 0;
  const f = fixture(request => request.method === 'POST' ? sending.promise : { messages: [++lookups === 1 ? saved : later] });
  f.welcome(); await settle(); f.submit(); await settle(); f.welcome('did:plc:bob'); await settle();
  sending.resolve({ state: 'accepted' }); await settle();
  assert.equal(f.get('message-text').value, later.text);
  assert.equal(f.get('message-text').readOnly, true);
  assert.equal(f.get('send-message').disabled, false);
});

test('new posts retain one operation through transient and permission failures without sending recovery metadata', async () => {
  let attempts = 0;
  const f = fixture(request => request.method === 'POST'
    ? { state: 'pending', detail: ++attempts === 1 ? 'source-unavailable' : 'reauthorization-required' }
    : { messages: [] });
  f.welcome(); await settle(); f.type('A new message'); f.submit(); await settle();
  assert.equal(f.get('reconnect-room-access').hidden, true);
  assert.match(f.get('pending-message').textContent, /Your message is saved/);
  f.submit(); await settle();
  assert.equal(f.get('reconnect-room-access').hidden, false);
  const posts = f.requests.filter(request => request.method === 'POST');
  assert.equal(posts.length, 2); assert.deepEqual(posts[0].body, posts[1].body);
  assert.deepEqual(Object.keys(posts[1].body).sort(), ['operationId', 'text']);
});

for (const failure of [
  { name: 'a network failure', respond() { throw new TypeError('Failed to fetch'); } },
  { name: 'an HTTP 500 response', respond: () => new Response('{"reason":"Unavailable"}', { status: 500 }) },
  { name: 'a non-JSON HTTP 200 response', respond: () => new Response('<html>Unavailable</html>') },
  { name: 'a missing message listing', respond: () => ({}) },
  { name: 'a malformed saved message', respond: () => ({ messages: [{ ...saved, operationId: null }] }) }
]) {
  test(`recovery stays closed after ${failure.name} until a successful check restores the original operation`, async () => {
    let failed = true;
    const f = fixture(request => {
      if (request.method === 'POST') return { state: 'pending', detail: 'reauthorization-required' };
      return failed ? failure.respond() : { messages: [saved] };
    });
    f.welcome(); await settle();
    assert.equal(f.get('send-message').disabled, true);
    assert.match(f.get('action-status').textContent, /Saved messages could not be checked/);
    f.type('Do not create a new operation while recovery is unavailable'); f.submit(); await settle();
    assert.equal(f.requests.some(request => request.method === 'POST'), false);
    failed = false; f.choose('lounge'); await settle();
    assert.equal(f.get('send-message').disabled, false);
    assert.equal(f.get('message-text').value, saved.text);
    f.submit(); await settle();
    const posts = f.requests.filter(request => request.method === 'POST');
    assert.equal(posts.length, 1);
    assert.deepEqual(posts[0].body, { operationId: saved.operationId, text: saved.text, replyTo: saved.replyTo });
  });
}

test('failed recovery in one room or account does not block a successful check for the next participant', async () => {
  let fail = true;
  const f = fixture(() => fail ? new Response('{}', { status: 500 }) : { messages: [] });
  f.welcome(); await settle();
  assert.equal(f.get('send-message').disabled, true);
  fail = false; f.choose('workshop'); await settle();
  assert.equal(f.get('send-message').disabled, false);
  f.welcome('did:plc:bob'); await settle();
  assert.equal(f.get('send-message').disabled, false);
});

test('a private recovery lookup sends the displayed identity and a mismatch requires reloading', async () => {
  const f = fixture(() => new Response('{"reason":"Your signed-in account changed."}', { status: 409 }));
  f.welcome(); await settle();
  const lookup = f.requests.find(request => request.path.endsWith('/messages/pending'));
  assert.equal(lookup.headers['X-Tangent-Participant'], 'did:plc:alice');
  assert.match(f.get('action-status').textContent, /signed-in account changed.*Reload/);
  assert.doesNotMatch(f.get('action-status').textContent, /Check for updates/);
  assert.equal(f.get('message-form').hidden, true);
  assert.equal(f.get('sync-room').hidden, true);
  assert.equal(f.get('send-message').disabled, true);
  f.submit(); await settle();
  assert.equal(f.requests.some(request => request.method === 'POST'), false);
  assert.equal(f.requests.some(request => request.path.includes('/messages?')), false);
});
