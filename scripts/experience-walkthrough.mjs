#!/usr/bin/env node
// Live walkthrough of the experience API and the local Rust connector against the real
// Tangent server. Signs in the existing disposable-network agent fixture (sign-in only:
// no server ownership claim), mints a scoped participant credential, verifies the shared
// browser/bearer boundary, then exercises the connector's CLI and MCP stdio intakes.
// Secrets (cookies, tokens) stay under ignored .local and never enter the receipt.
import { spawn } from 'node:child_process'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { setTimeout as pause } from 'node:timers/promises'

const root = resolve(fileURLToPath(new URL('..', import.meta.url)))
const origin = 'http://127.0.0.1:5220'
const directory = join(root, '.local', 'experience-walkthrough')
const connector = join(root, 'src', 'server', 'mcp', 'target', 'release',
  process.platform === 'win32' ? 'tangent-connector.exe' : 'tangent-connector')
const receipt = { status: 'live walkthrough', date: new Date().toISOString(), origin, steps: [], limits: [] }

const step = (name, detail) => receipt.steps.push({ name, ...detail })
const fail = message => { throw new Error(message) }

async function command(executable, args, options = {}) {
  return new Promise((resolveResult, reject) => {
    const child = spawn(executable, args, { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'], ...options })
    let stdout = ''; let stderr = ''
    child.stdout.on('data', chunk => { if (stdout.length < 262144) stdout += chunk })
    child.stderr.on('data', chunk => { if (stderr.length < 65536) stderr += chunk })
    child.on('error', error => reject(error))
    child.on('close', code => resolveResult({ code, stdout, stderr }))
  })
}

async function request(path, { method = 'GET', bearer, cookie, body } = {}) {
  const response = await fetch(origin + path, {
    method, redirect: 'manual', signal: AbortSignal.timeout(30000),
    headers: {
      accept: 'application/json',
      ...(bearer === undefined ? {} : { authorization: `Bearer ${bearer}` }),
      ...(cookie ? { cookie } : {}),
      ...(body === undefined ? {} : { origin, 'content-type': 'application/json' })
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  })
  let json = null
  try { json = await response.json() } catch {}
  return { status: response.status, json }
}

// A minimal stdio JSON-RPC peer for the MCP intake.
class ConnectorPeer {
  constructor(process_) { this.process = process_; this.nextId = 1; this.pending = new Map()
    let buffer = ''
    this.process.stdout.on('data', chunk => {
      buffer += chunk
      let index
      while ((index = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, index); buffer = buffer.slice(index + 1)
        if (!line.trim()) continue
        const message = JSON.parse(line)
        if (message.id !== undefined && this.pending.has(message.id)) {
          this.pending.get(message.id)(message); this.pending.delete(message.id)
        }
      }
    })
  }
  call(method, params) {
    const id = this.nextId++
    this.process.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n')
    return new Promise((resolveResult, reject) => {
      this.pending.set(id, resolveResult)
      setTimeout(() => { if (this.pending.delete(id)) reject(new Error(`MCP call timed out: ${method}`)) }, 20000)
    })
  }
  notify(method, params) { this.process.stdin.write(JSON.stringify({ jsonrpc: '2.0', method, params }) + '\n') }
  close() { this.process.stdin.end(); this.process.kill() }
}

await mkdir(directory, { recursive: true })
const fixtures = JSON.parse(await readFile(join(root, '.local', 'spaces-network', 'fixtures.json'), 'utf8'))
if (fixtures.status !== 'ready') fail('The disposable Spaces network is not running')
const agent = fixtures.accounts.find(account => account.role === 'agent')
if (!agent) fail('No agent fixture in the network')

// 1. Readiness.
const ready = await request('/health/ready')
if (ready.status !== 200) fail('The Tangent server is not ready')
step('server-ready', {})

// 2. Real fixture OAuth for the agent: verified sign-in, no ownership claim.
const cookieFile = join(directory, 'agent.cookies.json')
const oauth = await command(process.execPath, ['probes/scripts/oauth-flow.mjs',
  '--accounts', join(root, '.local', 'spaces-network', 'fixtures.json'),
  '--account', 'agent', '--web', '--probe', origin, '--use-handle', '--cookie-file', cookieFile])
if (oauth.code !== 0 || !JSON.parse(oauth.stdout || '{}').passed) fail(`Fixture OAuth failed: ${oauth.stderr.slice(0, 400)}`)
const saved = JSON.parse(await readFile(cookieFile, 'utf8'))
if (saved.did !== agent.did) fail('Signed-in identity differs from the fixture')
const cookie = saved.cookies.map(([name, value]) => `${name}=${value}`).join('; ')
step('fixture-oauth', { did: agent.did, ownershipClaimed: false })

// 3. Browser session observes the site.
const site = await request('/api/site', { cookie })
if (site.status !== 200 || site.json?.participant?.did !== agent.did) fail('Browser arrival failed')
const ownerClaimed = Boolean(site.json?.participant?.isOwner)
step('browser-arrival', { did: site.json.participant.did, ownerClaimed })

// 4. Mint a scoped participant credential through the browser session.
const issued = await request('/api/participation/credentials', {
  method: 'POST', cookie, body: { name: 'Experience connector walkthrough', lifetimeDays: 2 }
})
if (issued.status !== 200 || issued.json?.credential?.did !== agent.did) fail(`Credential mint failed (HTTP ${issued.status})`)
const token = issued.json.token
await writeFile(join(directory, 'agent-credential.json'), JSON.stringify(issued.json, null, 2) + '\n', { mode: 0o600 })
await writeFile(join(directory, 'token.txt'), token + '\n', { mode: 0o600 })
step('credential-minted', { did: issued.json.credential.did, lifetimeDays: 2, storage: '.local/experience-walkthrough (gitignored)' })

// 5. Shared boundary checks on the experience API.
const bearerArrival = await request('/api/v1/experience', { bearer: token })
if (bearerArrival.status !== 200 || bearerArrival.json?.experienceVersion !== '1.0') fail(`Bearer arrival failed (HTTP ${bearerArrival.status})`)
if (bearerArrival.json?.identity?.did !== agent.did) fail('Bearer identity differs')
step('bearer-arrival', {
  experienceVersion: bearerArrival.json.experienceVersion,
  identityDid: bearerArrival.json.identity.did,
  bytes: JSON.stringify(bearerArrival.json).length,
  tangents: bearerArrival.json.result?.data?.tangents?.length ?? null
})

const cookieArrival = await request('/api/v1/experience', { cookie })
if (cookieArrival.status !== 200 || cookieArrival.json?.identity?.did !== agent.did) fail('Cookie arrival failed')
step('cookie-arrival-same-actor', { identityDid: cookieArrival.json.identity.did })

const invalidBearer = await request('/api/v1/experience', { bearer: 'ts_invalid_walkthrough_probe', cookie })
if (invalidBearer.status === 200) fail('An invalid bearer fell back to the browser cookie')
step('no-cookie-fallback', { status: invalidBearer.status })

const anonymous = await request('/api/v1/experience', {})
step('anonymous-rejected', { status: anonymous.status })

const updates = await request('/api/v1/experience/updates', { bearer: token })
if (updates.status !== 200) fail(`Updates failed (HTTP ${updates.status})`)
step('digest-read', {
  waiting: updates.json?.attention?.waitingCount?.value ?? null,
  activity: updates.json?.attention?.newActivityCount?.value ?? null,
  checkpointReturned: Boolean(updates.json?.continuation?.activityCheckpoint)
})

// 6. Connector, CLI intake: enrollment and the participation tools.
const home = join(directory, 'connector-home')
await mkdir(home, { recursive: true })
const connectorEnvironment = { ...process.env, TANGENT_CONNECTOR_HOME: home }
const enrollment = await command(connector, ['enroll', '--name', 'walkthrough', '--server', origin,
  '--credential-file', join(directory, 'token.txt')], { env: connectorEnvironment })
if (enrollment.code !== 0) fail(`Enrollment failed: ${enrollment.stderr.slice(0, 400)}`)
step('connector-enrolled', { output: enrollment.stdout.trim().split('\n')[0], intake: 'cli' })

const cliCalls = []
const companions = await command(connector, ['companions', '--json'], { env: connectorEnvironment })
const companionId = JSON.parse(companions.stdout)[0]?.companionId
if (!companionId) fail('No companion listed after enrollment')
const select = await command(connector, ['call', 'SelectCompanion', JSON.stringify({ moniker: 'walkthrough' })], { env: connectorEnvironment })
cliCalls.push({ tool: 'SelectCompanion', code: select.code, text: select.stdout.trim() })
const arrive = await command(connector, ['call', 'Arrive', JSON.stringify({ companionId, serverUrl: origin })], { env: connectorEnvironment })
if (arrive.code !== 0) fail(`CLI arrival failed: ${arrive.stdout}${arrive.stderr}`)
const contextId = JSON.parse((await command(connector, ['call', 'Arrive', JSON.stringify({ companionId, serverUrl: origin }), '--json'], { env: connectorEnvironment })).stdout)
  .connector?.contextId
cliCalls.push({ tool: 'Arrive', code: arrive.code, text: arrive.stdout.trim() })
const updatesCall = await command(connector, ['call', 'GetUpdates', JSON.stringify({ contextId })], { env: connectorEnvironment })
cliCalls.push({ tool: 'GetUpdates', code: updatesCall.code, text: updatesCall.stdout.trim() })
step('connector-cli-intake', { calls: cliCalls.map(call => ({ tool: call.tool, exit: call.code, bytes: call.text.length })) })

// 7. Connector, MCP intake: the real stdio transport.
const serve = spawn(connector, ['serve'], { env: connectorEnvironment, windowsHide: true, stdio: ['pipe', 'pipe', 'ignore'] })
const peer = new ConnectorPeer(serve)
const initialize = await peer.call('initialize', {
  protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'experience-walkthrough', version: '1' }
})
peer.notify('notifications/initialized', {})
const tools = await peer.call('tools/list', {})
const toolNames = (tools.result?.tools ?? []).map(tool => tool.name)
const mcpSelect = await peer.call('tools/call', { name: 'SelectCompanion', arguments: { moniker: 'walkthrough' } })
const mcpCompanion = mcpSelect.result?.structuredContent?.connector?.companionId
const mcpArrive = await peer.call('tools/call', { name: 'Arrive', arguments: { companionId: mcpCompanion, serverUrl: origin } })
const mcpContext = mcpArrive.result?.structuredContent?.connector?.contextId
const mcpUpdates = await peer.call('tools/call', { name: 'GetUpdates', arguments: { contextId: mcpContext, view: 'expanded' } })
const ping = await peer.call('ping', {})
peer.close()
const mcpText = mcpArrive.result?.content?.[0]?.text ?? ''
step('connector-mcp-intake', {
  negotiated: initialize.result?.protocolVersion,
  server: initialize.result?.serverInfo,
  toolCount: toolNames.length,
  arrivalTextBytes: mcpText.length,
  arrivalView: mcpArrive.result?.structuredContent?.connector?.view,
  updatesIsError: mcpUpdates.result?.isError,
  pingResult: JSON.stringify(ping.result),
  deliveryMode: mcpArrive.result?.structuredContent?.connector?.deliveryMode
})
if (initialize.result?.protocolVersion !== '2025-06-18') fail('Protocol negotiation mismatch')
if (toolNames.length !== 12) fail(`Expected 12 tools, saw ${toolNames.length}`)
if (!mcpText.includes('you are participating as')) fail('Orientation view missing the you-identity line')

// 8. Clean up the platform credential entry; the minted token stays in .local for reuse.
const forget = await command(connector, ['forget', '--name', 'walkthrough'], { env: connectorEnvironment })
step('connector-forgotten', { code: forget.code })

receipt.limits.push(
  'No ownership was claimed and no Tangent/Topic was created: the server was left unclaimed for the owner, so the walkthrough could not include a genuine human/agent Topic exchange or a native-source Post.',
  'The MCP peer in this walkthrough is a scripted stdio JSON-RPC client, not a named agent host; no automatic-wake claim is made.',
  'The minted credential expires after 2 days; it is stored under .local/experience-walkthrough for reuse.'
)
const receiptPath = join(root, 'docs', 'evidence', 'experience-connector-walkthrough.json')
await writeFile(receiptPath, JSON.stringify(receipt, null, 2) + '\n')
console.log(JSON.stringify(receipt, null, 2))
console.log(`receipt: ${receiptPath}`)
