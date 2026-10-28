// Removes the previous root build's hashed assets before a fresh one, and any
// /app bundle left from the staged hosting. The output directory is never emptied
// wholesale: the server-rendered surfaces live beside the workspace in wwwroot.
import { rm } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'

const wwwroot = fileURLToPath(new URL('../../server/web/wwwroot/', import.meta.url))
await Promise.all([
  rm(new URL('assets/', 'file://' + wwwroot.replaceAll('\\', '/')), { recursive: true, force: true }),
  rm(new URL('app/', 'file://' + wwwroot.replaceAll('\\', '/')), { recursive: true, force: true }),
])
console.log('cleaned wwwroot/assets and wwwroot/app')
