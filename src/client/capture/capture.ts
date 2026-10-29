// npm run capture -- <screen> [--url <base URL>] [--server <published Tangent.Server>]
// Renders one screen at every width, theme and state the registry declares into the next round folder,
// .dream-loop/<screen>/<n>/, with the design reference's screen beside it where the reference has one.

import { mkdir, readdir, rename, rm, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { parseArgs } from 'node:util'
import { chromium, type Browser } from 'playwright'
import { storageKey } from '../appearance.ts'
import { interrupted, onInterrupt } from './interrupt.ts'
import { referencePackage, renderReference } from './reference.ts'
import { captureOperator, capturePersona, setupCodeFill, capturePlaceName, captureThemes, nearestReadings, referenceVariants, references, screens, widths, type Screen, type State, type Width } from './screens.ts'
import { doorOf, establish, forward, resolveServer, seed, signIn, startTangentSpace, statedBaseUrl } from './server.ts'

const root = fileURLToPath(new URL('../../../', import.meta.url))
const viewportHeight: Record<Width, number> = { 1440: 960, 1024: 768, 390: 844 }
// Chromium's network errors for an address it could not reach or that never answered.
const unreachable = /^net::ERR_(CONNECTION_|NAME_|ADDRESS_|TIMED_OUT|EMPTY_RESPONSE|INTERNET_|NETWORK_)/

const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: { url: { type: 'string' }, server: { type: 'string' } },
})
const name = positionals[0]
const screen = name ? screens[name] : undefined
const reference = name ? references[name] : undefined
const variants = name ? referenceVariants[name] : undefined
if (!name || positionals.length > 1 || (!screen && !reference && !variants)) {
  console.error('Usage: npm run capture -- <screen> [--url <base URL>] [--server <published Tangent.Server>]')
  console.error(`Screens: ${Object.keys(screens).join(', ')}.`)
  console.error(`In the reference, with no page yet: ${Object.keys(references).filter((key) => !screens[key]).join(', ')}.`)
  process.exit(2)
}
const referenceFolder = referencePackage(root)
if (!('folder' in referenceFolder) && (reference || variants)) console.error(referenceFolder.missing)
if (!screen && !('folder' in referenceFolder)) process.exit(2)

// The next round: one more than the highest finished round of this screen. It is written as <n>.partial
// and becomes <n> only when it is complete, so an unfinished round is never counted or handed to a scorer.
const screenFolder = join(root, '.dream-loop', name)
await mkdir(screenFolder, { recursive: true })
const rounds = (await readdir(screenFolder)).filter((entry) => /^\d+$/.test(entry)).map(Number)
const round = Math.max(0, ...rounds) + 1
const finished = join(screenFolder, String(round))
const folder = `${finished}.partial`
try {
  await mkdir(folder)
} catch (failure) {
  if ((failure as NodeJS.ErrnoException).code !== 'EEXIST') throw failure
  throw new Error(`${folder} holds an unfinished round: another capture of ${name} is running, or one was stopped. Delete it if none is running.`)
}
const forgetRound = onInterrupt(() => rm(folder, { recursive: true, force: true }))

try {
  const notes = [`# ${name}, round ${round}`, '']
  if (screen) notes.push(...(await captureScreen(screen)), '')
  else notes.push(`Tangent has no page for ${name} yet: this round holds the reference alone.`, '')
  notes.push(...(await captureReference()))
  await writeFile(join(folder, 'README.md'), notes.join('\n') + '\n')
  await rename(folder, finished)
} catch (failure) {
  // Under an interrupt, what failed is the interrupt's doing; its handler undoes everything and exits.
  const stopping = interrupted()
  if (stopping) await stopping
  // A failed round leaves nothing behind, so the round numbers count finished rounds.
  await rm(folder, { recursive: true, force: true })
  throw failure
} finally {
  forgetRound()
}
console.log(`Round ${round} of ${name}: ${finished}`)

async function launch(args: string[] = []): Promise<{ browser: Browser; close(): Promise<void> }> {
  // The run handles interrupts itself, so Playwright does not exit before the round and the server are undone.
  // Text is smoothed in greyscale: subpixel smoothing fringes glyphs with colours no token holds, and a scorer
  // reading the captures would take them for the page's own.
  const browser = await chromium.launch({ args: ['--disable-lcd-text', ...args], handleSIGINT: false, handleSIGTERM: false })
  const forget = onInterrupt(() => browser.close())
  return { browser, close: () => (forget(), browser.close()) }
}

