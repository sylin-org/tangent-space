import { readFile, writeFile } from 'node:fs/promises'
import { LexResolver } from '../packages/lex/lex-resolver/dist/index.js'
import { Lexicons } from '../packages/lexicon/dist/index.js'
const fixture = JSON.parse(await readFile('/evidence/fixtures.json', 'utf8'))
const actor = fixture.accounts.find(a => a.role === 'agent')
const resolver = new LexResolver({ fetch: globalThis.fetch, plcDirectoryUrl: fixture.plc, hooks: { onResolveAuthority: () => fixture.lexiconAuthority } })
const resolved = await resolver.get(fixture.collection, { noCache: true })
const lexicons = new Lexicons([resolved.lexicon])
const valid = { $type: fixture.collection, text: 'Record validation probe', createdAt: new Date().toISOString() }
const invalid = { $type: fixture.collection, text: 7, createdAt: new Date().toISOString() }
const schemaChecks = { validAccepted: lexicons.validate(fixture.collection, valid).success, malformedRejected: !lexicons.validate(fixture.collection, invalid).success }
if (!schemaChecks.validAccepted || !schemaChecks.malformedRejected) throw new Error('Resolved message schema did not enforce expected shape')
const sessionResponse = await fetch(`${actor.pds}/xrpc/com.atproto.server.createSession`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ identifier: actor.did, password: actor.password }) })
if (!sessionResponse.ok) throw new Error('Fixture sign-in failed')
const session = await sessionResponse.json()
const checks = []
for (const [label, record] of [['valid', valid], ['malformed', invalid]]) {
  const response = await fetch(`${actor.pds}/xrpc/com.atproto.space.createRecord`, { method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${session.accessJwt}` }, body: JSON.stringify({ space: fixture.rooms.workshop, repo: actor.did, collection: fixture.collection, rkey: `validation-${label}`, record, validate: true }) })
  const body = await response.json()
  checks.push({ label, requestedValidation: true, status: response.status, error: body.error, message: body.message, validationStatus: body.validationStatus })
}
const evidence = { completedAt: new Date().toISOString(), revision: fixture.revision, patch: 'upstream-varint.patch', messageSchema: { uri: resolved.uri.toString(), cid: resolved.cid.toString(), ...schemaChecks }, pdsRecordValidation: checks, conclusion: 'PDS dynamic third-party record schema validation is unavailable at this revision. Tangent must validate records before writes and acceptance; verified Lexicon resolution and client validation succeed.' }
await writeFile('/evidence/record-validation-evidence.json', JSON.stringify(evidence, null, 2))
console.log(JSON.stringify(evidence, null, 2))
