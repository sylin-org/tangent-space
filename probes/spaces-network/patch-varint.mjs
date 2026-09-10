// Narrow upstream ESM/CJS compatibility repair. It changes no CAR parsing,
// signature verification, OAuth, or Spaces authorization behavior.
import { readFile, writeFile } from 'node:fs/promises'
for (const file of ['/atproto/packages/car/src/lib/varint.ts', '/atproto/packages/car/dist/lib/varint.js']) {
  const source = await readFile(file, 'utf8')
  const oldImport = "import * as varint from 'varint'"
  const newImport = "import varint from 'varint'"
  if (!source.includes(oldImport) && !source.includes(newImport)) throw new Error(`Unexpected upstream varint module: ${file}`)
  await writeFile(file, source.replace(oldImport, newImport))
}
