// npm run capture -- <screen> [--url <base URL>] [--server <published Tangent.Server>]
// Renders one screen at every width, theme and state the registry declares into the next round folder,
// .dream-loop/<screen>/<n>/, with the design reference's screen beside it where the reference has one.

import { mkdir, readdir, rm, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { parseArgs } from 'node:util'
import { chromium, type Browser } from 'playwright'
import { storageKey } from '../appearance.ts'
import { referencePackage, renderReference } from './reference.ts'
import { captureThemes, references, screens, widths, type Screen, type State, type Width } from './screens.ts'
import { resolveServer, startTangentSpace } from './server.ts'

const root = fileURLToPath(new URL('../../../', import.meta.url))
const viewportHeight: Record<Width, number> = { 1440: 960, 1024: 768, 390: 844 }

const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: { url: { type: 'string' }, server: { type: 'string' } },
})
const name = positionals[0]
const screen = name ? screens[name] : undefined
const reference = name ? references[name] : undefined
if (!name || positionals.length > 1 || (!screen && !reference)) {
  console.error('Usage: npm run capture -- <screen> [--url <base URL>] [--server <published Tangent.Server>]')
  console.error(`Screens: ${Object.keys(screens).join(', ')}.`)
  console.error(`In the reference, with no page yet: ${Object.keys(references).filter((key) => !screens[key]).join(', ')}.`)
  process.exit(2)
}
const referenceFolder = referencePackage(root)
if (!('folder' in referenceFolder) && reference) console.error(referenceFolder.missing)
if (!screen && !('folder' in referenceFolder)) process.exit(2)

// The next round: one more than the highest round already written for this screen.
const screenFolder = join(root, '.dream-loop', name)
await mkdir(screenFolder, { recursive: true })
const rounds = (await readdir(screenFolder)).filter((entry) => /^\d+$/.test(entry)).map(Number)
const round = Math.max(0, ...rounds) + 1
const folder = join(screenFolder, String(round))
await mkdir(folder)

try {
  const notes = [`# ${name}, round ${round}`, '']
  if (screen) notes.push(...(await captureScreen(screen)), '')
  else notes.push(`Tangent has no page for ${name} yet: this round holds the reference alone.`, '')
  notes.push(...(await captureReference()))
  await writeFile(join(folder, 'README.md'), notes.join('\n') + '\n')
} catch (failure) {
  // A failed round leaves nothing behind, so the round numbers count finished rounds.
  await rm(folder, { recursive: true, force: true })
  throw failure
}
console.log(`Round ${round} of ${name}: ${folder}`)

async function captureScreen(screen: Screen): Promise<string[]> {
  if (screen.states.some((state) => state.name === 'reference'))
    throw new Error('No state is named reference; that name marks the reference captures.')
  // States that need the same settings share one Tangent Space.
  const groups = new Map<string, State[]>()
  for (const state of screen.states) {
    const key = JSON.stringify(state.settings ?? {})
    groups.set(key, [...(groups.get(key) ?? []), state])
  }
  if (values.url && [...groups.keys()].some((key) => key !== '{}'))
    throw new Error('A state of this screen needs settings the Tangent Space starts with; capture it without --url.')
  const server = values.url ? undefined : resolveServer(root, values.server)

  for (const [settings, states] of groups) {
    const tangentSpace = server ? await startTangentSpace(server, JSON.parse(settings)) : undefined
    try {
      const baseUrl = tangentSpace?.baseUrl ?? new URL(values.url!)
      // The browser asks for the base URL; a started Tangent Space answers it on its own loopback port.
      const args = tangentSpace ? [`--host-resolver-rules=MAP ${baseUrl.host} ${tangentSpace.listening.host}`] : []
      const browser = await chromium.launch({ args })
      try {
        for (const state of states) await captureState(browser, baseUrl, screen.path, state)
      } finally {
        await browser.close()
      }
    } finally {
      await tangentSpace?.stop()
    }
  }
  return [
    `Captures of ${screen.path} at ${widths.join(', ')} wide, light and dark, named <width>-<theme>-<state>.png.`,
    `States: ${screen.states.map((state) => state.name).join(', ')}.`,
    values.url ? `Tangent Space: the one running at ${values.url}.` : 'Tangent Space: started for this round over a new data folder.',
  ]
}

async function captureState(browser: Browser, baseUrl: URL, path: string, state: State): Promise<void> {
  const target = new URL(baseUrl.pathname.replace(/\/$/, '') + path, baseUrl)
  for (const [key, value] of Object.entries(state.query ?? {})) target.searchParams.set(key, value)
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
        await context.route('**/*', (route) =>
          new URL(route.request().url()).origin === baseUrl.origin ? route.fallback() : route.abort('blockedbyclient'),
        )
        const page = await context.newPage()
        let opened: string | undefined
        page.on('dialog', (dialog) => {
          opened = dialog.message()
          void dialog.dismiss()
        })
        await page.goto(target.href, { waitUntil: 'networkidle' })
        if (state.click) {
          await page.click(state.click)
          await page.waitForLoadState('networkidle')
        }
        await page.evaluate(() => document.fonts.ready.then(() => undefined))
        if (opened !== undefined) throw new Error(`${target} opened a dialog ("${opened}"); a page never does.`)
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
  if (!reference) return [`The design reference has no screen for ${name}; its target is the mock Leo approves.`]
  if (!('folder' in referenceFolder))
    return [`The design reference has a screen for ${name}. ${referenceFolder.missing}`]
  const notes = [`Reference, from ${referenceFolder.folder}, named <width>-<theme>-reference.png:`, '']
  const browser = await chromium.launch()
  try {
    for (const width of widths)
      for (const theme of captureThemes) {
        const file = reference[width]
        if (!file) {
          notes.push(`- ${width} ${theme}: the reference has no screen at this width.`)
          continue
        }
        const output = `${width}-${theme}-reference.png`
        await renderReference(browser, referenceFolder.folder, file, width, theme, join(folder, output))
        notes.push(`- ${width} ${theme}: ${output}, from screens/${file}.`)
      }
  } finally {
    await browser.close()
  }
  return notes
}
