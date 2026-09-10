import test from 'node:test'
import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import { once } from 'node:events'
import { ParticipantClient, runTurn } from './client.mjs'

const token = 'ts_' + 'a'.repeat(43)
const author = 'did:example:human'
const did = 'did:example:agent'
const message = { authorDid: author, sourceUri: 'at://test/message/one', sourceCid: 'bafy-one', content: { text: 'Hello' } }
const state = () => ({ room: 'workshop', did, cursor: null, pending: null, ackPending: null })

test('idle reads and own messages never execute a model', async () => {
  let calls = 0
  const current = state()
  const client = { read: async () => ({ messages: [], resumeCursor: 'idle', nextCursor: null }), acknowledge: async () => {} }
  assert.equal((await runTurn(client, current, async () => {}, async () => { calls++ })).kind, 'idle')
  client.read = async () => ({ messages: [{ ...message, authorDid: did }], resumeCursor: 'own', nextCursor: null })
  await runTurn(client, current, async () => {}, async () => { calls++ })
  assert.equal(calls, 0)
})

test('uncertain post resumes the same saved operation without another model call', async () => {
  const current = state(); const snapshots = []; const operations = []; const acknowledged = []; let calls = 0
  const save = async () => snapshots.push(JSON.stringify(current))
  const client = {
    read: async () => ({ messages: [message], nextCursor: 'page2', resumeCursor: 'resume1', freshness: 'complete' }),
    post: async (_room, operation) => { operations.push(operation); throw new Error('source unavailable') },
    acknowledge: async (_room, cursor) => acknowledged.push(cursor)
  }
  await assert.rejects(runTurn(client, current, save, async () => { calls++; return { text: 'A response' } }))
  assert.equal(calls, 1)
  assert.ok(current.pending.operationId)
  assert.equal(current.cursor, null)
  const restored = JSON.parse(snapshots.at(-1))
  client.post = async (_room, operation) => { operations.push(operation); return { state: 'accepted', sourceUri: 'at://accepted', sourceCid: 'bafy-accepted' } }
  const result = await runTurn(client, restored, async () => {}, async () => { throw new Error('A restarted pending write must not call a model') })
  assert.equal(result.kind, 'posted')
  assert.deepEqual(operations[0], operations[1])
  assert.equal(restored.cursor, 'page2')
  assert.deepEqual(acknowledged, ['resume1'])
  assert.equal(restored.pending, null)
})

test('pending source acceptance retains the operation and does not advance read position', async () => {
  const current = state()
  current.pending = { operationId: 'same-op', text: 'Hello', cursorAfter: 'after' }
  const result = await runTurn({ post: async () => ({ state: 'pending' }) }, current, async () => {}, async () => assert.fail('no model while pending'))
  assert.equal(result.kind, 'pending')
  assert.equal(current.pending.operationId, 'same-op')
  assert.equal(current.cursor, null)
})

test('failed acknowledgement is durable and retried before the next read', async () => {
  const current = state(); const calls = []
  const client = { read: async () => ({ messages: [message], resumeCursor: 'resume', nextCursor: null }), acknowledge: async () => { throw new Error('offline') } }
  await assert.rejects(runTurn(client, current, async () => {}))
  assert.equal(current.cursor, 'resume')
  assert.equal(current.ackPending, 'resume')
  client.acknowledge = async () => calls.push('ack')
  client.read = async () => { calls.push('read'); return { messages: [], resumeCursor: 'resume2', nextCursor: null } }
  await runTurn(client, current, async () => {})
  assert.deepEqual(calls.slice(0, 2), ['ack', 'read'])
})

test('HTTP redirects cannot forward the participant credential', async () => {
  let destinationCalls = 0
  const destination = createServer((_request, response) => { destinationCalls++; response.end('{}') })
  destination.listen(0, '127.0.0.1'); await once(destination, 'listening')
  const origin = createServer((_request, response) => { response.writeHead(302, { location: `http://127.0.0.1:${destination.address().port}/` }); response.end() })
  origin.listen(0, '127.0.0.1'); await once(origin, 'listening')
  try {
    const client = new ParticipantClient(`http://127.0.0.1:${origin.address().port}`, token)
    await assert.rejects(client.welcome(), error => !error.message.includes(token))
    assert.equal(destinationCalls, 0)
    assert.ok(!JSON.stringify(client).includes(token))
  } finally { origin.closeAllConnections(); destination.closeAllConnections(); await Promise.all([new Promise(resolve => origin.close(resolve)), new Promise(resolve => destination.close(resolve))]) }
})

test('server error bodies are not reflected into credential-bearing logs', async () => {
  const server = createServer((_request, response) => { response.writeHead(401); response.end(JSON.stringify({ echoedCredential: token })) })
  server.listen(0, '127.0.0.1'); await once(server, 'listening')
  try {
    const client = new ParticipantClient(`http://127.0.0.1:${server.address().port}`, token)
    await assert.rejects(client.welcome(), error => error.status === 401 && !error.message.includes(token))
  } finally { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)) }
})