async function captureScreen(screen: Screen): Promise<string[]> {
  if (screen.states.some((state) => state.name === 'reference'))
    throw new Error('No state is named reference; that name marks the reference captures.')
  // States that need the same settings, the same place and the same seed share one Tangent Space.
  const groups = new Map<string, State[]>()
  for (const state of screen.states) {
    const key = JSON.stringify({ settings: state.settings ?? {}, place: state.place ?? 'established', seed: state.seed ?? null })
    groups.set(key, [...(groups.get(key) ?? []), state])
  }
  if (values.url && screen.states.some((state) => state.settings || (state.place ?? 'established') !== 'established'))
    throw new Error('A state of this screen needs settings or a place the Tangent Space starts with; capture it without --url.')
  if (values.url && screen.states.some((state) => state.seed))
    throw new Error('A state of this screen needs the rows the capture host seeds; capture it without --url.')
  const server = values.url ? undefined : resolveServer(root, values.server)

  for (const [key, states] of groups) {
    const group = JSON.parse(key) as { settings: Record<string, string>; place: NonNullable<State['place']>; seed: string | null }
    const tangentSpace = server ? await startTangentSpace(server, group.settings) : undefined
    try {
      // First run comes first: a seed writes into an established place.
      if (tangentSpace && group.place !== 'unestablished')
        await establish(tangentSpace.listening, group.place === 'persona' ? capturePersona : captureOperator, screen.placeName ?? capturePlaceName, tangentSpace.setupCode)
      if (tangentSpace && group.seed) await seed(tangentSpace.listening, tangentSpace.baseUrl, group.seed)
      // A started Tangent Space is asked for at its base URL, which the browser maps to its loopback port.
      // A running one is asked for where --url says, and its pages link to the base URL it states.
      const pageBase = tangentSpace?.baseUrl ?? new URL(values.url!)
      const baseUrl = tangentSpace?.baseUrl ?? (await statedBaseUrl(doorOf(pageBase)))
      const args = tangentSpace ? [`--host-resolver-rules=MAP ${baseUrl.host} ${tangentSpace.listening.host}`] : []
      const { browser, close } = await launch(args)
      try {
        for (const state of states)
          await captureState(browser, pageBase, baseUrl, state.path ?? screen.path, state, tangentSpace?.listening, tangentSpace?.setupCode)
      } finally {
        await close()
      }
    } finally {
      await tangentSpace?.stop()
    }
  }
  return [
    `Captures of ${screen.path} at ${widths.join(', ')} wide, light and dark, named <width>-<theme>-<state>.png.`,
    `States: ${screen.states.map((state) => (state.path ? `${state.name} (${state.path})` : state.name)).join(', ')}.`,
    values.url ? `Tangent Space: the one running at ${values.url}.` : 'Tangent Space: started for this round over a new data folder.',
  ]
}

