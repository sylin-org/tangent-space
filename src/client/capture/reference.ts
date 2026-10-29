// The design reference package beside the captures. It is configured, never part of the tree: the folder
// named by TANGENT_REFERENCE, or .dream-loop/reference at the repository root.

import { existsSync } from 'node:fs'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import type { Browser } from 'playwright'
import type { CaptureTheme, Width } from './screens.ts'

export type ReferencePackage = { folder: string } | { missing: string }

export function referencePackage(root: string): ReferencePackage {
  const named = process.env.TANGENT_REFERENCE
  const folder = named || join(root, '.dream-loop', 'reference')
  if (existsSync(join(folder, 'screens'))) return { folder }
  const how =
    'Put the design reference package (the folder holding screens/, spec/ and tokens/) at ' +
    `${join(root, '.dream-loop', 'reference')}, or set TANGENT_REFERENCE to its folder.`
  return { missing: named ? `TANGENT_REFERENCE is ${named}, which has no screens/ folder. ${how}` : `The design reference is not configured. ${how}` }
}

// The reference asks Google Fonts for its three typefaces; the client's bundled files answer instead,
// so the reference renders offline in the same fonts the pages use.
const fontsHost = 'https://fonts.gstatic.com/bundled/'
const clientFolder = new URL('../', import.meta.url)

/** Renders one reference screen, without the reference viewer's bar, at the size it is drawn. */
export async function renderReference(
  browser: Browser,
  folder: string,
  file: string,
  width: Width,
  theme: CaptureTheme,
  output: string,
): Promise<void> {
  const fonts = (await readFile(new URL('fonts.css', clientFolder), 'utf8')).replaceAll('./fonts/', fontsHost)
  const context = await browser.newContext({
    viewport: { width, height: 844 },
    deviceScaleFactor: 1,
    colorScheme: theme,
    reducedMotion: 'reduce',
  })
  try {
    await context.route('**/*', (route) => {
      const url = route.request().url()
      if (url.startsWith('https://fonts.googleapis.com/'))
        return route.fulfill({ contentType: 'text/css', headers: { 'access-control-allow-origin': '*' }, body: fonts })
      if (url.startsWith(fontsHost))
        return route.fulfill({
          contentType: 'font/woff2',
          headers: { 'access-control-allow-origin': '*' },
          path: fileURLToPath(new URL(`fonts/${url.slice(fontsHost.length)}`, clientFolder)),
        })
      return url.startsWith('file:') ? route.fallback() : route.abort('blockedbyclient')
    })
    const page = await context.newPage()
    const screen = pathToFileURL(join(folder, 'screens', file))
    screen.search = new URLSearchParams({ theme, embed: '1' }).toString()
    await page.goto(screen.href, { waitUntil: 'networkidle' })
    await page.evaluate(() => document.fonts.ready.then(() => undefined))
    await page.locator('.ref-stage > div').screenshot({ path: output, animations: 'disabled', caret: 'hide' })
  } finally {
    await context.close()
  }
}
