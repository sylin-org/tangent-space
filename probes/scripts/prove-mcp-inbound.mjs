// Opt-in real MCP proof against the existing loopback-only disposable network.
// Passwords and tokens stay in process memory; only whitelisted evidence is saved.
import { readFile, writeFile, mkdir } from 'node:fs/promises'
import { resolve, dirname } from 'node:path'

const origin = 'http://127.0.0.1:5220'
const fixturePath = resolve('.local/spaces-network/fixtures.json')
const output = resolve('docs/evidence/mcp-inbound.json')
const privateOutput = resolve('.local/mcp-proof/state.json')
const runId = `mcp-${Date.now().toString(36)}`
const fixture = JSON.parse(await readFile(fixturePath, 'utf8'))
if (fixture.status !== 'ready') throw new Error('DisposableFixtureNotReady')
const roles = Object.fromEntries(fixture.accounts.map(a => [a.role, a]))
const evidence = { proof: 'mcp-inbound', runId, synthetic: false, origin, checks: [], calls: [], completedAt: null }

function local(value) {
  const url = new URL(value)
  if (!['localhost', '127.0.0.1', '[::1]'].includes(url.hostname) || !['http:', 'https:'].includes(url.protocol)
      || url.username || url.password) throw new Error('OnlyDisposableLoopbackOriginsAllowed')
  return url
}

async function request(url, { token, body, method = body === undefined ? 'GET' : 'POST', headers = {} } = {}) {
  const response = await fetch(local(url), { method, redirect: 'error', signal: AbortSignal.timeout(45000),
    headers: { accept: 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}),
      ...(body !== undefined ? { 'content-type': 'application/json' } : {}), ...headers },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }) })
  const raw = await response.text()
  if (Buffer.byteLength(raw) > 2 * 1024 * 1024) throw new Error('ResponseTooLarge')
  let json = null
  if (raw) { try { json = JSON.parse(raw) } catch { throw new Error(`InvalidJsonStatus${response.status}`) } }
  return { status: response.status, json }
}

function check(label, condition) {
  if (!condition) throw new Error(`CheckFailed:${label}`)
  evidence.checks.push(label)
}

async function serviceProof(role, discovery, override = {}) {
  const account = roles[role]
  if (!account) throw new Error('UnknownFixtureRole')
  const pds = local(account.pds)
  const login = await request(new URL('/xrpc/com.atproto.server.createSession', pds),
    { body: { identifier: account.handle, password: account.password } })
  check(`fixture-login-${role}`, login.status === 200 && login.json.did === account.did && typeof login.json.accessJwt === 'string')
  const url = new URL('/xrpc/com.atproto.server.getServiceAuth', pds)
  url.searchParams.set('aud', override.audience ?? discovery.audience)
  url.searchParams.set('lxm', override.method ?? discovery.exchangeMethod)
  url.searchParams.set('exp', String(Math.floor(Date.now() / 1000) + 120))
  const result = await request(url, { token: login.json.accessJwt })
  check(`fixture-proof-${role}`, result.status === 200 && typeof result.json.token === 'string')
  return result.json.token
}

let rpcId = 0
async function rpc(token, method, params = {}, headerOverrides = {}) {
  const meta = { 'io.modelcontextprotocol/protocolVersion': '2026-07-28', 'io.modelcontextprotocol/clientCapabilities': {},
    'io.modelcontextprotocol/clientInfo': { name: 'tangent-inbound-proof', version: '0.1' } }
  return request(origin + '/mcp', { token,
    headers: { accept: 'application/json, text/event-stream', 'MCP-Protocol-Version': '2026-07-28', 'Mcp-Method': method,
      ...(params.name ? { 'Mcp-Name': params.name } : {}), ...headerOverrides },
    body: { jsonrpc: '2.0', id: ++rpcId, method, params: { ...params, _meta: meta } } })
}

async function tool(token, name, args, expected = 'ok') {
  const response = await rpc(token, 'tools/call', { name, arguments: args })
  check(`${name}-http`, response.status === 200)
  const payload = response.json?.result?.structuredContent
  check(`${name}-envelope`, payload?.operation === name && payload?.status === expected && payload.contractVersion === '0.1')
  check(`${name}-segments`, ['identity', 'place', 'result', 'activity', 'next'].every(k => k in payload.segments))
  check(`${name}-bounded-activity`, payload.segments.activity.notices.length <= 3)
  // Contains only public fixture DID, non-secret references, content and receipts.
  evidence.calls.push({ operation: name, input: args, response: payload })
  return payload
}

