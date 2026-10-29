// Starts a Tangent Space for one capture run the way the test fixture does: the real server on a loopback
// port it chooses itself, over a new temp folder that holds its data, with any settings a state needs, and
// Koan's test connector on so a state can be captured signed in.

import { spawn, spawnSync } from 'node:child_process'
import { request as send, type IncomingHttpHeaders } from 'node:http'
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
// It is https, as atproto sign-in needs outside Development; the browser's requests to it are answered by the
// server's loopback http address through forward().
export const captureBaseUrl = 'https://tangent.localhost'

/** Builds the capture host and returns its entry assembly, or returns the published server it is given. The capture
 *  host is the server with Koan's test connector, which only TangentCaptureHost adds, built into a folder of its
 *  own under .local so neither the server's build nor its publish ever carries the connector. Its composition is
 *  read from its folder rather than written to the server's koan.lock.json. */
export function resolveServer(root: string, published?: string): string {
  // npm runs scripts in src/client; a relative path means the folder npm was started from.
  if (published) return resolve(process.env.INIT_CWD ?? process.cwd(), published)
  const project = join(root, 'src', 'server', 'Tangent.Server.csproj')
  const output = join(root, '.local', 'capture-host')
  const build = spawnSync(
    'dotnet',
    ['build', project, '-c', 'Release', '-nologo', '-t:Build', '-p:TangentCaptureHost=true', '-p:KoanComposition=false', '-o', output, '-getProperty:TargetPath'],
    { encoding: 'utf8' },
  )
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
  const all: Record<string, string> = {
    'Koan:Web:ApplicationUrl': captureBaseUrl,
    'Tangent:DataFolder': join(folder, 'data'),
    'Koan:Web:Auth:TestProvider:Enabled': 'true',
    ...settings,
  }
  // Port 0: the server binds a free port itself and reports it, so no other process can hold it in between.
  // The listening address goes on the command line, not in ASPNETCORE_URLS: Koan's Web Auth resolves the test
  // connector's token endpoint against that variable, where port 0 is no address to call.
  const environment: Record<string, string> = { ...(process.env as Record<string, string>) }
  delete environment.ASPNETCORE_URLS
  for (const [key, value] of Object.entries(all)) environment[key.replaceAll(':', '__')] = value

  const listen = '--urls=http://127.0.0.1:0'
  const [command, args] = server.toLowerCase().endsWith('.dll') ? ['dotnet', [server, listen]] : [server, [listen]]
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

export interface Forwarded {
  status: number
  headers: Record<string, string>
  body: Buffer
}

/** Sends a request the browser made to the base URL to the address the Tangent Space listens on, as it came,
 *  and answers with what the server answered, redirects included, for the browser to follow itself. */
export function forward(listening: URL, url: URL, method: string, headers: Record<string, string>, body?: Buffer): Promise<Forwarded> {
  const target = new URL(url.pathname + url.search, listening)
  const sent: Record<string, string> = { ...headers }
  delete sent.host
  return new Promise((answer, fail) => {
    const outgoing = send(target, { method, headers: sent }, (response) => {
      const chunks: Buffer[] = []
      response.on('data', (chunk: Buffer) => chunks.push(chunk))
      response.on('end', () => answer({ status: response.statusCode ?? 502, headers: flatten(response.headers), body: Buffer.concat(chunks) }))
      response.on('error', fail)
    })
    outgoing.on('error', fail)
    outgoing.end(body)
  })
}

function flatten(headers: IncomingHttpHeaders): Record<string, string> {
  const flat: Record<string, string> = {}
  // Several cookies set at once travel as one value with a line between them, as Playwright's fulfil takes them.
  for (const [name, value] of Object.entries(headers)) if (value !== undefined) flat[name] = Array.isArray(value) ? value.join('\n') : value
  return flat
}

/** Someone a state is captured as: the test connector's persona name, its subject and the handle the sign-in
 *  carries as the atproto connector's verified handle claim. */
export interface Persona {
  name: string
  did: string
  handle: string
}

/** Signs a persona in through Koan's test connector at the loopback address and answers with the cookies the
 *  sign-in leaves, for the browser to carry. */
export async function signIn(listening: URL, persona: Persona): Promise<Map<string, string>> {
  const jar = new Map([['_tp_user', encodeURIComponent(`${persona.name}|${persona.did}`)]])
  let target = new URL('/auth/test/challenge?return=' + encodeURIComponent('/sign-in'), listening)
  for (let hop = 0; hop < 10; hop++) {
    if (target.pathname.endsWith('/.testoauth/authorize')) target.searchParams.set('claim.atproto:handle', persona.handle)
    const cookie = [...jar].map(([name, value]) => `${name}=${value}`).join('; ')
    const response = await fetch(target, { redirect: 'manual', headers: { cookie } })
    for (const set of response.headers.getSetCookie()) {
      const pair = set.split(';')[0]
      const split = pair.indexOf('=')
      jar.set(pair.slice(0, split).trim(), pair.slice(split + 1))
    }
    const location = response.headers.get('location')
    if (response.status < 300 || response.status >= 400 || !location) break
    target = new URL(location, target)
  }
  const session = new Map([...jar].filter(([name, value]) => name.startsWith('.AspNetCore.Koan.cookie') && value !== ''))
  if (session.size === 0) throw new Error(`Signing ${persona.name} in through the test connector left no session cookie.`)
  return session
}

/** Establishes the place through first run as a persona, the way the page does: signs them in, reads the form's
 *  token from the first-run resource as JSON, and posts the form with the place's name. */
export async function establish(listening: URL, persona: Persona, name: string): Promise<void> {
  const jar = await signIn(listening, persona)
  const cookies = () => [...jar].map(([key, value]) => `${key}=${value}`).join('; ')
  const page = await fetch(new URL('/first-run', listening), { headers: { accept: 'application/json', cookie: cookies() } })
  if (!page.ok) throw new Error(`The first-run page answered ${page.status} ${page.statusText}.`)
  for (const set of page.headers.getSetCookie()) {
    const pair = set.split(';')[0]
    const split = pair.indexOf('=')
    jar.set(pair.slice(0, split).trim(), pair.slice(split + 1))
  }
  const { form } = (await page.json()) as { form?: { token?: { field?: string; value?: string } } }
  if (!form?.token?.field || !form.token.value) throw new Error('The first-run page holds no form: the place is established already.')
  const body = new URLSearchParams({ [form.token.field]: form.token.value, name, proprietor: 'me', handle: '' })
  const posted = await fetch(new URL('/first-run', listening), {
    method: 'POST',
    redirect: 'manual',
    headers: { cookie: cookies(), 'content-type': 'application/x-www-form-urlencoded' },
    body,
  })
  if (posted.status !== 303) throw new Error(`First run as ${persona.name} answered ${posted.status}:
${await posted.text()}`)
}
