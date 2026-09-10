// Run from repository root on the disposable network. Emits public DID documents
// and an already-expired real service signature; never prints account credentials.
import { readFile } from 'node:fs/promises'
import { setTimeout as wait } from 'node:timers/promises'
const fixture = JSON.parse(await readFile('.local/spaces-network/fixtures.json', 'utf8'))
const authority = fixture.accounts.find(account => account.role === 'authority')
const agent = fixture.accounts.find(account => account.role === 'agent')
const authorityDocument = await (await fetch(`${fixture.plc}/${authority.did}`)).json()
const agentDocument = await (await fetch(`${fixture.plc}/${agent.did}`)).json()
const login = await fetch(`${authority.pds}/xrpc/com.atproto.server.createSession`, { method: 'POST', headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ identifier: authority.did, password: authority.password }) })
if (!login.ok) throw new Error('Disposable authority login failed')
const session = await login.json()
const url = new URL('/xrpc/com.atproto.server.getServiceAuth', authority.pds)
const expiresAt = Math.floor(Date.now() / 1000) + 3
url.searchParams.set('aud', fixture.managingApp)
url.searchParams.set('lxm', 'com.atproto.simplespace.checkUserAccess')
url.searchParams.set('exp', String(expiresAt))
const response = await fetch(url, { headers: { authorization: `Bearer ${session.accessJwt}` } })
if (!response.ok) throw new Error(`Disposable service signature request failed: ${response.status}`)
const { token } = await response.json()
const parts = token.split('.')
await wait(Math.max(0, expiresAt * 1000 - Date.now() + 1000))
console.log(JSON.stringify({ upstreamRevision: fixture.revision, authorityDocument, agentDocument,
  expiredServiceSignature: { expiresAt, signingInput: parts.slice(0, 2).join('.'), signatureHex: Buffer.from(parts[2], 'base64url').toString('hex') } }))
