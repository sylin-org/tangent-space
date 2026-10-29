// Starts a Tangent Space for one capture run the way the test fixture does: the real server on a free
// loopback port, over a new temp folder that holds its data, with any Tangent:* settings a state needs.

import { spawn, spawnSync } from 'node:child_process'
import { mkdtemp, rm } from 'node:fs/promises'
import { createServer } from 'node:net'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'

export interface TangentSpace {
  /** The base URL the pages are rendered with and the browser asks for. */
  baseUrl: URL
  /** Where the base URL's host and port actually answer. */
  listening: URL
  stop(): Promise<void>
}

// Every capture shows this base URL whatever port the server listens on, so captures do not differ by port.
export const captureBaseUrl = 'http://tangent.localhost'

/** Builds the server project and returns its entry assembly, or returns the published server it is given. */
export function resolveServer(root: string, published?: string): string {
  // npm runs scripts in src/client; a relative path means the folder npm was started from.
  if (published) return resolve(process.env.INIT_CWD ?? process.cwd(), published)
  const project = join(root, 'src', 'server', 'Tangent.Server.csproj')
  const build = spawnSync('dotnet', ['build', project, '-c', 'Release', '-nologo', '-t:Build', '-getProperty:TargetPath'], {
    encoding: 'utf8',
  })
  if (build.status !== 0) throw new Error(`Building ${project} failed:\n${build.stdout}${build.stderr}`)
  return build.stdout.trim()
}

function freePort(): Promise<number> {
  return new Promise((found, reject) => {
    const probe = createServer()
    probe.once('error', reject)
    probe.listen(0, '127.0.0.1', () => {
      const address = probe.address()
      probe.close(() => (typeof address === 'object' && address ? found(address.port) : reject(new Error('No free port.'))))
    })
  })
}

export async function startTangentSpace(server: string, settings: Readonly<Record<string, string>> = {}): Promise<TangentSpace> {
  const folder = await mkdtemp(join(tmpdir(), 'tangent-capture-'))
  const port = await freePort()
  const listening = new URL(`http://127.0.0.1:${port}`)
  const all: Record<string, string> = { 'Tangent:BaseUrl': captureBaseUrl, 'Tangent:DataFolder': join(folder, 'data'), ...settings }
  const environment: Record<string, string> = { ...process.env as Record<string, string>, ASPNETCORE_URLS: listening.origin }
  for (const [key, value] of Object.entries(all)) environment[key.replaceAll(':', '__')] = value

  const [command, args] = server.toLowerCase().endsWith('.dll') ? ['dotnet', [server]] : [server, []]
  const child = spawn(command, args, { cwd: folder, env: environment, stdio: ['ignore', 'pipe', 'pipe'] })
  let log = ''
  let ended = false
  child.stdout.on('data', (chunk) => (log += chunk))
  child.stderr.on('data', (chunk) => (log += chunk))
  const exited = new Promise<void>((done) => {
    const end = (failure?: Error) => {
      if (failure) log += `${failure.message}\n`
      ended = true
      done()
    }
    child.once('exit', () => end())
    child.once('error', end)
  })

  const stop = async () => {
    if (!ended) child.kill()
    await exited
    await rm(folder, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 })
  }

  const baseUrl = new URL(all['Tangent:BaseUrl'])
  const health = new URL(`${baseUrl.pathname.replace(/\/$/, '')}/health/live`, listening)
  const deadline = Date.now() + 60_000
  for (;;) {
    if (ended) {
      await stop()
      throw new Error(`The Tangent Space exited before it answered:\n${log}`)
    }
    try {
      if ((await fetch(health)).ok) break
    } catch {}
    if (Date.now() > deadline) {
      await stop()
      throw new Error(`The Tangent Space did not answer ${health} within a minute:\n${log}`)
    }
    await new Promise((wait) => setTimeout(wait, 200))
  }
  return { baseUrl, listening, stop }
}
