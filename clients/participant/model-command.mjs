import { spawn } from 'node:child_process'
import { join } from 'node:path'
import { readBoundedFile } from './state.mjs'

// The PID comes directly from this spawn, never from user/conversation input.
// Unix adapters get their own process group; Windows taskkill follows this
// adapter's descendants without matching names or touching unrelated processes.
async function terminateProcessTree(child) {
  if (!Number.isSafeInteger(child.pid) || child.pid <= 0) return
  if (process.platform !== 'win32') {
    try { process.kill(-child.pid, 'SIGKILL') } catch (error) { if (error.code !== 'ESRCH') throw error }
    return
  }
  const executable = join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'taskkill.exe')
  await new Promise((resolve, reject) => {
    const cleanup = spawn(executable, ['/PID', String(child.pid), '/T', '/F'],
      { shell: false, windowsHide: true, stdio: 'ignore' })
    cleanup.on('error', reject)
    cleanup.on('close', code => {
      if (code === 0) resolve()
      else reject(new Error('Model process tree cleanup did not complete'))
    })
  })
}

// This executes only an operator-selected command file. Conversation content is
// stdin data and is never substituted into a shell or executable arguments.
export async function executeModelCommand(commandFile, input, credential, { timeoutMs = 180000 } = {}) {
  if (!Number.isSafeInteger(timeoutMs) || timeoutMs < 1 || timeoutMs > 180000) throw new Error('Model timeout must remain within three minutes')
  let command
  try { command = JSON.parse(await readBoundedFile(commandFile, 16384)) }
  catch { throw new Error('Unable to read a valid model command configuration') }
  if (typeof command.executable !== 'string' || !command.executable || !Array.isArray(command.args)
    || command.args.some(value => typeof value !== 'string') || JSON.stringify(command).includes(credential))
    throw new Error('Command configuration must contain an executable and string args without the Tangent credential')
  const environment = Object.fromEntries(Object.entries(process.env).filter(([name, value]) =>
    !/^(TANGENT|PARTICIPANT)_(TOKEN|CREDENTIAL|AUTHORIZATION)/i.test(name) && !value?.includes(credential)))
  return new Promise((resolve, reject) => {
    const child = spawn(command.executable, command.args, { shell: false, windowsHide: true, detached: process.platform !== 'win32',
      env: environment, stdio: ['pipe', 'pipe', 'ignore'] })
    const chunks = []; let size = 0; let done = false; let stopping = false
    const finish = (error, value) => { if (done) return; done = true; clearTimeout(timer); error ? reject(error) : resolve(value) }
    const stop = async error => {
      if (done || stopping) return
      stopping = true
      try { await terminateProcessTree(child); finish(error) }
      catch { finish(new Error('Model command failed and its process tree cleanup could not be confirmed')) }
    }
    const timer = setTimeout(() => void stop(new Error('Model command exceeded its time limit')), timeoutMs)
    child.on('error', () => finish(new Error('Unable to start the configured model command')))
    child.stdout.on('data', chunk => {
      size += chunk.length
      if (size > 16384) void stop(new Error('Model command output exceeded its bound'))
      else chunks.push(chunk)
    })
    child.on('close', code => {
      if (stopping) return
      if (code !== 0) return finish(new Error('Model command did not complete successfully'))
      try { finish(null, JSON.parse(Buffer.concat(chunks).toString('utf8'))) }
      catch { finish(new Error('Model command must return one JSON object')) }
    })
    child.stdin.on('error', () => void stop(new Error('Model command stopped accepting input')))
    child.stdin.end(JSON.stringify(input))
  })
}
