// Disposable protocol fixture. The PDS, PLC signatures, OAuth and Spaces
// handlers are upstream implementations; Tangent policy is the small callback.
import { randomBytes } from 'node:crypto'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { createServer as httpServer } from 'node:http'
import { createServer as tcpServer, connect } from 'node:net'
import { TestPds } from '../packages/dev-env/dist/pds.js'
import { TestPlc } from '../packages/dev-env/dist/plc.js'
import { mockNetworkUtilities } from '../packages/dev-env/dist/util.js'
import { LexiconAuthorityProfile } from '../packages/dev-env/dist/service-profile-lexicon.js'
import { Secp256k1Keypair } from '../packages/crypto/dist/index.js'
import { JoseKey } from '../packages/oauth/oauth-provider/dist/oauth-provider.js'
import { createDpopProof } from '../packages/space/dist/index.js'
import { verifyJwt } from '../packages/xrpc-server/dist/auth.js'
import { com } from '../packages/pds/dist/lexicons/index.js'

const REVISION = 'c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae'
const outDir = process.env.PROBE_OUTPUT ?? '/evidence'
const password = () => randomBytes(24).toString('base64url')
await mkdir(outDir, { recursive: true })
const startedAt = new Date().toISOString()
// A failed fresh launch must never leave previous-run credentials looking live.
await writeFile(`${outDir}/fixtures.json`, JSON.stringify({ status: 'starting', startedAt, accounts: [] }), { mode: 0o600 })
await writeFile(`${outDir}/baseline-evidence.json`, JSON.stringify({ status: 'starting', startedAt, revision: REVISION }))

// Identical localhost origins are visible inside the container and on the host.
// This forward is transport only; no headers, state, proofs or tokens change.
const callbackForward = tcpServer((socket) => {
  const upstream = connect(5180, 'host.docker.internal')
  socket.pipe(upstream).pipe(socket)
  socket.on('error', () => upstream.destroy())
  upstream.on('error', () => socket.destroy())
})
callbackForward.listen(5180, '0.0.0.0')

const plc = await TestPlc.create({ port: 2582 })
const pds1 = await TestPds.create({ port: 2583, hostname: 'localhost', didPlcUrl: plc.url, enableDidDocWithSession: true })
const pds2 = await TestPds.create({ port: 2584, hostname: 'localhost', didPlcUrl: plc.url, serviceHandleDomains: ['.test2'], enableDidDocWithSession: true })
mockNetworkUtilities([pds1, pds2])
const lexUser = { handle: 'lex-authority.test', email: 'lex-authority@test.invalid', password: password() }
const lex = await LexiconAuthorityProfile.create(pds1, lexUser)
for (const pds of [pds1, pds2]) pds.ctx.cfg.lexicon.didAuthority = lex.did
await lex.createRecords()

// Clearly local namespace, resolved through the test PDS's explicit authority.
const spaceType = 'local.tangent.room'
const collection = 'local.tangent.message'
for (const id of [spaceType, collection]) {
  const doc = JSON.parse(await readFile(new URL(`./lexicons/${id}.json`, import.meta.url), 'utf8'))
  await lex.agent.com.atproto.repo.createRecord({ repo: lex.did, collection: 'com.atproto.lexicon.schema', rkey: doc.id, record: doc })
}

const actors = {}
for (const [role, pds, suffix] of [
  ['authority', pds1, 'test'], ['owner', pds1, 'test'], ['manager', pds1, 'test'],
  ['agent', pds2, 'test2'], ['outsider', pds2, 'test2'],
]) {
  const account = { handle: `tangent-${role}.${suffix}`, email: `${role}@test.invalid`, password: password() }
  const result = await pds.getAgent().createAccount(account)
  actors[role] = { role, ...account, did: result.data.did, pds: pds.url, client: pds.getClient(), headers: { authorization: `Bearer ${result.data.accessJwt}` } }
}
const safeActor = ({ role, handle, did, pds }) => ({ role, handle, did, pds })
const fixtures = { status: 'ready', startedAt, revision: REVISION, plc: plc.url, pds1: pds1.url, pds2: pds2.url, lexiconAuthority: lex.did, spaceType, collection, accounts: Object.values(actors).map(({ client, headers, ...a }) => a) }
await writeFile(`${outDir}/fixtures.json`, JSON.stringify(fixtures, null, 2), { mode: 0o600 })

