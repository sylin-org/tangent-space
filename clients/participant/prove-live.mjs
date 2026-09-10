// Authorized disposable S06 proof. This saves credentials only under --output,
// never prints them, and leaves site/room membership unchanged.
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
const options = Object.fromEntries(Array.from({ length: (process.argv.length - 2) / 2 }, (_, index) => [process.argv[2 + index * 2].slice(2), process.argv[3 + index * 2]]))
const site = options.site ?? 'http://127.0.0.1:5223'
const room = options.room ?? 'poc-1788987688033-workshop'
const cookiesDirectory = resolve(options['cookies-dir'] ?? '.local/tangent/1788984280268/rooms-proof/room-proof-poc-1788987688033')
const output = resolve(options.output ?? '.local/participation-proof')
await mkdir(output, { recursive: true })
const cookie = async role => {
  const saved = JSON.parse(await readFile(join(cookiesDirectory, role + '.cookies.json'), 'utf8'))
  if (saved.origin !== site) throw new Error('Cookie fixture belongs to another host')
  return { did: saved.did, header: saved.cookies.map(([name, value]) => `${name}=${value}`).join('; ') }
}
const agent = await cookie('agent'); const owner = await cookie('owner')
const checks = []
async function request(path, { token, browser, body, origin = site } = {}) {
  const response = await fetch(site + path, { method: body === undefined ? 'GET' : 'POST', redirect: 'manual', signal: AbortSignal.timeout(30000),
    headers: { ...(token ? { authorization: `Bearer ${token}` } : {}), ...(browser ? { cookie: browser.header } : {}),
      ...(body === undefined ? {} : { 'content-type': 'application/json', ...(origin ? { origin } : {}) }) }, body: body === undefined ? undefined : JSON.stringify(body) })
  const text = await response.text(); let json
  try { json = text ? JSON.parse(text) : null } catch { json = null }
  return { status: response.status, json }
}
function check(name, condition, details = {}) {
  checks.push({ name, passed: !!condition, ...details })
  if (!condition) throw new Error(`Live proof failed: ${name}`)
}
async function contractChecks() {
  const submittedDid = await request('/api/participation/credentials', { browser: agent, body: { name: 'invalid delegated DID', lifetimeDays: 7, grants: ['welcome', 'read'], did: owner.did } })
  check('submitted-DID-rejected', submittedDid.status === 400, { status: submittedDid.status })
  const defaultLifetime = await request('/api/participation/credentials', { browser: agent, body: { name: 'S06 default lifetime' } })
  check('omitted-lifetime-defaults-seven-days', defaultLifetime.status === 200 &&
    Date.parse(defaultLifetime.json?.credential?.expiresAt) - Date.parse(defaultLifetime.json?.credential?.createdAt) === 7 * 86400000,
    { status: defaultLifetime.status })
  const unknownRevoke = await request(`/api/participation/credentials/${defaultLifetime.json.credential.id}/revoke`, { browser: agent, body: { did: owner.did } })
  check('revocation-unknown-field-rejected', unknownRevoke.status === 400, { status: unknownRevoke.status })
  await request(`/api/participation/credentials/${defaultLifetime.json.credential.id}/revoke`, { browser: agent, body: {} })
}
try {
  if (options.contracts === 'true') {
    const previous = JSON.parse(await readFile(join(output, 'participation.json'), 'utf8'))
    const repeated = new Set(['submitted-DID-rejected', 'omitted-lifetime-defaults-seven-days', 'revocation-unknown-field-rejected'])
    checks.push(...previous.checks.filter(item => !repeated.has(item.name)))
    await contractChecks()
  } else if (options.resume === 'true') {
    const previous = JSON.parse(await readFile(join(output, 'participation.json'), 'utf8'))
    checks.push(...previous.checks.filter(item => item.name !== 'scripted-human-fixture-accepted'))
    const context = JSON.parse(await readFile(join(output, 'context.json'), 'utf8'))
    const human = await request(`/api/rooms/${room}/messages`, { browser: owner, body: context.operation })
    context.human = { text: context.operation.text, ...human.json }
    await writeFile(join(output, 'context.json'), JSON.stringify(context, null, 2))
    check('scripted-human-fixture-accepted', human.status === 200 && human.json?.state === 'accepted',
      { status: human.status, operationId: human.json?.operationId, sourceUri: human.json?.sourceUri, sourceCid: human.json?.sourceCid, detail: human.json?.detail })
  } else {
  const identity = await request('/api/site', { browser: agent })
  check('verified-agent-cookie', identity.status === 200 && identity.json?.participant?.did === agent.did, { status: identity.status, did: agent.did })
  await contractChecks()
  for (const [name, origin] of [['missing-origin-rejected', null], ['cross-origin-rejected', 'https://other.invalid']]) {
    const denied = await request('/api/participation/credentials', { browser: agent, body: { name: 'cross-origin probe' }, origin })
    check(name, denied.status === 401 || denied.status === 403, { status: denied.status })
  }
  const invalidMixed = await request('/api/site', { browser: agent, token: 'invalid' })
  check('invalid-bearer-cannot-fall-back-to-cookie', invalidMixed.status === 401, { status: invalidMixed.status })
  const issued = await request('/api/participation/credentials', { browser: agent, body: { name: 'S06 runner', lifetimeDays: 1 } })
  check('enrollment-binds-agent-DID', issued.status === 200 && issued.json?.credential?.did === agent.did && !!issued.json?.token, { status: issued.status })
  let credential = issued.json
  const welcome = await request('/api/site', { token: credential.token })
  check('bearer-welcome-without-cookie', welcome.status === 200 && welcome.json?.participant?.did === agent.did, { status: welcome.status })
  const bearerEnrollment = await request('/api/participation/credentials', { token: credential.token, body: { name: 'must not enroll' } })
  check('bearer-cannot-enroll', [401, 403].includes(bearerEnrollment.status), { status: bearerEnrollment.status })
  const ownerIssued = await request('/api/participation/credentials', { browser: owner, body: { name: 'S06 administration ceiling', lifetimeDays: 1 } })
  check('owner-fixture-credential-issued', ownerIssued.status === 200, { status: ownerIssued.status })
  const ownerDenied = await request('/api/connections/authority', { token: ownerIssued.json.token })
  check('owner-credential-cannot-administer-authority', ownerDenied.status === 403, { status: ownerDenied.status })
  await request(`/api/participation/credentials/${ownerIssued.json.credential.id}/revoke`, { browser: owner, body: {} })
  const readOnly = await request('/api/participation/credentials', { browser: agent, body: { name: 'S06 read only', lifetimeDays: 1, grants: ['welcome', 'read'] } })
  check('narrow-credential-issued', readOnly.status === 200, { status: readOnly.status })
  const postDenied = await request(`/api/rooms/${room}/messages`, { token: readOnly.json.token, body: { operationId: 's06-denied-' + Date.now(), text: 'This write must be denied.' } })
  check('read-only-credential-cannot-post', postDenied.status === 403, { status: postDenied.status })
  await request(`/api/participation/credentials/${readOnly.json.credential.id}/revoke`, { browser: agent, body: {} })
  const revoked = await request(`/api/participation/credentials/${credential.credential.id}/revoke`, { browser: agent, body: {} })
  check('credential-revoked', revoked.status === 204, { status: revoked.status })
  const denied = await request('/api/site', { token: credential.token })
  check('revoked-credential-denied-next-request', denied.status === 401, { status: denied.status })
  const renewed = await request('/api/participation/credentials', { browser: agent, body: { name: 'S06 agent demo', lifetimeDays: 1 } })
  check('reenrollment-preserves-DID', renewed.status === 200 && renewed.json?.credential?.did === agent.did && renewed.json.token !== credential.token, { status: renewed.status })
  credential = renewed.json
  await writeFile(join(output, 'agent-credential.json'), JSON.stringify(credential, null, 2), { mode: 0o600 })
  const humanText = 'For our Tangent PoC, suggest one concrete test that would distinguish immediate room removal from an already issued Spaces credential expiring later. Reply in two short sentences and mention what evidence you would record.'
  const operation = { operationId: 's06-human-' + Date.now(), text: humanText }
  const context = { site, room, agentDid: agent.did, ownerDid: owner.did, operation }
  await writeFile(join(output, 'context.json'), JSON.stringify(context, null, 2))
  const human = await request(`/api/rooms/${room}/messages`, { browser: owner, body: operation })
  context.human = { text: humanText, ...human.json }
  await writeFile(join(output, 'context.json'), JSON.stringify(context, null, 2))
  check('scripted-human-fixture-accepted', human.status === 200 && human.json?.state === 'accepted', { status: human.status, operationId: human.json?.operationId, sourceUri: human.json?.sourceUri, sourceCid: human.json?.sourceCid, detail: human.json?.detail })
  }
} catch (error) {
  await writeFile(join(output, 'participation.json'), JSON.stringify({ completedAt: new Date().toISOString(), site, room, status: 'failed', error: error.message, checks }, null, 2))
  console.log(JSON.stringify({ status: 'failed', error: error.message, checks })); process.exitCode = 1
}
if (!process.exitCode) {
  const evidence = { completedAt: new Date().toISOString(), site, room, status: 'passed', authentication: 'Real Koan browser cookie enrollment; opaque Tangent credentials, no cookie on participation requests.', runnerRestart: 'pending combined application restart proof', actualModelReply: 'separate agent-demo evidence', checks }
  await writeFile(join(output, 'participation.json'), JSON.stringify(evidence, null, 2))
  console.log(JSON.stringify(evidence))
}
