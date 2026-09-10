// Real official TypeScript MCP client against the local PoC; no model and no source writes.
import { readFile, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'

const sdk = resolve('.local/mcp-proof/sdk/node_modules/@modelcontextprotocol/sdk/dist/esm')
const { Client } = await import(pathToFileURL(resolve(sdk, 'client/index.js')))
const { StreamableHTTPClientTransport } = await import(pathToFileURL(resolve(sdk, 'client/streamableHttp.js')))
const state = JSON.parse(await readFile('.local/mcp-proof/state.json', 'utf8'))
const client = new Client({ name: 'tangent-official-sdk-proof', version: '0.1' }, { capabilities: {} })
const transport = new StreamableHTTPClientTransport(new URL('http://127.0.0.1:5220/mcp'), {
  requestInit: { headers: { authorization: `Bearer ${state.agentToken}` } },
})
let phase = 'connect'
try {
  await client.connect(transport)
  phase = 'list-tools'
  const listing = await client.listTools()
  if (!listing.tools.some(t => t.name === 'SelectCompanion')) throw new Error('MissingSelection')
  phase = 'select-companion'
  const selection = await client.callTool({ name: 'SelectCompanion', arguments: { moniker: state.agentDid } })
  const selected = selection.structuredContent
  if (selected?.status !== 'ok' || selected.segments.identity.did !== state.agentDid) throw new Error('WrongIdentity')
  phase = 'arrive'
  const arrival = await client.callTool({ name: 'Arrive', arguments: { companionId: selected.companionId, serverUrl: 'http://127.0.0.1:5220' } })
  if (arrival.structuredContent?.status !== 'ok') throw new Error('ArrivalFailed')
  const evidence = { proof: 'official-typescript-mcp-client', sdkVersion: '1.30.0', passed: true,
    server: client.getServerVersion(), toolCount: listing.tools.length, methods: ['initialize', 'tools/list', 'tools/call'],
    selectedDid: state.agentDid, companionPreserved: selected.companionId === state.agentCompanionId, contextPreserved: arrival.structuredContent.contextId === state.agentContextId,
    segmentNames: Object.keys(arrival.structuredContent.segments), completedAt: new Date().toISOString() }
  await writeFile('docs/evidence/mcp-sdk.json', JSON.stringify(evidence, null, 2) + '\n')
  console.log(JSON.stringify(evidence))
} catch {
  console.error(JSON.stringify({ passed: false, phase, error: 'McpSdkProofFailed' }))
  process.exitCode = 1
} finally {
  await client.close().catch(() => {})
}