// A started Tangent Space answers the browser's requests to its base URL through its loopback address
// (`listening`); one already running is asked for where --url says.
async function captureState(browser: Browser, pageBase: URL, baseUrl: URL, path: string, state: State, listening?: URL, setupCode?: string): Promise<void> {
  if (state.signedIn && !listening) throw new Error(`The ${state.name} state signs in through the test connector; capture it without --url.`)
  const target = new URL(pageBase.pathname.replace(/\/$/, '') + path, pageBase)
  for (const [key, value] of Object.entries(state.query ?? {})) target.searchParams.set(key, value)
  const tangentSpace = new Set([pageBase.origin, baseUrl.origin])
  for (const width of widths)
    for (const theme of captureThemes) {
      const context = await browser.newContext({
        viewport: { width, height: viewportHeight[width] },
        deviceScaleFactor: 1,
        colorScheme: theme,
        reducedMotion: 'reduce',
      })
      try {
        // The theme a participant chose, for pages that restore it, besides the device's colour scheme.
        await context.addInitScript(([key, value]) => localStorage.setItem(key, value), [storageKey, theme] as const)
        // Only the Tangent Space answers; anything else the page asks for is refused, so nothing else is fetched.
        await context.route('**/*', async (route) => {
          const request = route.request()
          const url = new URL(request.url())
          if (!tangentSpace.has(url.origin)) return route.abort('blockedbyclient')
          if (state.cancel && url.pathname === pageBase.pathname.replace(/\/$/, '') + state.cancel) return route.abort('aborted')
          if (!listening) return route.fallback()
          const answered = await forward(listening, url, request.method(), await request.allHeaders(), request.postDataBuffer() ?? undefined)
          return route.fulfill(answered)
        })
        if (state.signedIn && listening) {
          const session = await signIn(listening, capturePersona)
          await context.addCookies([...session].map(([name, value]) => ({ name, value, domain: baseUrl.hostname, path: '/', httpOnly: true })))
        }
        const page = await context.newPage()
        let opened: string | undefined
        page.on('dialog', (dialog) => {
          opened = dialog.message()
          void dialog.dismiss()
        })
        // A request to the Tangent Space that failed on the network means the browser could not reach it.
        // One the server answered and Chromium then dropped (an error page for a stylesheet or a script) is
        // the page as it is, and it is captured as it is.
        const unanswered: string[] = []
        page.on('requestfailed', (request) => {
          const failure = request.failure()?.errorText ?? ''
          if (tangentSpace.has(new URL(request.url()).origin) && unreachable.test(failure))
            unanswered.push(`${request.url()}: ${failure}`)
        })

        await page.goto(target.href, { waitUntil: 'networkidle' })
        for (const selector of state.check ?? []) await page.check(selector)
        for (const [selector, value] of Object.entries(state.fill ?? {})) {
          if (value === setupCodeFill && !setupCode) throw new Error(`The ${state.name} state types the setup code, and the server logged none.`)
          await page.fill(selector, value === setupCodeFill ? setupCode! : value)
        }
        if (state.click) {
          await page.click(state.click)
          await page.waitForLoadState('networkidle')
        }
        await page.evaluate(() => document.fonts.ready.then(() => undefined))

        if (opened !== undefined) throw new Error(`${target} opened a dialog ("${opened}"); a page never does.`)
        if (await page.evaluate(() => document.querySelector('dialog[open]') !== null))
          throw new Error(`${target} has an open dialog element; a page never does.`)
        if (unanswered.length > 0)
          throw new Error(
            `The Tangent Space did not answer these requests, so the capture would not show its page; the browser must reach ${pageBase.origin} and its base URL ${baseUrl.origin}:\n${unanswered.join('\n')}`,
          )
        // A page that opens on a field further down (autofocus on the field a refusal concerns) has scrolled to it;
        // the whole page is captured from its top, so the sticky masthead sits where it does at the top.
        await page.evaluate(() => window.scrollTo(0, 0))
        await page.screenshot({
          path: join(folder, `${width}-${theme}-${state.name}.png`),
          fullPage: true,
          animations: 'disabled',
          caret: 'hide',
        })
      } finally {
        await context.close()
      }
    }
}

async function captureReference(): Promise<string[]> {
  if (variants) {
    if (!('folder' in referenceFolder)) return [`The design reference has screens for ${name}. ${referenceFolder.missing}`]
    const notes = [`Reference, from ${referenceFolder.folder}, named <width>-<theme>-reference-<variant>.png:`, '']
    for (const [variant, entry] of Object.entries(variants)) {
      notes.push(`${variant}:`)
      notes.push(...(await renderReferences(references[entry] ?? {}, `-${variant}`)))
    }
    return notes
  }
  if (!reference) {
    const nearest = nearestReadings[name]
    return [
      `The design reference has no screen for ${name}; its target is the mock Leo approves.`,
      ...(nearest
        ? ['', `The nearest reading in the reference, for whoever draws that mock: ${nearest.map((file) => `screens/${file}`).join(' and ')}. They are not rendered here and are not the target.`]
        : []),
    ]
  }
  if (!('folder' in referenceFolder)) return [`The design reference has a screen for ${name}. ${referenceFolder.missing}`]
  return [`Reference, from ${referenceFolder.folder}, named <width>-<theme>-reference.png:`, '', ...(await renderReferences(reference, ''))]
}

// Renders a reference's screen at each width and theme it is drawn at, its file names carrying the suffix.
async function renderReferences(reference: Partial<Record<Width, string>>, suffix: string): Promise<string[]> {
  if (!('folder' in referenceFolder)) return []
  const notes: string[] = []
  const { browser, close } = await launch()
  try {
    for (const width of widths)
      for (const theme of captureThemes) {
        const file = reference[width]
        if (!file) {
          notes.push(`- ${width} ${theme}: the reference has no screen at this width.`)
          continue
        }
        const output = `${width}-${theme}-reference${suffix}.png`
        await renderReference(browser, referenceFolder.folder, file, width, theme, join(folder, output))
        notes.push(`- ${width} ${theme}: ${output}, from screens/${file}.`)
      }
  } finally {
    await close()
  }
  return notes
}
