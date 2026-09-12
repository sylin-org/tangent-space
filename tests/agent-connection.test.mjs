import test from 'node:test';
import assert from 'node:assert/strict';
import { createAgentConnection } from '../src/server/web/wwwroot/agent-connection.js';

const token = 'tangent-fixture-credential-for-transport-tests';
const participant = { participantRef: 'a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6', did: 'did:plc:agent', handle: 'agent.example' };
const result = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const memory = () => { const values = new Map(); return { getItem: key => values.get(key), setItem: (key, value) => values.set(key, value), removeItem: key => values.delete(key) }; };

test('agent requests omit human cookies, forbid redirects, and never expose credentials in identity', async () => {
  const calls = [];
  const connection = createAgentConnection({ fetch: async (path, options) => { calls.push({ path, options }); return result({ participant }); } });
  await connection.connect(token, participant.participantRef);
  await connection.api('/api/rooms/workshop/messages', { method: 'POST', body: { text: 'hello' } });
  assert.deepEqual(connection.identity(), participant);
  assert.equal(JSON.stringify(connection.identity()).includes(token), false);
  for (const { options } of calls) { assert.equal(options.credentials, 'omit'); assert.equal(options.redirect, 'error'); assert.equal(options.headers.Authorization, 'Bearer ' + token); }
});

test('a disconnected page cannot use a human session as an implicit agent', async () => {
  let calls = 0;
  const connection = createAgentConnection({ fetch: async () => { calls++; return result({ participant }); } });
  await assert.rejects(connection.api('/api/site'), /Connect an agent/);
  assert.equal(calls, 0);
});

test('revocation clears stored connection and cannot fall back to cookies', async () => {
  let calls = 0;
  const storage = memory();
  const connection = createAgentConnection({ storage, fetch: async () => ++calls === 1 ? result({ participant }) : result({ error: 'invalid' }, 401) });
  await connection.connect(token);
  await assert.rejects(connection.api('/api/rooms'), /invalid, expired or revoked/);
  assert.equal(connection.identity(), null);
  assert.equal(await connection.restore(), null);
  await assert.rejects(connection.api('/api/site'), /Connect an agent/);
  assert.equal(calls, 2);
});

test('a mismatched credential file cannot select its stated author', async () => {
  const connection = createAgentConnection({ fetch: async () => result({ participant }) });
  await assert.rejects(connection.connect(token, '0f1e2d3c4b5a69788796a5b4c3d2e1f0'), /expected Participant/);
  assert.equal(connection.identity(), null);
});

test('a non-JSON authentication failure still removes the agent connection', async () => {
  const connection = createAgentConnection({ fetch: async path => path === '/api/site' ? result({ participant }) : new Response('Authentication required', { status: 401 }) });
  await connection.connect(token);
  await assert.rejects(connection.api('/api/rooms'), /invalid, expired or revoked/);
  assert.equal(connection.identity(), null);
});

test('reconnection aborts old work and rejects a response even if transport ignores cancellation', async () => {
  let complete;
  let signal;
  const connection = createAgentConnection({ fetch: async (path, options) => {
    if (path === '/api/site') return result({ participant });
    signal = options.signal;
    return new Promise(resolve => { complete = resolve; });
  } });
  await connection.connect(token);
  const request = connection.api('/api/rooms');
  connection.disconnect();
  assert.equal(signal.aborted, true);
  complete(result({ rooms: ['old private data'] }));
  await assert.rejects(request, /connection changed/);
});

test('restore revalidates the saved credential rather than trusting its cached identity', async () => {
  const storage = memory();
  const first = createAgentConnection({ storage, fetch: async () => result({ participant }) });
  await first.connect(token);
  let calls = 0;
  const second = createAgentConnection({ storage, fetch: async () => { calls++; return result({ participant }); } });
  assert.equal(second.identity(), null);
  await second.restore();
  assert.equal(calls, 1);
  assert.equal(second.identity().did, participant.did);
});

test('transport cannot send credential to another origin or an unrelated route', async () => {
  let calls = 0;
  const connection = createAgentConnection({ fetch: async () => { calls++; return result({ participant }); } });
  await connection.connect(token);
  for (const path of ['https://elsewhere.test/api/rooms', '//elsewhere.test/api/rooms', '/auth/logout', '/api/participation/credentials']) await assert.rejects(connection.api(path));
  assert.equal(calls, 1);
});

test('pending write receipt remains available for an unchanged retry', async () => {
  const receipt = { operationId: 'same-operation', state: 'pending', detail: 'retry' };
  const connection = createAgentConnection({ fetch: async path => result(path === '/api/site' ? { participant } : receipt, path === '/api/site' ? 200 : 202) });
  await connection.connect(token);
  assert.deepEqual(await connection.api('/api/rooms/workshop/messages', { method: 'POST', body: {} }), receipt);
});

test('an unbounded streamed response is cancelled before it can be fully buffered', async () => {
  let cancelled = false;
  const connection = createAgentConnection({ fetch: async path => path === '/api/site' ? result({ participant })
    : new Response(new ReadableStream({ pull(controller) { controller.enqueue(new Uint8Array(128 * 1024)); }, cancel() { cancelled = true; } })) });
  await connection.connect(token);
  await assert.rejects(connection.api('/api/rooms'), /exceeds/);
  assert.equal(cancelled, true);
});

test('anonymous arrival is an explicit cookie-free orientation, while activity still requires the agent', async () => {
  const calls = [];
  const connection = createAgentConnection({ fetch: async (path, options) => { calls.push({ path, options }); return result({ identity: null }); } });
  assert.deepEqual(await connection.api('/api/participation/arrival?cursor=opaque'), { identity: null });
  assert.equal(calls[0].options.credentials, 'omit');
  assert.equal(calls[0].options.headers.Authorization, undefined);
  await assert.rejects(connection.api('/api/activity'), /Connect an agent/);
  await assert.rejects(connection.api('/api/participation/arrival', { method: 'POST', body: {} }), /Connect an agent/);
  assert.equal(calls.length, 1);
});

test('authenticated aggregate arrival and bounded activity use the verified agent and guarded paths', async () => {
  const calls = [];
  const connection = createAgentConnection({ fetch: async (path, options) => { calls.push({ path, options }); return result({ participant }); } });
  await connection.connect(token);
  for (const path of ['/api/participation/arrival', '/api/tangents?page=2', '/api/activity?cursor=opaque', '/api/activity/wait?cursor=opaque']) {
    await connection.api(path);
    // Token-only sessions: identity verification is the bearer token, never a
    // participant header echoing identity back at the server.
    assert.equal(calls.at(-1).options.headers['X-Tangent-Participant'], undefined);
    assert.equal(calls.at(-1).options.headers.Authorization, 'Bearer ' + token);
    assert.equal(calls.at(-1).options.credentials, 'omit');
  }
  // /api/tangents/<key> is a legitimate single-segment shape the transport must allow
  // (the server decides if the key exists); an unrelated multi-segment route rejects here.
  for (const path of ['/api/participation/arrival/credentials', '/api/activity/../participation/credentials', '/api/tangents/members/roles']) await assert.rejects(connection.api(path));
  assert.equal(calls.length, 5);
});
