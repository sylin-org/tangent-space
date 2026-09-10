#!/usr/bin/env node
// Live EPIC-004 activity proof. It deliberately uses only an existing disposable fixture.
import { mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'

const defaults = {
  origin: 'http://127.0.0.1:5220',
  contextDir: '.local/demo/1788984280268',
  tangent: 'small-hours',
  room: 'small-hours-lounge',
}

const options = { ...defaults }
for (const value of process.argv.slice(2)) {
  const match = /^--(origin|context-dir|tangent|room)=(.+)$/.exec(value)
  if (!match) throw new Error('UnsupportedArgument')
  options[match[1].replace(/-([a-z])/g, (_, c) => c.toUpperCase())] = match[2]
}

const origin = new URL(options.origin)
if (!['127.0.0.1', 'localhost', '[::1]'].includes(origin.hostname) || !['http:', 'https:'].includes(origin.protocol))
  throw new Error('LoopbackOriginRequired')
origin.pathname = '/'; origin.search = ''; origin.hash = ''

const contextDir = resolve(options.contextDir)
const owner = JSON.parse(await readFile(resolve(contextDir, 'owner.cookies.json'), 'utf8'))
const agent = JSON.parse(await readFile(resolve(contextDir, 'agent-credential.json'), 'utf8'))
if (!owner?.did || !Array.isArray(owner.cookies) || !agent?.credential?.did || typeof agent.token !== 'string')
  throw new Error('FixtureCredentialsInvalid')
if (!Array.isArray(agent.credential.grants) || !agent.credential.grants.includes('read'))
  throw new Error('AgentReadGrantRequired')

const ownerDid = owner.did
const agentDid = agent.credential.did
const cookie = owner.cookies.map(pair => Array.isArray(pair) && typeof pair[0] === 'string' && typeof pair[1] === 'string'
  ? `${pair[0]}=${pair[1]}` : '').filter(Boolean).join('; ')
if (!cookie) throw new Error('OwnerCookieInvalid')

const evidence = { proof: 'epic004-activity', origin: origin.origin, tangent: options.tangent, room: options.room,
  membershipWrites: 0, replayMarkers: 0, uniqueMarkers: 0, replayPages: 0, idleWaitMilliseconds: 0,
  statuses: {}, completedAt: null }

function endpoint(path, query = undefined) {
  const url = new URL(path, origin)
  for (const [key, value] of Object.entries(query ?? {})) if (value != null) url.searchParams.set(key, value)
  return url
}

async function request(path, { actor, method = 'GET', body, query, signal } = {}) {
  const headers = { accept: 'application/json' }
  if (actor === 'owner') {
    headers.cookie = cookie
    headers['x-tangent-participant'] = ownerDid
    if (method !== 'GET') { headers.origin = origin.origin; headers['sec-fetch-site'] = 'same-origin' }
  }
  if (actor === 'agent') headers.authorization = `Bearer ${agent.token}`
  if (body !== undefined) headers['content-type'] = 'application/json'
  const response = await fetch(endpoint(path, query), { method, headers, body: body === undefined ? undefined : JSON.stringify(body),
    redirect: 'error', signal: signal ?? AbortSignal.timeout(30000) })
  const text = await response.text()
  if (Buffer.byteLength(text) > 512 * 1024) throw new Error('ResponseTooLarge')
  let json = null
  if (text) { try { json = JSON.parse(text) } catch { throw new Error('InvalidJsonResponse') } }
  return { status: response.status, json }
}

function requireStatus(result, expected, label) {
  if (result.status !== expected) throw new Error(`${label}:status-${result.status}`)
  evidence.statuses[label] = result.status
  return result.json
}

function requireSnapshot(value, label) {
  if (!value || typeof value.checkpoint !== 'string' || !Array.isArray(value.events) || !Array.isArray(value.channels))
    throw new Error(`${label}:invalid-snapshot`)
  return value
}

async function ownerMembership(role) {
  const result = await request(`/api/tangents/${encodeURIComponent(options.tangent)}/members/${encodeURIComponent(agentDid)}`, {
    actor: 'owner', method: 'PUT', body: { role },
  })
  return requireStatus(result, 200, `membership-${role.toLowerCase()}`)
}

async function openSse() {
  const headers = { accept: 'text/event-stream', authorization: `Bearer ${agent.token}` }
  const response = await fetch(endpoint('/api/activity/events'), { headers, redirect: 'error', signal: AbortSignal.timeout(30000) })
  if (response.status !== 200 || !response.body) throw new Error(`sse-start:status-${response.status}`)
  evidence.statuses.sseStart = response.status
  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let pending = ''
  async function block(timeoutMs) {
    const deadline = Date.now() + timeoutMs
    while (true) {
      const split = pending.search(/\r?\n\r?\n/)
      if (split >= 0) { const item = pending.slice(0, split); pending = pending.slice(split).replace(/^\r?\n\r?\n/, ''); return item }
      const remaining = deadline - Date.now()
      if (remaining <= 0) throw new Error('sse-event-timeout')
      const value = await Promise.race([reader.read(), new Promise((_, reject) => setTimeout(() => reject(new Error('sse-event-timeout')), remaining))])
      if (value.done) throw new Error('sse-closed-before-activity')
      pending += decoder.decode(value.value, { stream: true })
      if (pending.length > 512 * 1024) throw new Error('SsePayloadTooLarge')
    }
  }
  return { reader, block }
}

function sseData(block) {
  const line = block.split(/\r?\n/).find(value => value.startsWith('data:'))
  return line ? JSON.parse(line.slice(5).trim()) : null
}

let membershipRestored = false
try {
  // The initial snapshots prove the fixture identities work before this proof changes only membership audit state.
  const ownerBaseline = requireSnapshot(requireStatus(await request('/api/activity', { actor: 'owner' }), 200, 'owner-baseline'), 'owner-baseline')
  const agentBaseline = requireSnapshot(requireStatus(await request('/api/activity', { actor: 'agent' }), 200, 'agent-baseline'), 'agent-baseline')
  requireStatus(await request(`/api/rooms/${encodeURIComponent(options.room)}/messages`, { actor: 'agent' }), 200, 'agent-history-before')
  if (!agentBaseline.channels.some(channel => channel.roomKey === options.room && channel.tangentKey === options.tangent))
    throw new Error('AgentChannelBaselineMissing')

  let cursor = agentBaseline.checkpoint
  for (let index = 0; index < 30; index++) { await ownerMembership('Member'); evidence.membershipWrites++ }

  const seen = new Set()
  let firstPage = true
  while (true) {
    const page = requireSnapshot(requireStatus(await request('/api/activity', { actor: 'agent', query: { cursor } }), 200, 'agent-replay'), 'agent-replay')
    evidence.replayPages++
    if (page.resetRequired) throw new Error('UnexpectedReplayReset')
    if (firstPage && !page.hasMore) throw new Error('ReplayDidNotPageBeyond25')
    firstPage = false
    if (page.hasMore && (page.nextCursor !== page.checkpoint || page.events.length > 25)) throw new Error('UnstableReplayBoundary')
    for (const event of page.events) {
      if (event?.kind === 'MembershipChanged' && event.tangentKey === options.tangent
          && event.actorDid === ownerDid && event.targetDid === agentDid) {
        if (seen.has(event.sequence)) throw new Error('DuplicateActivityMarker')
        seen.add(event.sequence)
      }
    }
    cursor = page.checkpoint
    if (!page.hasMore) break
    if (evidence.replayPages > 8) throw new Error('ReplayPageBoundExceeded')
  }
  evidence.replayMarkers = seen.size; evidence.uniqueMarkers = seen.size
  if (seen.size < 30) throw new Error('MissingMembershipMarkers')

  const wrongActor = requireSnapshot(requireStatus(await request('/api/activity', { actor: 'owner', query: { cursor } }), 200, 'wrong-actor-cursor'), 'wrong-actor-cursor')
  if (!wrongActor.resetRequired) throw new Error('WrongActorCursorWasAccepted')

  const idleStarted = Date.now()
  const idle = requireSnapshot(requireStatus(await request('/api/activity/wait', { actor: 'agent', query: { cursor } }), 200, 'idle-wait'), 'idle-wait')
  evidence.idleWaitMilliseconds = Date.now() - idleStarted
  if (idle.checkpoint !== cursor || evidence.idleWaitMilliseconds < 18000 || evidence.idleWaitMilliseconds > 26000)
    throw new Error('IdleWaitDidNotRemainBounded')

  const sse = await openSse()
  // Drain the authorized initial payload before changing policy; only the post-removal stream is inspected below.
  sseData(await sse.block(5000))
  await ownerMembership('Removed')
  const postRemoval = sseData(await sse.block(7000))
  await sse.reader.cancel()
  if (postRemoval && JSON.stringify(postRemoval).includes(options.room)) throw new Error('SseLeakedRemovedRoom')
  requireStatus(await request(`/api/rooms/${encodeURIComponent(options.room)}/messages`, { actor: 'agent' }), 403, 'agent-history-removed')
  const removedActivity = requireSnapshot(requireStatus(await request('/api/activity', { actor: 'agent' }), 200, 'agent-activity-removed'), 'agent-activity-removed')
  if (removedActivity.channels.some(channel => channel.roomKey === options.room || channel.tangentKey === options.tangent))
    throw new Error('RemovedActivityLeakedChannel')
  const removedDirectory = requireStatus(await request('/api/tangents', { actor: 'agent' }), 200, 'agent-directory-removed')
  if (Array.isArray(removedDirectory?.tangents) && removedDirectory.tangents.some(tangent => tangent?.key === options.tangent))
    throw new Error('RemovedDirectoryLeakedTangent')

  await ownerMembership('Member'); membershipRestored = true
  evidence.completedAt = new Date().toISOString()
  const destination = resolve('docs/evidence/epic004-activity.json')
  await mkdir(dirname(destination), { recursive: true })
  const temporary = `${destination}.${process.pid}.tmp`
  await writeFile(temporary, JSON.stringify(evidence, null, 2) + '\n', { encoding: 'utf8', mode: 0o600 })
  await rename(temporary, destination)
  console.log(JSON.stringify({ proof: evidence.proof, replayMarkers: evidence.replayMarkers, replayPages: evidence.replayPages,
    idleWaitMilliseconds: evidence.idleWaitMilliseconds, restored: membershipRestored }))
} finally {
  if (!membershipRestored) {
    try { await ownerMembership('Member') } catch { /* Preserve the original failure without printing credentials or bodies. */ }
  }
}
