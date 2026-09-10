// Offline schema validation against the pinned official implementation. No sessions or network requests.
import { readFile } from 'node:fs/promises'
import { Lexicons } from '../packages/lexicon/dist/index.js'

const ids = ['local.tangent.room', 'local.tangent.message']
const docs = await Promise.all(ids.map(async id => JSON.parse(await readFile(new URL(`./lexicons/${id}.json`, import.meta.url), 'utf8'))))
const schemas = new Lexicons(docs)
const message = { $type: ids[1], text: 'A source-backed reply.', createdAt: '2026-09-09T21:00:00.000Z' }
const source = { uri: 'at://did:plc:example/space/local.tangent.room/lounge/did:plc:writer/local.tangent.message/one', cid: 'bafyreidvmsh4woms3l3flgzzn6763qlrl7hrirx2hbzbnxs2oxyhtfdegy' }
const checks = [
  ['plain message', message, true],
  ['reply with source reference', { ...message, replyTo: source }, true],
  ['missing reply CID', { ...message, replyTo: { uri: source.uri } }, false],
  ['invalid reply CID', { ...message, replyTo: { ...source, cid: 'invalid' } }, false],
  ['invalid timestamp', { ...message, createdAt: 'yesterday' }, false],
  ['oversized UTF-8 body', { ...message, text: '🪴'.repeat(1025) }, false],
].map(([name, record, expected]) => ({ name, passed: schemas.validate(ids[1], record).success === expected }))
process.stdout.write(JSON.stringify({ observedAt: new Date().toISOString(), passed: checks.every(c => c.passed), source: 'Pinned official Lexicon validator; offline, not PDS write enforcement or live schema publication.', checks }, null, 2))
if (checks.some(c => !c.passed)) process.exitCode = 1
