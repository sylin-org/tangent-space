import test from 'node:test'
import assert from 'node:assert/strict'
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, join, resolve, sep } from 'node:path'
import { setTimeout as pause } from 'node:timers/promises'
import { executeModelCommand } from './model-command.mjs'

test('model timeout terminates its adapter and spawned descendant', { timeout: 15000 }, async () => {
  const directory = await mkdtemp(join(tmpdir(), 'tangent-model-timeout-'))
  const pidsPath = join(directory, 'pids.json')
  const adapterPath = join(directory, 'adapter.mjs')
  const commandPath = join(directory, 'command.json')
  await writeFile(adapterPath, `import { spawn } from 'node:child_process';
import { writeFileSync } from 'node:fs';
const descendant = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], { windowsHide: true, stdio: 'ignore' });
writeFileSync(process.argv[2], JSON.stringify([process.pid, descendant.pid]));
process.stdin.resume(); setInterval(() => {}, 1000);
`)
  await writeFile(commandPath, JSON.stringify({ executable: process.execPath, args: [adapterPath, pidsPath] }))
  let pids = []; let cleanupNeeded = true
  try {
    await assert.rejects(executeModelCommand(commandPath, { messages: [] }, 'ts_' + 'a'.repeat(43), { timeoutMs: 2000 }), /exceeded its time limit/)
    pids = JSON.parse(await readFile(pidsPath, 'utf8'))
    assert.equal(pids.length, 2)
    for (const pid of pids) {
      let alive = true
      for (let attempt = 0; attempt < 20 && alive; attempt++) {
        try { process.kill(pid, 0); await pause(50) } catch (error) { if (error.code === 'ESRCH') alive = false; else throw error }
      }
      assert.equal(alive, false, 'Only the adapter-created processes must be gone after timeout')
    }
    cleanupNeeded = false
  } finally {
    // This cleanup uses only the two PIDs created and recorded by this test.
    if (cleanupNeeded) {
      if (pids.length === 0) try { pids = JSON.parse(await readFile(pidsPath, 'utf8')) } catch {}
      for (const pid of pids) try { process.kill(pid, 'SIGKILL') } catch {}
    }
    const target = resolve(directory)
    assert.ok(target.startsWith(resolve(tmpdir()) + sep) && basename(target).startsWith('tangent-model-timeout-'))
    await rm(target, { recursive: true, force: true })
  }
})
