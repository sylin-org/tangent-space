import { readFile, writeFile } from 'node:fs/promises'
import { LexResolver } from '../packages/lex/lex-resolver/dist/index.js'
const fixture = JSON.parse(await readFile('/evidence/fixtures.json', 'utf8'))
const resolver = new LexResolver({
  fetch: globalThis.fetch,
  plcDirectoryUrl: fixture.plc,
  hooks: { onResolveAuthority: () => fixture.lexiconAuthority },
})
const checks = []
for (const nsid of [fixture.spaceType, fixture.collection, 'com.example.group']) {
  try {
    const result = await resolver.get(nsid, { noCache: true })
    checks.push({ nsid, uri: result.uri.toString(), cid: result.cid.toString(), lexicon: result.lexicon })
  } catch (err) {
    const causes = []
    for (let current = err; current; current = current.cause) causes.push({ name: current.name, message: current.message, reason: current.reason })
    checks.push({ nsid, error: causes })
  }
}
const evidence = { completedAt: new Date().toISOString(), revision: fixture.revision, patch: 'upstream-varint.patch', checks }
await writeFile('/evidence/lexicon-resolution-evidence.json', JSON.stringify(evidence, null, 2))
console.log(JSON.stringify(evidence, null, 2))
if (checks.some(check => check.error)) process.exitCode = 1
