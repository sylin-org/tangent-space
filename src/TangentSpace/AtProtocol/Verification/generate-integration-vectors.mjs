// Independent official oracle. Run inside the pinned disposable atproto image
// at /atproto/tangent-verification-integration.mjs; emits public test data only.
import { Secp256k1Keypair, P256Keypair } from './packages/crypto/dist/index.js'
import { RepoCommit, serializeRecord, serializeRepo, verifyRepoCarFull } from './packages/space/dist/index.js'
import { encodeCarBlock, readCarStream } from './packages/car/dist/index.js'
const collect = async stream => { const chunks = []; for await (const chunk of stream) chunks.push(chunk); return Buffer.concat(chunks) }
const context = { space: 'at://did:example:site/space/local.tangent.room/duplicates', author: 'did:example:author', rev: '3kbcq3p7ad400' }
const vectors = []
for (const [curve, type] of [['secp256k1', Secp256k1Keypair], ['P-256', P256Keypair]]) {
  const signer = await type.create()
  const value = { $type: 'local.tangent.message', text: 'same value' }
  const records = await Promise.all(['one', 'two'].map(key => serializeRecord('local.tangent.message', key, value)))
  const commit = await RepoCommit.fromRecords(records).sign(context, signer)
  const car = await collect(serializeRepo(commit, records))
  const verified = await verifyRepoCarFull([car], { ...context, didKey: signer.did() })
  if (verified.records.length !== 2 || !verified.records[0].cid.equals(verified.records[1].cid)) throw new Error('Duplicate fixture changed')
  await verified[Symbol.asyncDispose]()
  const finalFrame = encodeCarBlock(records[1])
  const deduplicated = car.subarray(0, car.length - finalFrame.length)
  let deduplicatedRejected = false
  try { const result = await verifyRepoCarFull([deduplicated], { ...context, didKey: signer.did() }); await result[Symbol.asyncDispose]() } catch { deduplicatedRejected = true }
  if (!deduplicatedRejected) throw new Error('Official verifier unexpectedly accepted deduplication')
  const signingInput = Buffer.from('eyJhbGciOiJ0ZXN0In0.eyJpc3MiOiJkaWQ6ZXhhbXBsZTphdXRob3IifQ')
  vectors.push({ curve, ...context, multikey: signer.did().slice('did:key:'.length), carBase64: car.toString('base64'), finalFrameLength: finalFrame.length, deduplicatedRejected, signingInput: signingInput.toString('utf8'), signatureHex: Buffer.from(await signer.sign(signingInput)).toString('hex') })
}
console.log(JSON.stringify(vectors))
