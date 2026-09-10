// Disposable independent Spaces client for the pinned local reference network.
// Stage beside /atproto/packages (e.g. /atproto/tangent-client/direct-spaces.mjs).
// JSON-lines stdin: login {role}; issue {role,space,name}; read {name,space,repo,rkey};
// write {role,space,rkey,record,mode?:'create'|'put'};
// check {role,space,user,port,aud?,method?,exp?,tamper?}. EOF discards every key/token.
// Authentication here is legacy createSession, deliberately distinct from the
// separately proven Koan OAuth client. DPoP mechanics are the official implementation.
import { readFile } from 'node:fs/promises'
import { createInterface } from 'node:readline'
import { JoseKey } from '../packages/oauth/oauth-provider/dist/oauth-provider.js'
import { createDpopProof } from '../packages/space/dist/index.js'

const fixture = JSON.parse(await readFile('/evidence/fixtures.json', 'utf8'))
if (fixture.status !== 'ready') throw new Error('Disposable fixture is not ready.')
const accounts = new Map(fixture.accounts.map(account => [account.role, account]))
const byDid = new Map(fixture.accounts.map(account => [account.did, account]))
const authority = accounts.get('authority')
const sessions = new Map()
const credentials = new Map()
const maximumBytes = 65536

function account(roleOrDid) {
  const found = accounts.get(roleOrDid) ?? byDid.get(roleOrDid)
  if (!found) throw new Error('InvalidCommand')
  const origin = new URL(found.pds)
  if (origin.protocol !== 'http:' || !['localhost', '127.0.0.1'].includes(origin.hostname))
    throw new Error('InvalidFixtureOrigin')
  return found
}

function space(value) {
  const prefix = `at://${authority.did}/space/${fixture.spaceType}/`
  if (typeof value !== 'string' || !value.startsWith(prefix)
      || !/^[a-z0-9][a-z0-9-]{0,63}$/.test(value.slice(prefix.length)))
    throw new Error('InvalidCommand')
  return value
}

function key(value) {
  if (typeof value !== 'string' || !/^[a-zA-Z0-9._~:-]{1,512}$/.test(value)
      || value === '.' || value === '..') throw new Error('InvalidCommand')
  return value
}

function name(value) {
  if (typeof value !== 'string' || !/^[a-zA-Z0-9_-]{1,64}$/.test(value)) throw new Error('InvalidCommand')
  return value
}

function session(actor) {
  const current = sessions.get(actor.role)
  if (!current) throw new Error('LoginRequired')
  return current
}

function endpoint(actor, method, parameters) {
  const url = new URL(`/xrpc/${method}`, actor.pds)
  for (const [field, value] of Object.entries(parameters ?? {})) url.searchParams.set(field, value)
  return url
}

async function request(url, { method = 'GET', body, headers = {} } = {}) {
  const response = await fetch(url, {
    method, redirect: 'error', signal: AbortSignal.timeout(15000),
    headers: { accept: 'application/json', ...(body ? { 'content-type': 'application/json' } : {}), ...headers },
    ...(body ? { body: JSON.stringify(body) } : {}),
  })
  const raw = await response.text()
  if (Buffer.byteLength(raw) > maximumBytes) throw new Error('ResponseTooLarge')
  let value
  try { value = JSON.parse(raw) } catch { throw new Error('InvalidProtocolResponse') }
  return { status: response.status, ok: response.ok, value }
}

function errorBody(value) {
  return { error: typeof value?.error === 'string' && /^[a-zA-Z0-9._-]{1,80}$/.test(value.error)
    ? value.error : 'ProtocolRejected' }
}

function sourceBody(value) {
  // Deliberate whitelist: never forward arbitrary response fields or auth headers.
  const body = {}
  for (const field of ['uri', 'cid', 'validationStatus']) {
    if (typeof value?.[field] === 'string') body[field] = value[field]
  }
  if (value?.value && typeof value.value === 'object') {
    body.value = {}
    for (const field of ['$type', 'text', 'createdAt']) {
      if (typeof value.value[field] === 'string') body.value[field] = value.value[field]
    }
  }
  return body
}

