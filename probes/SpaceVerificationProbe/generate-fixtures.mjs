// Run inside the pinned upstream image: this is the independent TS oracle,
// not a runtime dependency of the .NET verifier. Only disposable test data.
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { Secp256k1Keypair, P256Keypair, parseDidKey } from '../packages/crypto/dist/index.js'
import { IdResolver } from '../packages/identity/dist/index.js'
import { RepoCommit, serializeRecord, serializeRepo, verifyRepoCarFull, LtHash, encodeCommitCtx } from '../packages/space/dist/index.js'
import { encodeCarBlock, readCarStream } from '../packages/car/dist/index.js'

const out = '/atproto/tangent-verification/out'
await mkdir(out, { recursive: true })
const collect = async stream => { const chunks = []; for await (const c of stream) chunks.push(c); return Buffer.concat(chunks) }
const tests = []
async function test(name, car, key, ctx, expected, expectValues = true) {
  let accepted = false, error
  try { const result = await verifyRepoCarFull([car], { ...ctx, didKey: key.did, expectValues }); accepted = true; await result[Symbol.asyncDispose]() } catch (e) { error = e.message }
  if (accepted !== expected) throw new Error(`Oracle expectation failed: ${name}: ${error}`)
  await writeFile(`${out}/${name}.car`, car)
  tests.push({ name, file: `${name}.car`, curve: key.curve, publicKeyHex: key.bytes, didKey: key.did, space: ctx.space, author: ctx.author, expectValues, expected, oracleError: error })
}
const ctx = { space: 'at://did:example:site/space/local.tangent.room/vector', author: 'did:example:author', rev: '3kbcq3p7ad400' }
for (const [curve, klass] of [['secp256k1', Secp256k1Keypair], ['P-256', P256Keypair]]) {
  const signer = await klass.create()
  const key = { curve, bytes: Buffer.from(signer.publicKeyBytes()).toString('hex'), did: signer.did() }
  const records = await Promise.all([
    serializeRecord('local.tangent.message', 'one', { $type: 'local.tangent.message', text: 'hello', createdAt: '2026-09-09T00:00:00Z' }),
    serializeRecord('local.tangent.message', 'two', { $type: 'local.tangent.message', text: 'world', createdAt: '2026-09-09T00:00:01Z' }),
  ])
  const commit = await RepoCommit.fromRecords(records).sign(ctx, signer)
  const car = await collect(serializeRepo(commit, records))
  await test(`${curve}-valid`, car, key, ctx, true)
  await test(`${curve}-wrong-space`, car, key, { ...ctx, space: ctx.space + '-other' }, false)
  await test(`${curve}-wrong-author`, car, key, { ...ctx, author: 'did:example:other' }, false)
  const otherKey = await klass.create()
  await test(`${curve}-wrong-key`, car, { ...key, bytes: Buffer.from(otherKey.publicKeyBytes()).toString('hex'), did: otherKey.did() }, ctx, false)
  const badMac = { ...commit, mac: Uint8Array.from(commit.mac) }; badMac.mac[0] ^= 1
  await test(`${curve}-bad-mac`, await collect(serializeRepo(badMac, records)), key, ctx, false)
  const badSig = { ...commit, sig: Uint8Array.from(commit.sig) }; badSig.sig[0] ^= 1
  await test(`${curve}-bad-signature`, await collect(serializeRepo(badSig, records)), key, ctx, false)
  await test(`${curve}-record-deletion`, await collect(serializeRepo(commit, records.slice(1))), key, ctx, false)
  const added = await serializeRecord('local.tangent.message', 'three', { text: 'unauthorized addition' })
  await test(`${curve}-record-addition`, await collect(serializeRepo(commit, [...records, added])), key, ctx, false)
  const indexOnly = await collect(serializeRepo(commit, records, { excludeValues: true }))
  await test(`${curve}-missing-records`, indexOnly, key, ctx, false)
  await test(`${curve}-index-only`, indexOnly, key, ctx, true, false)
  const tampered = Buffer.from(car); tampered[tampered.length - 1] ^= 1
  await test(`${curve}-tampered-block`, tampered, key, ctx, false)
  await test(`${curve}-truncated`, car.subarray(0, car.length - 7), key, ctx, false)
  await test(`${curve}-extra-block`, Buffer.concat([car, encodeCarBlock(added)]), key, ctx, false)
  const emptyCommit = await RepoCommit.fromRecords([]).sign(ctx, signer)
  await test(`${curve}-empty`, await collect(serializeRepo(emptyCommit, [])), key, ctx, true)
  await test(`${curve}-unsupported-version`, await collect(serializeRepo({ ...commit, ver: 2 }, records)), key, ctx, false)
}

// Independent numeric/context vectors expose an endian or HKDF-mode mistake.
const elements = ['local.tangent.message/one/bafyreiaaa', 'local.tangent.message/two/bafyreiaab']
const set = new LtHash(); for (const e of elements) set.add(e)
const primitiveVectors = { elements, stateHex: Buffer.from(set.state()).toString('hex'), digestHex: Buffer.from(set.digest()).toString('hex'), ctx, ikmHex: '01020304', encodedContextHex: Buffer.from(encodeCommitCtx(ctx, Uint8Array.from([1,2,3,4]))).toString('hex') }

// Actual PDS download, using the account's own legacy session solely to obtain
// bytes for verification. No changes to data, memberships, or the running PDS.
const fixture = JSON.parse(await readFile('/evidence/fixtures.json', 'utf8'))
const actor = fixture.accounts.find(a => a.role === 'agent')
const login = await fetch(`${actor.pds}/xrpc/com.atproto.server.createSession`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ identifier: actor.did, password: actor.password }) })
if (!login.ok) throw new Error('Fixture login failed')
const session = await login.json()
const url = new URL('/xrpc/com.atproto.space.getRepo', actor.pds)
url.searchParams.set('space', fixture.rooms.workshop); url.searchParams.set('repo', actor.did)
const response = await fetch(url, { headers: { authorization: `Bearer ${session.accessJwt}` } })
if (!response.ok) throw new Error(`Actual PDS CAR download failed: ${response.status}`)
const liveCar = Buffer.from(await response.arrayBuffer())
const resolver = new IdResolver({ plcUrl: fixture.plc })
const didKey = await resolver.did.resolveAtprotoKey(actor.did)
const parsed = parseDidKey(didKey)
await test('actual-pds-repository', liveCar, { curve: parsed.jwtAlg === 'ES256K' ? 'secp256k1' : 'P-256', bytes: Buffer.from(parsed.keyBytes).toString('hex'), did: didKey }, { space: fixture.rooms.workshop, author: actor.did }, true)
await writeFile(`${out}/manifest.json`, JSON.stringify({ generatedAt: new Date().toISOString(), upstreamRevision: fixture.revision, oracle: '@atproto/space verifyRepoCarFull; pinned upstream image plus varint interop fix', primitiveVectors, tests }, null, 2))
console.log(JSON.stringify({ cases: tests.length, actualPdsBytes: liveCar.length, output: out }))
