// Two real Spaces, existing Participants, genuine OAuth, and one actual model
// conversation. All cookies, credentials, prompts and context stay under .local.
import { spawn } from 'node:child_process'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { resolve, join, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { ParticipantClient } from '../clients/participant/client.mjs'

async function jsonFile(path, label) {
  try { return JSON.parse(await readFile(path, 'utf8')) }
  catch (error) { const safe = new Error(`Unable to read ${label}`); safe.code = error.code; throw safe }
}
const root = resolve(fileURLToPath(new URL('..', import.meta.url)))
const options = Object.fromEntries(Array.from({ length: (process.argv.length - 2) / 2 }, (_, index) =>
  [process.argv[2 + index * 2].slice(2), process.argv[3 + index * 2]]))
if (options.origin !== 'http://127.0.0.1:5220') throw new Error('This clean demo is limited to the default local site on port 5220')
for (const key of ['fixtures', 'directory', 'powershell']) if (!options[key]) throw new Error(`Missing --${key}`)
const origin = options.origin
const directory = resolve(options.directory)
if (!directory.startsWith(resolve(root, '.local') + sep)) throw new Error('Demo artifacts must remain in ignored .local')
await mkdir(directory, { recursive: true })
const fixtures = await jsonFile(options.fixtures, 'the private network fixture')
if (fixtures.status !== 'ready') throw new Error('The disposable network is not ready')
const accounts = Object.fromEntries(fixtures.accounts.map(account => [account.role, account]))
const networkId = String(Date.parse(fixtures.startedAt))
const rooms = { lounge: 'tangent-lounge', workshop: 'tangent-workshop' }
const contextPath = join(directory, 'context.json')
let state
try { state = await jsonFile(contextPath, 'the private demo context') } catch (error) { if (error.code !== 'ENOENT') throw error }
state ??= { site: origin, room: rooms.workshop, rooms, networkId, agentDid: accounts.agent.did,
  ownerDid: accounts.owner.did, connected: {}, arrivals: {}, completed: false }
if (state.site !== origin || state.networkId !== networkId || state.agentDid !== accounts.agent.did) throw new Error('Demo context belongs to another identity network')
if (options.reconnect === 'true') state.connected = {}
const save = () => writeFile(contextPath, JSON.stringify(state, null, 2) + '\n', { mode: 0o600 })
const cookiePath = role => join(directory, `${role}.cookies.json`)
const cookies = {}
let stage = 'readiness'
async function command(executable, args) {
  return new Promise((resolveResult, reject) => {
    const child = spawn(executable, args, { cwd: root, shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'ignore'] })
    let stdout = ''; let size = 0
    child.stdout.on('data', chunk => { size += chunk.length; if (size <= 1024 * 1024) stdout += chunk })
    child.on('error', () => reject(new Error('Unable to start the required local command')))
    child.on('close', code => {
      if (code !== 0 || size > 1024 * 1024) return reject(new Error('Required local command failed; no credential-bearing output was logged'))
      try { resolveResult(JSON.parse(stdout)) } catch { reject(new Error('Local command did not return its expected JSON result')) }
    })
  })
}
async function loadCookie(role) {
  const saved = await jsonFile(cookiePath(role), 'a private browser cookie fixture')
  if (saved.origin !== origin || saved.did !== accounts[role].did) throw new Error('Saved cookie identity or origin differs')
  cookies[role] = saved.cookies.map(([name, value]) => `${name}=${value}`).join('; ')
}
async function request(role, method, path, body) {
  const response = await fetch(origin + path, { method, redirect: 'manual', signal: AbortSignal.timeout(90000), headers: {
    accept: 'application/json', ...(role ? { cookie: cookies[role] } : {}),
    ...(body === undefined ? {} : { origin, 'content-type': 'application/json' })
  }, body: body === undefined ? undefined : JSON.stringify(body) })
  let json = null
  try { json = await response.json() } catch {}
  return { status: response.status, json }
}
async function login(role, connection) {
  stage = `real OAuth for ${role}${connection ? ' ' + connection : ''}`
  const args = ['probes/scripts/oauth-flow.mjs', '--accounts', resolve(options.fixtures), '--account', role,
    '--web', '--probe', origin, '--use-handle', '--cookie-file', cookiePath(role)]
  if (connection) args.push('--start-path', `/api/connections/${connection}`, '--initial-cookies', cookiePath(connection === 'authority' ? 'owner' : role))
  const result = await command(process.execPath, args)
  if (!result.passed) throw new Error('Real local OAuth did not complete')
  await loadCookie(role)
  if (connection) { state.connected[role] = connection; await save() }
}
async function admin(role, method, path, body) {
  const result = await request(role, method, path, body)
  if (result.status !== 200 || !result.json?.accepted) throw new Error(`Demo administration failed with HTTP ${result.status}`)
  return result.json
}
async function describe(role, room) {
  const result = await request(role, 'GET', `/api/rooms/${room}`)
  if (result.status !== 200) throw new Error('Demo room description is unavailable')
  return result.json
}
try {
  const ready = await request(null, 'GET', '/health/ready')
  if (ready.status !== 200) throw new Error('The latest default application must be running')
  const listing = await request(null, 'GET', '/api/rooms')
  if (listing.status !== 200 || listing.json?.nextPage || !Array.isArray(listing.json?.rooms)
    || listing.json.rooms.some(room => !Object.values(rooms).includes(room.key)))
    throw new Error('Default site already contains non-demo rooms; this script does not delete or rename them')
  for (const role of ['owner', 'manager', 'agent', 'outsider']) {
    let arrived
    try { await loadCookie(role); arrived = await request(role, 'GET', '/api/site') } catch {}
    if (arrived?.json?.participant?.did !== accounts[role].did) { delete state.connected[role]; await login(role); arrived = await request(role, 'GET', '/api/site') }
    const participant = arrived.json?.participant
    if (!participant || participant.did !== accounts[role].did || (role === 'owner' && !participant.isOwner)) throw new Error('Verified participant arrival failed')
    if (state.arrivals[role] && state.arrivals[role] !== participant.joinedAt) throw new Error('Participant arrival continuity changed')
    state.arrivals[role] = participant.joinedAt; await save()
    if (state.connected[role] !== 'rooms') await login(role, 'rooms')
  }
  if (state.connected.authority !== 'authority') await login('authority', 'authority')
  stage = 'create and provision the two real rooms'
  for (const [key, title, admission, topic] of [
    [rooms.lounge, 'Lounge', 'SignedIn', 'A place to say hello, compare notes, and follow a conversation at your own pace.'],
    [rooms.workshop, 'Workshop', 'InvitationOnly', 'Explore ideas together: ask a question, try something, and leave room for a tangent.']
  ]) {
    const existing = await request('owner', 'GET', `/api/rooms/${key}`)
    if (existing.status === 404) await admin('owner', 'POST', '/api/rooms', { key, title, admission })
    else if (existing.status !== 200 || existing.json.title !== title || existing.json.admission !== admission)
      throw new Error('Existing fixed-key demo room differs from its declared setup')
    const current = await describe('owner', key)
    if (current.spaceState !== 'Ready') await admin('owner', 'POST', `/api/rooms/${key}/provision`, {})
    if ((await describe('owner', key)).spaceUri !== `at://${accounts.authority.did}/space/local.tangent.room/${key}`)
      throw new Error('Demo room did not map to its exact real Space')
    if (current.topic !== topic) await admin('owner', 'PUT', `/api/rooms/${key}/topic`, { topic })
  }
  stage = 'delegate workshop administration and invite the agent'
  await admin('owner', 'PUT', `/api/rooms/${rooms.workshop}/members/${encodeURIComponent(accounts.manager.did)}`, { role: 'Manager' })
  await admin('manager', 'PUT', `/api/rooms/${rooms.workshop}/members/${encodeURIComponent(accounts.agent.did)}`, { role: 'Member' })
  const outsider = await describe('outsider', rooms.workshop)
  if (outsider.canRead || outsider.canWrite) throw new Error('The outsider must remain uninvited to Workshop')
  const agent = await describe('agent', rooms.workshop)
  if (!agent.canRead || !agent.canWrite || agent.canManage) throw new Error('Agent must have Member access without management')
  stage = 'seed one stable human conversation fixture'
  state.operation ??= { operationId: `demo-human-${networkId}`,
    text: 'Welcome to the workshop. What is one small detail that helps a person or an agent feel invited into an ongoing conversation? Keep it practical and reply in two short sentences.' }
  await save()
  const human = await request('owner', 'POST', `/api/rooms/${rooms.workshop}/messages`, state.operation)
  if (human.status !== 200 || human.json?.state !== 'accepted') throw new Error(`Human source is not accepted (HTTP ${human.status}); rerun retains its operation ID`)
  state.human = { ...human.json, text: state.operation.text }; await save()
  if (options['human-only'] !== 'true') {
    stage = 'enroll or reuse the existing agent credential'
    const credentialPath = join(directory, 'agent-credential.json')
    let credential
    try {
      credential = await jsonFile(credentialPath, 'the private agent credential')
      if ((await new ParticipantClient(origin, credential.token).welcome()).participant?.did !== accounts.agent.did) credential = null
    } catch { credential = null }
    if (!credential) {
      const issued = await request('agent', 'POST', '/api/participation/credentials', { name: 'Workshop participant', lifetimeDays: 7 })
      if (issued.status !== 200 || issued.json?.credential?.did !== accounts.agent.did) throw new Error('Demo enrollment did not bind the verified agent')
      credential = issued.json
      await writeFile(credentialPath, JSON.stringify(credential, null, 2), { mode: 0o600 })
    }
    const modelDirectory = join(directory, 'model')
    await mkdir(modelDirectory, { recursive: true })
    const commandPath = join(directory, 'model-command.json')
    await writeFile(commandPath, JSON.stringify({ executable: options.powershell, args: ['-NoLogo', '-NoProfile', '-File',
      join(root, 'scripts', 'demo-model-adapter.ps1'), '-OutputDirectory', modelDirectory] }, null, 2))
    if (!state.agentReply) {
    stage = 'actual model participant turn'
    const result = await command(process.execPath, ['clients/participant/client.mjs', 'watch', '--site', origin,
      '--credential-file', credentialPath, '--room', rooms.workshop, '--state', join(directory, 'agent-state.json'),
      '--command-file', commandPath, '--once'])
    if (result.kind === 'pending') throw new Error('Agent source remains pending; rerun resumes the saved operation without another model call')
    state.agentPost = result; await save()
    const history = await request('owner', 'GET', `/api/rooms/${rooms.workshop}/messages`)
    const model = await jsonFile(join(modelDirectory, 'last-model-execution.json'), 'the model execution record')
    const ownHistory = await new ParticipantClient(origin, credential.token).read(rooms.workshop)
    const reply = [...(history.json?.messages ?? []), ...ownHistory.messages].find(message => message.authorDid === accounts.agent.did
      && message.content?.text === model.replyText && message.content?.replyTo?.uri === state.human.sourceUri
      && message.content?.replyTo?.cid === state.human.sourceCid)
    if (!reply || model.exitCode !== 0 || model.toolEvents !== 0) throw new Error('Actual model output did not match one accepted agent source reply')
    state.agentReply = { sourceUri: reply.sourceUri, sourceCid: reply.sourceCid, authorDid: reply.authorDid, text: reply.content.text,
      replyTo: reply.content.replyTo, model: model.model, completedAt: model.completedAt }
    state.modelDirectory = modelDirectory; state.modelCommand = commandPath
    }
  }
  state.completed = true; state.preparedAt = new Date().toISOString()
  state.links = { home: origin + '/', lounge: `${origin}/?room=${rooms.lounge}`, workshop: `${origin}/?room=${rooms.workshop}` }
  await save()
  console.log(JSON.stringify({ status: 'ready', origin, rooms, workshop: state.links.workshop,
    actualModelReply: !!state.agentReply, privateContext: contextPath }))
} catch (error) {
  state.completed = false; state.lastFailure = { stage, message: error.message, at: new Date().toISOString() }; await save()
  console.error(JSON.stringify({ status: 'failed', stage, message: error.message })); process.exitCode = 1
}
