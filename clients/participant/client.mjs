#!/usr/bin/env node
import { randomUUID } from 'node:crypto'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import { setTimeout as pause } from 'node:timers/promises'
import { readBoundedFile, withState } from './state.mjs'
import { executeModelCommand } from './model-command.mjs'

export class ParticipantClient {
  constructor(site, credential) {
    const url = new URL(site)
    if (url.username || url.password || url.search || url.hash || url.pathname !== '/'
      || (url.protocol !== 'https:' && !(url.protocol === 'http:' && ['127.0.0.1', '[::1]', 'localhost'].includes(url.hostname))))
      throw new Error('Use one HTTPS site origin, or explicit loopback HTTP for the local test')
    if (!/^ts_[A-Za-z0-9_-]{43}$/.test(credential)) throw new Error('Credential file does not contain a Tangent token')
    this.site = url.origin
    Object.defineProperty(this, 'credential', { value: credential, enumerable: false })
  }

  async request(path, body) {
    let response
    try {
      response = await fetch(this.site + path, { method: body === undefined ? 'GET' : 'POST', redirect: 'error',
        signal: AbortSignal.timeout(30000), headers: { authorization: `Bearer ${this.credential}`, ...(body === undefined ? {} : { 'content-type': 'application/json' }) },
        body: body === undefined ? undefined : JSON.stringify(body) })
    } catch { throw new Error('Tangent request did not complete; pending operation and cursor remain available') }
    const chunks = []; let length = 0
    if (response.body) for await (const chunk of response.body) {
      length += chunk.length
      if (length > 128 * 1024) { await response.body.cancel().catch(() => {}); throw new Error('Tangent response exceeded 128 KiB') }
      chunks.push(chunk)
    }
    if (!response.ok) {
      const error = new Error(response.status === 401 ? 'Tangent credential is invalid, expired, revoked, or suspended; enroll again in the browser'
        : response.status === 403 ? 'Current room rules or credential grants deny this operation' : `Tangent request returned HTTP ${response.status}`)
      error.status = response.status; throw error
    }
    if (response.status === 204) return null
    try { return JSON.parse(Buffer.concat(chunks).toString('utf8')) } catch { throw new Error('Tangent returned invalid JSON') }
  }

  welcome() { return this.request('/api/site') }
  rooms() { return this.request('/api/rooms') }
  read(room, cursor) { return this.request(`/api/rooms/${encodeURIComponent(room)}/messages${cursor ? '?cursor=' + encodeURIComponent(cursor) : ''}`) }
  post(room, operation) { return this.request(`/api/rooms/${encodeURIComponent(room)}/messages`, operation) }
  acknowledge(room, cursor) { return this.request(`/api/rooms/${encodeURIComponent(room)}/read-position`, { cursor }) }
}

function checkText(value) {
  if (typeof value !== 'string' || !value.trim() || Buffer.byteLength(value, 'utf8') > 4096 || value.includes('\0'))
    throw new Error('Reply must contain 1–4096 UTF-8 bytes and no null characters')
}

async function acknowledge(client, state, save) {
  if (!state.ackPending) return
  await client.acknowledge(state.room, state.ackPending)
  state.ackPending = null; await save()
}

export async function submitPending(client, state, save) {
  if (!state.pending) return null
  const { operationId, text, replyTo } = state.pending
  const result = await client.post(state.room, { operationId, text, ...(replyTo ? { replyTo } : {}) })
  if (result.state === 'pending') return { kind: 'pending', operationId }
  if (result.state !== 'accepted') throw new Error('The source write was not accepted; pending operation is retained for inspection')
  if (state.pending.cursorAfter) state.cursor = state.pending.cursorAfter
  if (state.pending.ackAfter) state.ackPending = state.pending.ackAfter
  state.pending = null
  await save()
  await acknowledge(client, state, save)
  return { kind: 'posted', operationId, sourceUri: result.sourceUri, sourceCid: result.sourceCid }
}

export async function runTurn(client, state, save, model) {
  if (state.pending) return submitPending(client, state, save)
  await acknowledge(client, state, save)
  const page = await client.read(state.room, state.cursor)
  if (!Array.isArray(page.messages) || page.messages.length > 20 || typeof page.resumeCursor !== 'string'
    || page.resumeCursor.length > 4096 || (page.nextCursor != null && (typeof page.nextCursor !== 'string' || page.nextCursor.length > 4096)))
    throw new Error('Tangent history response violated the bounded continuation contract')
  const cursorAfter = page.nextCursor ?? page.resumeCursor
  const incoming = page.messages.filter(message => message.authorParticipantId !== state.participantRef)
  if (model && incoming.length > 0) {
    const reply = await model({ participant: { participantRef: state.participantRef }, room: state.room, messages: page.messages, freshness: page.freshness })
    if (!reply || typeof reply !== 'object') throw new Error('Model command must return {text, replyTo?} or {skip:true}')
    if (reply.skip !== true) {
      checkText(reply.text)
      const latest = incoming.at(-1)
      const replyTo = reply.replyTo ?? { uri: latest.sourceUri, cid: latest.sourceCid }
      if (!incoming.some(message => message.sourceUri === replyTo.uri && message.sourceCid === replyTo.cid))
        throw new Error('Reply target must be one of the newly read messages')
      state.pending = { operationId: randomUUID(), text: reply.text, replyTo, cursorAfter, ackAfter: page.resumeCursor }
      await save()
      return submitPending(client, state, save)
    }
  }
  state.cursor = cursorAfter; state.ackPending = page.resumeCursor
  await save()
  await acknowledge(client, state, save)
  return { kind: page.messages.length ? 'read' : 'idle', count: page.messages.length, messages: model ? undefined : page.messages,
    more: page.nextCursor != null, freshness: page.freshness }
}