function lifetime(current) {
  return { expiresAt: new Date(current.exp * 1000).toISOString(),
    expiresInSeconds: Math.max(0, current.exp - Math.floor(Date.now() / 1000)),
    lifetimeSeconds: current.exp - current.iat }
}

async function run(command) {
  if (!command || typeof command !== 'object' || Array.isArray(command)) throw new Error('InvalidCommand')
  switch (command.command ?? command.op) {
    case 'login': {
      const actor = account(command.role)
      sessions.delete(actor.role)
      const result = await request(endpoint(actor, 'com.atproto.server.createSession'), {
        method: 'POST', body: { identifier: actor.handle, password: actor.password },
      })
      if (!result.ok) return { command: 'login', role: actor.role, status: result.status, body: errorBody(result.value) }
      if (result.value.did !== actor.did || typeof result.value.accessJwt !== 'string') throw new Error('InvalidProtocolResponse')
      sessions.set(actor.role, result.value.accessJwt)
      return { command: 'login', role: actor.role, status: result.status, authenticated: true,
        authentication: 'legacy-createSession-disposable-fixture' }
    }
    case 'issue': {
      const actor = account(command.role)
      const requestedSpace = space(command.space)
      const credentialName = name(command.name)
      const delegation = await request(endpoint(actor, 'com.atproto.space.getDelegationToken', { space: requestedSpace }), {
        headers: { authorization: `Bearer ${session(actor)}` },
      })
      if (!delegation.ok) return { command: 'issue', name: credentialName, status: delegation.status, stage: 'delegation', body: errorBody(delegation.value) }
      if (typeof delegation.value.token !== 'string') throw new Error('InvalidProtocolResponse')
      const dpopKey = await JoseKey.generate(['ES256'])
      const url = endpoint(account('authority'), 'com.atproto.space.getSpaceCredential')
      const proof = await createDpopProof(dpopKey, { htm: 'POST', htu: url.toString() })
      const issued = await request(url, { method: 'POST', body: { space: requestedSpace },
        headers: { authorization: `Bearer ${delegation.value.token}`, dpop: proof } })
      if (!issued.ok) return { command: 'issue', name: credentialName, status: issued.status, stage: 'credential', body: errorBody(issued.value) }
      if (typeof issued.value.credential !== 'string') throw new Error('InvalidProtocolResponse')
      // Decode time claims for observation only. The PDS validates this credential during the subsequent read.
      const claims = JSON.parse(Buffer.from(issued.value.credential.split('.')[1], 'base64url').toString('utf8'))
      if (!Number.isSafeInteger(claims.exp) || !Number.isSafeInteger(claims.iat)) throw new Error('InvalidProtocolResponse')
      const current = { credential: issued.value.credential, key: dpopKey, exp: claims.exp, iat: claims.iat }
      credentials.set(credentialName, current)
      return { command: 'issue', name: credentialName, status: issued.status, issued: true, ...lifetime(current) }
    }
    case 'read': {
      const credentialName = name(command.name)
      const current = credentials.get(credentialName)
      if (!current) throw new Error('CredentialRequired')
      const author = account(command.repo)
      const url = endpoint(author, 'com.atproto.space.getRecord', {
        space: space(command.space), repo: author.did, collection: fixture.collection, rkey: key(command.rkey),
      })
      const proof = await createDpopProof(current.key, { htm: 'GET', htu: url.toString(), credential: current.credential })
      const result = await request(url, { headers: { authorization: `DPoP ${current.credential}`, dpop: proof } })
      return { command: 'read', name: credentialName, status: result.status,
        body: result.ok ? sourceBody(result.value) : errorBody(result.value), ...lifetime(current) }
    }
    case 'write': {
      const actor = account(command.role)
      const mode = command.mode ?? 'create'
      if (!['create', 'put'].includes(mode) || !command.record || typeof command.record !== 'object'
          || Array.isArray(command.record) || Buffer.byteLength(JSON.stringify(command.record)) > maximumBytes / 2)
        throw new Error('InvalidCommand')
      const result = await request(endpoint(actor, `com.atproto.space.${mode}Record`), {
        method: 'POST', headers: { authorization: `Bearer ${session(actor)}` },
        body: { space: space(command.space), repo: actor.did, collection: fixture.collection,
          rkey: key(command.rkey), record: command.record },
      })
      return { command: 'write', role: actor.role, mode, status: result.status,
        body: result.ok ? sourceBody(result.value) : errorBody(result.value) }
    }
    case 'check': {
      const actor = account(command.role)
      const user = account(command.user)
      const requestedSpace = space(command.space)
      const port = command.port
      if (!Number.isInteger(port) || port < 1024 || port > 65535
          || (command.tamper !== undefined && typeof command.tamper !== 'boolean')
          || (command.exp !== undefined && !Number.isSafeInteger(command.exp)))
        throw new Error('InvalidCommand')
      // Only the explicitly registered disposable host is a valid callback target.
      // Audience/method overrides affect the signed claims, never the destination.
      const origin = `http://host.docker.internal:${port}`
      const service = JSON.parse(await readFile(`/evidence/tangent-service-${port}.json`, 'utf8'))
      if (service.endpoint !== origin || (command.origin !== undefined && command.origin !== origin)
          || !/^did:plc:[a-z2-7]{24}#tangent$/.test(service.managingApp ?? ''))
        throw new Error('InvalidFixtureOrigin')
      const audience = command.aud ?? service.managingApp
      const method = command.method ?? 'com.atproto.simplespace.checkUserAccess'
      if (typeof audience !== 'string' || audience.length < 1 || audience.length > 2048
          || typeof method !== 'string' || !/^[a-zA-Z][a-zA-Z0-9.-]{1,254}$/.test(method))
        throw new Error('InvalidCommand')
      const parameters = { aud: audience, lxm: method,
        ...(command.exp === undefined ? {} : { exp: command.exp }) }
      const issued = await request(endpoint(actor, 'com.atproto.server.getServiceAuth', parameters), {
        headers: { authorization: `Bearer ${session(actor)}` },
      })
      if (!issued.ok) return { command: 'check', status: issued.status, stage: 'service-auth', body: errorBody(issued.value) }
      if (typeof issued.value.token !== 'string') throw new Error('InvalidProtocolResponse')
      let token = issued.value.token
      if (command.tamper) {
        const segments = token.split('.')
        if (segments.length !== 3) throw new Error('InvalidProtocolResponse')
        const signature = Buffer.from(segments[2], 'base64url')
        if (!signature.length) throw new Error('InvalidProtocolResponse')
        signature[0] ^= 1
        segments[2] = signature.toString('base64url')
        token = segments.join('.')
      }
      const url = new URL('/xrpc/com.atproto.simplespace.checkUserAccess', origin)
      url.searchParams.set('space', requestedSpace)
      url.searchParams.set('user', user.did)
      const result = await request(url, { headers: { authorization: `Bearer ${token}` } })
      if (result.ok && typeof result.value?.authorized !== 'boolean') throw new Error('InvalidProtocolResponse')
      return { command: 'check', status: result.status, stage: 'callback',
        body: result.ok ? { authorized: result.value.authorized } : errorBody(result.value) }
    }
    default: throw new Error('InvalidCommand')
  }
}

const safeErrors = new Set(['InvalidCommand', 'InvalidFixtureOrigin', 'LoginRequired', 'CredentialRequired', 'ResponseTooLarge', 'InvalidProtocolResponse'])
for await (const line of createInterface({ input: process.stdin, crlfDelay: Infinity })) {
  if (!line.trim()) continue
  try {
    if (Buffer.byteLength(line) > maximumBytes) throw new Error('InvalidCommand')
    let command
    try { command = JSON.parse(line) } catch { throw new Error('InvalidCommand') }
    process.stdout.write(JSON.stringify(await run(command)) + '\n')
  } catch (failure) {
    process.stdout.write(JSON.stringify({ status: 400,
      error: safeErrors.has(failure?.message) ? failure.message : 'ProtocolRequestFailed' }) + '\n')
  }
}
