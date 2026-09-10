// Opt-in disposable-account workflow proof. Uses credentials only from ignored local state.
import { readFile, writeFile } from 'node:fs/promises'
const state = JSON.parse(await readFile('.local/mcp-proof/state.json', 'utf8'))
const origin = 'http://127.0.0.1:5220'
const evidence = { proof: 'mcp-workflows', runId: state.runId, checks: [], calls: [] }
let sequence = 0
function check(label, condition) {
  if (!condition) throw new Error('CheckFailed:' + label)
  evidence.checks.push(label)
}
async function call(role, name, values, statuses = ['ok']) {
  const contextId = role === 'owner' ? state.contextId : state.agentContextId
  const input = { contextId, ...values }
  const response = await fetch(origin + '/mcp', {
    method: 'POST', redirect: 'error', signal: AbortSignal.timeout(60000),
    headers: { authorization: `Bearer ${state[role + 'Token']}`, 'content-type': 'application/json',
      accept: 'application/json, text/event-stream', 'MCP-Protocol-Version': '2026-07-28', 'Mcp-Method': 'tools/call', 'Mcp-Name': name },
    body: JSON.stringify({ jsonrpc: '2.0', id: ++sequence, method: 'tools/call', params: { name, arguments: input,
      _meta: { 'io.modelcontextprotocol/protocolVersion': '2026-07-28', 'io.modelcontextprotocol/clientCapabilities': {},
        'io.modelcontextprotocol/clientInfo': { name: 'tangent-workflow-proof', version: '0.1' } } } })
  })
  check(name + '-http', response.status === 200)
  const json = await response.json()
  const payload = json.result?.structuredContent
  check(name + '-status', statuses.includes(payload?.status))
  check(name + '-identity', payload.segments.identity?.did === state[role + 'Did'])
  evidence.calls.push({ operation: name, input, response: payload })
  return payload
}
const key = suffix => state.runId + '-' + suffix
try {
  const home = state.serverRef + '::home'
  const lounge = home + '::tangent-lounge'
  const ownerChannels = await call('owner', 'ListChannels', { tangentRef: home })
  check('existing-lounge-preserved', ownerChannels.segments.result.data.channels.some(x => x.channelRef === lounge))
  const text = 'MCP proof: Lumen can participate through an authenticated account and keep her credentials outside the model.'
  const posted = await call('agent', 'PostMessage', { channelRef: lounge, text, requestId: key('post') }, ['ok', 'pending'])
  check('source-post-completed', posted.status === 'ok')
  const messageRef = posted.segments.result.data.message.messageRef
  const replay = await call('agent', 'PostMessage', { channelRef: lounge, text, requestId: key('post') })
  check('post-retry-same-message', replay.segments.result.data.message.messageRef === messageRef)
  await call('agent', 'PostMessage', { channelRef: lounge, text: text + ' changed', requestId: key('post') }, ['blocked'])
  const around = await call('owner', 'ReadChannel', { channelRef: lounge, aroundMessageRef: messageRef, limit: 3 })
  check('around-contains-anchor', around.segments.result.data.messages.some(m => m.messageRef === messageRef))
  const reply = await call('owner', 'PostMessage', { channelRef: lounge, replyTo: messageRef,
    text: 'The human side received your MCP message. This is a source-authored reply.', requestId: key('reply') })
  check('reply-source-reference', reply.segments.result.data.message.replyTo === messageRef)
  const elsewhere = await call('agent', 'ReadChannel', { channelRef: home + '::tangent-workshop', limit: 2 })
  check('bbs-other-channel-reply', elsewhere.segments.activity.notices.some(n => n.channelRef === lounge && n.repliesToYou.value > 0))
  const updates = await call('agent', 'GetUpdates', { limit: 2 })
  const history = await call('agent', 'ReadChannel', { channelRef: lounge, aroundMessageRef: messageRef, limit: 25 })
  await call('agent', 'MarkRead', { channelRef: lounge, readCursor: history.segments.result.data.readCursor, requestId: key('read') })
  await call('agent', 'GetOperation', { requestId: key('post') })

  const created = await call('owner', 'CreateTangent', { serverRef: state.serverRef, name: 'MCP Workshop ' + state.runId,
    description: 'Disposable local workflow verification.', visibility: 'public', requestId: key('create') })
  const tangentRef = created.segments.result.data.tangent.tangentRef
  const createdAgain = await call('owner', 'CreateTangent', { serverRef: state.serverRef, name: 'MCP Workshop ' + state.runId,
    description: 'Disposable local workflow verification.', visibility: 'public', requestId: key('create') })
  check('creation-retry-same-tangent', createdAgain.segments.result.data.tangent.tangentRef === tangentRef)
  await call('owner', 'SetParticipationPolicy', { tangentRef, admission: 'open', preset: 'everyone', undeclared: 'write', requestId: key('open') })
  await call('agent', 'JoinTangent', { tangentRef, requestId: key('join') })
  const newChannel = await call('owner', 'CreateChannel', { tangentRef, name: 'Shared notes', topic: 'A test conversation', visibility: 'public', requestId: key('channel') }, ['ok', 'pending'])
  const channelRef = newChannel.segments.result.data?.channel.channelRef
  check('channel-created', typeof channelRef === 'string')
  await call('agent', 'SetWatch', { scopeRef: tangentRef, mode: 'replies', requestId: key('watch-tangent') })
  await call('agent', 'SetWatch', { scopeRef: channelRef, mode: 'all', requestId: key('watch-channel') })
  await call('owner', 'SetRole', { scopeRef: tangentRef, participantDid: state.agentDid, role: 'admin', requestId: key('role-admin') })
  await call('owner', 'SetRole', { scopeRef: tangentRef, participantDid: state.agentDid, role: 'member', requestId: key('role-member') })
  await call('owner', 'SetRestriction', { scopeRef: tangentRef, participantDid: state.agentDid, restriction: 'ban', reason: 'Local permission proof', requestId: key('ban') })
  await call('agent', 'ReadChannel', { channelRef }, ['blocked'])
  await call('owner', 'SetRestriction', { scopeRef: tangentRef, participantDid: state.agentDid, restriction: 'none', reason: 'Restore after proof', requestId: key('restore') })
  await call('agent', 'LeaveTangent', { tangentRef, requestId: key('leave') })
  await call('owner', 'SetParticipationPolicy', { tangentRef, admission: 'invite', preset: 'everyone', undeclared: 'write', requestId: key('invite-only') })
  const invite = await call('owner', 'InviteParticipant', { tangentRef, participantDid: state.agentDid, role: 'member', requestId: key('invite') })
  check('invitation-not-externally-sent', invite.segments.result.data.delivery === 'not_sent')
  await call('agent', 'JoinTangent', { tangentRef, inviteRef: invite.segments.result.data.inviteRef, requestId: key('redeem') })
  await call('agent', 'ListTangents', { serverRef: state.serverRef, limit: 1 })
  state.workflow = { messageRef, lounge, text, requestId: key('post'), tangentRef, channelRef,
    activityCursor: updates.segments.result.data.checkpoint }
  await writeFile('.local/mcp-proof/state.json', JSON.stringify(state) + '\n', { mode: 0o600 })
  evidence.completedAt = new Date().toISOString()
  await writeFile('docs/evidence/mcp-workflows.json', JSON.stringify(evidence, null, 2) + '\n')
  console.log(JSON.stringify({ passed: true, checks: evidence.checks.length, calls: evidence.calls.length }))
} catch (error) {
  await writeFile('.local/mcp-proof/last-workflow.json', JSON.stringify(evidence, null, 2) + '\n')
  console.error(JSON.stringify({ passed: false,
    failure: /^CheckFailed:[\w-]+$/.test(error.message) ? error.message : 'WorkflowFailed',
    lastCheck: evidence.checks.at(-1) }))
  process.exitCode = 1
}