// Service identity registered through the actual local PLC endpoint. Import
// @did-plc/lib through dev-env's workspace dependency resolution.
const { createRequire } = await import('node:module')
const require = createRequire(new URL('../packages/dev-env/package.json', import.meta.url))
const plcLib = require('@did-plc/lib')
const serviceKey = await Secp256k1Keypair.create()
const op = await plcLib.signOperation({ type: 'plc_operation', rotationKeys: [serviceKey.did()], alsoKnownAs: [], verificationMethods: {}, services: { tangent: { type: 'AtprotoSpaceService', endpoint: 'http://localhost:2585' } }, prev: null }, serviceKey)
const serviceDid = await plcLib.didForCreateOp(op)
await pds1.ctx.plcClient.sendOperation(serviceDid, op)
const managingApp = `${serviceDid}#tangent`
const memberships = new Map()
const callbackCalls = []
let evidence = { revision: REVISION, status: 'starting', accounts: Object.values(actors).map(safeActor) }
const helper = httpServer(async (req, res) => {
  const url = new URL(req.url ?? '/', 'http://localhost:2585')
  const send = (status, body) => { res.writeHead(status, { 'content-type': 'application/json' }); res.end(JSON.stringify(body)) }
  if (url.pathname === '/health') return send(200, { status: evidence.status, revision: REVISION })
  if (url.pathname === '/evidence') return send(200, evidence)
  if (url.pathname !== '/xrpc/com.atproto.simplespace.checkUserAccess' || req.method !== 'GET') return send(404, { error: 'NotFound' })
  const space = url.searchParams.get('space')
  const user = url.searchParams.get('user')
  try {
    const bearer = req.headers.authorization
    if (!bearer?.startsWith('Bearer ')) return send(401, { error: 'AuthenticationRequired' })
    const claims = await verifyJwt(bearer.slice(7), managingApp, 'com.atproto.simplespace.checkUserAccess', (did, refresh) => pds1.ctx.idResolver.did.resolveAtprotoKey(did, refresh))
    if (claims.iss !== actors.authority.did || !space?.startsWith(`at://${claims.iss}/space/`)) return send(403, { error: 'WrongAuthority' })
    const authorized = memberships.get(space)?.has(user) === true
    callbackCalls.push({ space, user, issuer: claims.iss, authorized })
    send(200, { authorized })
  } catch { send(401, { error: 'InvalidServiceAuthentication' }) }
})
await new Promise((resolve) => helper.listen(2585, '0.0.0.0', resolve))

const call = (actor, method, params) => actor.client.call(method, params, { headers: actor.headers })
const rooms = {}
for (const [name, allowed] of [['lounge', Object.values(actors)], ['workshop', [actors.owner, actors.manager, actors.agent]]]) {
  const space = `at://${actors.authority.did}/space/${spaceType}/${name}`
  memberships.set(space, new Set(allowed.map(a => a.did)))
  const result = await call(actors.authority, com.atproto.simplespace.createSpace, { type: spaceType, skey: name, policy: com.atproto.simplespace.defs.managingAppPolicy.build({ managingApp }), appAccess: com.atproto.simplespace.defs.open.build({}) })
  if (result.uri !== space) throw new Error('Unexpected space authority')
  rooms[name] = space
}
fixtures.rooms = rooms
fixtures.managingApp = managingApp
await writeFile(`${outDir}/fixtures.json`, JSON.stringify(fixtures, null, 2), { mode: 0o600 })

