// Starts a Tangent Space for one capture run the way the test fixture does: the real server on a loopback
// port it chooses itself, over a new temp folder that holds its data, with any settings a state needs.

import { spawn, spawnSync } from 'node:child_process'
import { mkdtemp, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { onInterrupt } from './interrupt.ts'

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

/** The base URL a Tangent Space states for itself: its door, asked for as JSON at the given address. */
export async function statedBaseUrl(door: URL): Promise<URL> {
  const response = await fetch(door, { headers: { accept: 'application/json' } })
  if (!response.ok) throw new Error(`${door} answered ${response.status} ${response.statusText}.`)
  const { baseUrl } = (await response.json()) as { baseUrl?: unknown }
  if (typeof baseUrl !== 'string') throw new Error(`${door} did not answer as the door of a Tangent Space.`)
  return new URL(baseUrl)
}

/** The door's address under a base URL, keeping its path. */
export function doorOf(baseUrl: URL): URL {
  return new URL(baseUrl.pathname.replace(/\/$/, '') + '/', baseUrl)
}

export async function startTangentSpace(server: string, settings: Readonly<Record<string, string>> = {}): Promise<TangentSpace> {
  const folder = await mkdtemp(join(tmpdir(), 'tangent-capture-'))
  const all: Record<string, string> = { 'Koan:Web:ApplicationUrl': captureBaseUrl, 'Tangent:DataFolder': join(folder, 'data'), ...settings }
  // Port 0: the server binds a free port itself and reports it, so no other process can hold it in between.
  const environment: Record<string, string> = { ...(process.env as Record<string, string>), ASPNETCORE_URLS: 'http://127.0.0.1:0' }
  for (const [key, value] of Object.entries(all)) environment[key.replaceAll(':', '__')] = value

  const [command, args] = server.toLowerCase().endsWith('.dll') ? ['dotnet', [server]] : [server, []]
  const child = spawn(command, args, { cwd: folder, env: environment, stdio: ['ignore', 'pipe', 'pipe'] })
  let log = ''
  let ended = false
  let announce: (address: URL) => void = () => undefined
  const announced = new Promise<URL>((found) => (announce = found))
  const read = (chunk: Buffer) => {
    log += chunk
    const reported = /Now listening on: (http:\/\/\S+)/.exec(log)
    if (reported) announce(new URL(reported[1]))
  }
  child.stdout.on('data', read)
  child.stderr.on('data', read)
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
    forget()
    if (!ended) child.kill()
    await exited
    await rm(folder, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 })
  }
  const forget = onInterrupt(stop)

  try {
    const deadline = Date.now() + 60_000
    let timer: NodeJS.Timeout | undefined
    const listening = await Promise.race([
      announced,
      exited.then(() => Promise.reject(new Error('The Tangent Space exited before it listened.'))),
      new Promise<never>((_, reject) => (timer = setTimeout(() => reject(new Error('The Tangent Space did not listen within a minute.')), 60_000))),
    ]).finally(() => clearTimeout(timer))

    // The address answers as this run's Tangent Space: its door states the base URL this run configured.
    const baseUrl = new URL(all['Koan:Web:ApplicationUrl'])
    const door = new URL(doorOf(baseUrl).pathname, listening)
    let stated: URL | undefined
    while (!stated) {
      if (ended) throw new Error('The Tangent Space exited before its door answered.')
      try {
        stated = await statedBaseUrl(door)
      } catch (failure) {
        if (Date.now() > deadline) throw failure
        await new Promise((wait) => setTimeout(wait, 200))
      }
    }
    if (stated.href !== baseUrl.href)
      throw new Error(`${listening} states the base URL ${stated}, not ${baseUrl}: it is not this run's Tangent Space.`)
    return { baseUrl, listening, stop }
  } catch (failure) {
    await stop()
    throw new Error(`${(failure as Error).message}\n${log}`)
  }
}
