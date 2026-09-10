import { mkdir, open, readFile, rename, unlink, stat } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'

export async function readBoundedFile(path, maximum = 65536) {
  if ((await stat(path)).size > maximum) throw new Error('Local input file exceeds its supported size')
  return readFile(path, 'utf8')
}

export async function saveState(path, value) {
  const target = resolve(path)
  await mkdir(dirname(target), { recursive: true })
  const temporary = target + `.tmp-${process.pid}`
  const file = await open(temporary, 'w', 0o600)
  try { await file.writeFile(JSON.stringify(value, null, 2)); await file.sync() } finally { await file.close() }
  await rename(temporary, target)
}

export async function withState(path, site, room, action) {
  const target = resolve(path)
  await mkdir(dirname(target), { recursive: true })
  const lockPath = target + '.lock'
  let lock
  try { lock = await open(lockPath, 'wx', 0o600) }
  catch (error) {
    if (error.code !== 'EEXIST') throw error
    throw new Error('Runner state is locked; if its previous process crashed, confirm it stopped before removing the adjacent .lock file')
  }
  await lock.writeFile(String(process.pid))
  try {
    let state
    try { state = JSON.parse(await readBoundedFile(target)) }
    catch (error) { if (error.code !== 'ENOENT') throw error; state = { version: 1, site, room, cursor: null, pending: null, ackPending: null } }
    if (state.version !== 1 || state.site !== site || state.room !== room) throw new Error('State belongs to another site or room')
    return await action(state, () => saveState(target, state))
  } finally { await lock.close(); await unlink(lockPath) }
}