async function credential(actor, space) {
  const delegated = await call(actor, com.atproto.space.getDelegationToken, { space })
  const key = await JoseKey.generate(['ES256'])
  const proof = await createDpopProof(key, { htm: 'POST', htu: `${pds1.url}/xrpc/com.atproto.space.getSpaceCredential` })
  const result = await pds1.getClient().call(com.atproto.space.getSpaceCredential, { space }, { headers: { authorization: `Bearer ${delegated.token}`, dpop: proof } })
  return { credential: result.credential, key }
}
async function readWithCredential(cred, space, author, rkey) {
  const url = new URL('/xrpc/com.atproto.space.getRecord', author.pds)
  for (const [k, v] of Object.entries({ space, repo: author.did, collection, rkey })) url.searchParams.set(k, v)
  const proof = await createDpopProof(cred.key, { htm: 'GET', htu: url.toString(), credential: cred.credential })
  const response = await fetch(url, { headers: { authorization: `DPoP ${cred.credential}`, dpop: proof } })
  return { status: response.status, body: await response.json() }
}
try {
  const message = { $type: collection, text: 'Hello from the second PDS.', createdAt: new Date().toISOString() }
  const written = await call(actors.agent, com.atproto.space.createRecord, { space: rooms.workshop, repo: actors.agent.did, collection, rkey: 's01-message', record: message })
  const memberCred = await credential(actors.manager, rooms.workshop)
  const read = await readWithCredential(memberCred, rooms.workshop, actors.agent, 's01-message')
  if (read.status !== 200 || read.body.cid !== written.cid || read.body.value.text !== message.text) throw new Error('Cross-PDS read did not match source')
  let outsiderDenied = false
  let outsiderError
  try { await credential(actors.outsider, rooms.workshop) } catch (err) { outsiderError = err.error; outsiderDenied = err.error === 'UserNotAuthorized' }
  if (!outsiderDenied) throw new Error('Outsider credential request did not fail as expected')
  const loungeCred = await credential(actors.outsider, rooms.lounge)
  const wrongRoom = await readWithCredential(loungeCred, rooms.workshop, actors.agent, 's01-message')
  if (wrongRoom.status < 400) throw new Error('Lounge credential read Workshop')
  const unauthCallback = await fetch(`http://localhost:2585/xrpc/com.atproto.simplespace.checkUserAccess?space=${encodeURIComponent(rooms.workshop)}&user=${actors.agent.did}`)
  if (unauthCallback.status !== 401) throw new Error('Unauthenticated callback accepted')
  const claims = JSON.parse(Buffer.from(memberCred.credential.split('.')[1], 'base64url').toString('utf8'))
  memberships.get(rooms.workshop).delete(actors.manager.did)
  let removedDenied = false
  try { await credential(actors.manager, rooms.workshop) } catch (err) { removedDenied = err.error === 'UserNotAuthorized' }
  const afterRemoval = await readWithCredential(memberCred, rooms.workshop, actors.agent, 's01-message')
  memberships.get(rooms.workshop).add(actors.manager.did)
  evidence = { ...evidence, status: 'passed', completedAt: new Date().toISOString(), authentication: 'Legacy password sessions for fixture setup and this baseline; OAuth remains a separate required probe.', rooms, managingApp, lexiconAuthority: lex.did, checks: { crossPdsRead: { status: read.status, author: actors.agent.did, reader: actors.manager.did, sourceUri: written.uri, cid: written.cid, validationStatus: written.validationStatus }, outsiderCredential: { denied: outsiderDenied, error: outsiderError }, wrongRoomCredential: { status: wrongRoom.status, error: wrongRoom.body.error }, unsignedManagingCallback: { status: unauthCallback.status }, removal: { newCredentialDenied: removedDenied, oldCredentialReadStatus: afterRemoval.status, credentialLifetimeSeconds: claims.exp - claims.iat } }, callbackCalls }
  if (!removedDenied || afterRemoval.status !== 200) throw new Error('Unexpected post-removal credential behavior')
} catch (err) {
  evidence = { ...evidence, status: 'failed', error: err.error ?? err.message, callbackCalls }
}
await writeFile(`${outDir}/baseline-evidence.json`, JSON.stringify(evidence, null, 2))
console.log(JSON.stringify({ status: evidence.status, revision: REVISION, plc: plc.url, pds1: pds1.url, pds2: pds2.url, helper: 'http://localhost:2585', fixtureFile: `${outDir}/fixtures.json` }))

async function shutdown() {
  helper.close()
  callbackForward.close()
  await Promise.allSettled([pds1.close(), pds2.close()])
  await plc.close()
  process.exit(0)
}
process.on('SIGTERM', shutdown)
process.on('SIGINT', shutdown)
