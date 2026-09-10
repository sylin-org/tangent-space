// Disposable test registration only: real signed PLC operation, no PDS restart.
import { readFile, writeFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { Secp256k1Keypair } from '../packages/crypto/dist/index.js'

const port = Number(process.argv[2] ?? '5220')
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error('Expected a local monolith port between 1024 and 65535')
const fixture = JSON.parse(await readFile('/evidence/fixtures.json', 'utf8'))
if (fixture.status !== 'ready' || fixture.plc !== 'http://localhost:2582') throw new Error('Start the disposable S01 network first')
const endpoint = `http://host.docker.internal:${port}`
const output = `/evidence/tangent-service-${port}.json`
const require = createRequire(new URL('../packages/dev-env/package.json', import.meta.url))
const plc = require('@did-plc/lib')
const matches = async service => {
  if (service.endpoint !== endpoint || !/^did:plc:[a-z2-7]{24}#tangent$/.test(service.managingApp ?? '')) return false
  const did = service.managingApp.slice(0, -'#tangent'.length)
  const response = await fetch(`${fixture.plc}/${did}`, { signal: AbortSignal.timeout(10000) })
  if (!response.ok) return false
  const document = await response.json()
  return document.id === did && document.service?.some(item =>
    (item.id === '#tangent' || item.id === service.managingApp) && item.type === 'AtprotoSpaceService' && item.serviceEndpoint === endpoint)
}

let service
try {
  const existing = JSON.parse(await readFile(output, 'utf8'))
  if (await matches(existing)) service = existing
} catch { /* A fresh network or first registration requires a new service DID. */ }

if (!service) {
  const signingKey = await Secp256k1Keypair.create()
  const operation = await plc.signOperation({ type: 'plc_operation', rotationKeys: [signingKey.did()], alsoKnownAs: [], verificationMethods: {},
    services: { tangent: { type: 'AtprotoSpaceService', endpoint } }, prev: null }, signingKey)
  const did = await plc.didForCreateOp(operation)
  await new plc.Client(fixture.plc).sendOperation(did, operation)
  service = { managingApp: `${did}#tangent`, endpoint }
  if (!await matches(service)) throw new Error('PLC did not return the registered Tangent service')
  // A disposable immutable service registration needs no saved private key.
  await writeFile(output, JSON.stringify(service, null, 2), { mode: 0o600 })
}
console.log(JSON.stringify({ managingApp: service.managingApp, endpoint: service.endpoint }))