try {
  const discoveryResult = await request(origin + '/.well-known/tangent-mcp')
  check('discovery', discoveryResult.status === 200)
  const discovery = { ...discoveryResult.json, audience: discoveryResult.json.serviceProof?.audience,
    exchangeMethod: discoveryResult.json.serviceProof?.method }
  check('proof-audience', typeof discovery.audience === 'string' && discovery.audience.startsWith('did:'))
  check('proof-method', discovery.exchangeMethod === 'local.tangent.mcp.exchange')
  const ownerProof = await serviceProof('owner', discovery)
  const exchange = await request(origin + '/mcp/token', { token: ownerProof,
    body: { name: runId, lifetimeDays: 1, grants: ['welcome', 'read', 'post', 'manage'] } })
  check('owner-proof-exchange', exchange.status === 200 && typeof exchange.json.token === 'string')
  const ownerToken = exchange.json.token
  const replay = await request(origin + '/mcp/token', { token: ownerProof, body: { name: runId } })
  check('proof-replay-denied', replay.status === 401)
  const wrongProof = await serviceProof('agent', discovery, { method: 'com.atproto.simplespace.checkUserAccess' })
  check('wrong-method-denied', (await request(origin + '/mcp/token', { token: wrongProof, body: { name: runId } })).status === 401)
  check('anonymous-mcp-denied', (await rpc(null, 'tools/list')).status === 401)
  check('metadata-mismatch-denied', (await rpc(ownerToken, 'tools/list', {}, { 'Mcp-Method': 'tools/call' })).status === 400)
  check('foreign-origin-denied', (await rpc(ownerToken, 'tools/list', {}, { Origin: 'https://untrusted.example' })).status === 403)
  check('protocol-header-mismatch-denied', (await rpc(ownerToken, 'tools/list', {}, { 'MCP-Protocol-Version': '2025-11-25' })).status === 400)
  check('tool-header-mismatch-denied', (await rpc(ownerToken, 'tools/call',
    { name: 'SelectCompanion', arguments: { moniker: roles.owner.did } }, { 'Mcp-Name': 'PostMessage' })).status === 400)

  const declared = await rpc(ownerToken, 'server/discover')
  check('server-discover', declared.status === 200 && !!declared.json.result)
  const listing = await rpc(ownerToken, 'tools/list')
  check('tool-discovery', listing.status === 200 && Array.isArray(listing.json.result?.tools))
  const names = listing.json.result.tools.map(t => t.name)
  check('no-connector-credential-tools', !names.includes('RegisterCompanion') && !names.includes('ListCompanions'))
  evidence.tools = names
  check('full-inbound-vocabulary', names.length === 18)

  const selected = await tool(ownerToken, 'SelectCompanion', { moniker: roles.owner.did })
  const companionId = selected.companionId
  check('selection-has-no-server-context', selected.contextId === null && companionId.startsWith('cmp_'))
  check('verified-acting-did', selected.segments.identity.did === roles.owner.did)
  const selectedByHandle = await tool(ownerToken, 'SelectCompanion', { moniker: '@' + roles.owner.handle })
  check('handle-and-did-same-companion', selectedByHandle.companionId === companionId)
  const wrongServer = await tool(ownerToken, 'Arrive', { companionId, serverUrl: 'https://different.tangent.example' }, 'blocked')
  check('wrong-server-does-not-issue-context', wrongServer.contextId === null)
  const arrived = await tool(ownerToken, 'Arrive', { companionId, serverUrl: origin })
  const contextId = arrived.contextId
  check('arrival-binds-both-identifiers', contextId.startsWith('ctx_') && arrived.companionId === companionId)
  const serverRef = arrived.segments.place.serverRef
  const tangents = await tool(ownerToken, 'ListTangents', { contextId, serverRef })
  check('retained-community-directory', tangents.segments.result.data.tangents.length > 0)
  const tangentRef = tangents.segments.result.data.tangents[0].tangentRef
  const channels = await tool(ownerToken, 'ListChannels', { contextId, tangentRef })
  const channelRef = channels.segments.result.data.channels[0]?.channelRef
  check('retained-channel-directory', typeof channelRef === 'string')
  await tool(ownerToken, 'ReadChannel', { contextId, channelRef, limit: 2 })

  const agentProof = await serviceProof('agent', discovery)
  const agentExchange = await request(origin + '/mcp/token', { token: agentProof, body: { name: runId + '-agent', lifetimeDays: 1 } })
  check('agent-proof-exchange', agentExchange.status === 200 && typeof agentExchange.json.token === 'string')
  const agentToken = agentExchange.json.token
  await tool(agentToken, 'ReadChannel', { contextId, channelRef }, 'blocked')
  const agentSelected = await tool(agentToken, 'SelectCompanion', { moniker: roles.agent.did })
  check('separate-companion-identities', agentSelected.companionId !== companionId && agentSelected.contextId === null && agentSelected.segments.identity.did === roles.agent.did)
  await tool(agentToken, 'Arrive', { companionId, serverUrl: origin }, 'blocked')
  const agentArrived = await tool(agentToken, 'Arrive', { companionId: agentSelected.companionId, serverUrl: origin })
  check('separate-server-contexts', agentArrived.contextId !== contextId)

  // Credentials are retained only under ignored private state for restart/revocation follow-up.
  await mkdir(dirname(privateOutput), { recursive: true })
  await writeFile(privateOutput, JSON.stringify({ runId, ownerToken, agentToken, companionId, agentCompanionId: agentSelected.companionId, contextId, agentContextId: agentArrived.contextId,
    ownerDid: roles.owner.did, agentDid: roles.agent.did, serverRef, tangentRef, channelRef }) + '\n', { mode: 0o600 })
  evidence.completedAt = new Date().toISOString()
  await mkdir(dirname(output), { recursive: true })
  await writeFile(output, JSON.stringify(evidence, null, 2) + '\n')
  console.log(JSON.stringify({ passed: true, checks: evidence.checks.length, calls: evidence.calls.length, tools: names.length, evidence: output }))
} catch (error) {
  // Whitelist labels generated here; never print HTTP bodies, JWTs or arbitrary exceptions.
  const safe = /^CheckFailed:[A-Za-z0-9_-]+$/.test(error?.message ?? '') ? error.message : 'ProofFailedSeeStage'
  console.error(JSON.stringify({ passed: false, failure: safe, lastCheck: evidence.checks.at(-1) ?? 'none' }))
  process.exitCode = 1
}