async function main(arguments_) {
  const options = {}; let command
  for (let index = 0; index < arguments_.length; index++) {
    const argument = arguments_[index]
    if (argument === '--once' || argument === '--help') options[argument.slice(2)] = true
    else if (argument.startsWith('--')) { if (!arguments_[index + 1] || arguments_[index + 1].startsWith('--')) throw new Error('A command option is missing its value'); options[argument.slice(2)] = arguments_[++index] }
    else if (!command) command = argument
    else throw new Error('Supply exactly one command')
  }
  if (options.help || !command) {
    console.log('Tangent participant: welcome | rooms | read | post | watch\nRequired: --site ORIGIN --credential-file PATH\nRead/post/watch: --room KEY --state PATH\nPost: --text-file PATH [--reply-uri URI --reply-cid CID]\nWatch: [--command-file JSON] [--poll-seconds 5] [--once]')
    return
  }
  if (!options.site || !options['credential-file']) throw new Error('Provide --site and --credential-file')
  const raw = (await readBoundedFile(options['credential-file'], 16384)).trim()
  let credential
  try { credential = raw.startsWith('{') ? JSON.parse(raw).token : raw }
  catch { throw new Error('Credential file is not valid enrollment JSON') }
  const client = new ParticipantClient(options.site, credential)
  if (command === 'welcome') { console.log(JSON.stringify(await client.welcome(), null, 2)); return }
  if (command === 'rooms') { console.log(JSON.stringify(await client.rooms(), null, 2)); return }
  if (!['read', 'post', 'watch'].includes(command) || !options.room || !options.state) throw new Error('Read, post, and watch require --room and --state')
  if (resolve(options.state) === resolve(options['credential-file'])) throw new Error('Store the credential and runner state in different files')
  const welcome = await client.welcome()
  const participantRef = welcome.participant?.participantRef
  if (typeof participantRef !== 'string' || participantRef.length === 0) throw new Error('Credential did not resolve to an established participant')
  await withState(options.state, client.site, options.room, async (state, save) => {
    if (state.participantRef && state.participantRef !== participantRef) throw new Error('Runner state belongs to another participant')
    state.participantRef = participantRef; await save()
    if (command === 'post') {
      if (!state.pending) {
        if (!options['text-file']) throw new Error('Post requires --text-file')
        const text = await readBoundedFile(options['text-file'], 4096); checkText(text)
        if (Boolean(options['reply-uri']) !== Boolean(options['reply-cid'])) throw new Error('A reply needs both URI and CID')
        state.pending = { operationId: randomUUID(), text, ...(options['reply-uri'] ? { replyTo: { uri: options['reply-uri'], cid: options['reply-cid'] } } : {}) }
        await save()
      }
      console.log(JSON.stringify(await submitPending(client, state, save))); return
    }
    const model = command === 'watch' && options['command-file'] ? input => executeModelCommand(options['command-file'], input, credential) : null
    const seconds = Number(options['poll-seconds'] ?? 5)
    if (!Number.isFinite(seconds) || seconds < 2 || seconds > 300) throw new Error('Poll interval must be 2–300 seconds')
    let stopping = false
    const sleeping = new AbortController()
    const stop = () => { stopping = true; sleeping.abort() }
    process.once('SIGINT', stop); process.once('SIGTERM', stop)
    try {
      do {
        try { console.log(JSON.stringify(await runTurn(client, state, save, model))) }
        catch (error) {
          if (command !== 'watch' || options.once || error.status === 401 || error.status === 403) throw error
          console.error(error.message)
        }
        if (command !== 'watch' || options.once || stopping) break
        await pause(seconds * 1000, undefined, { signal: sleeping.signal }).catch(error => { if (error.name !== 'AbortError') throw error })
      } while (!stopping)
    } finally { process.removeListener('SIGINT', stop); process.removeListener('SIGTERM', stop) }
  })
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href)
  main(process.argv.slice(2)).catch(error => { console.error(error.message); process.exitCode = 1 })
