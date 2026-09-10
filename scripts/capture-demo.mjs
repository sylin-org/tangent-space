// Separate clean-demo screenshots. Uses a fresh headless browser context and
// genuine saved OAuth cookie; no user profile, tracing, or storage-state export.
import { readFile, mkdir, writeFile } from 'node:fs/promises'
import { resolve, join, sep } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const root = resolve(fileURLToPath(new URL('..', import.meta.url)))
const directory = resolve(process.argv[2] ?? '')
if (!directory.startsWith(resolve(root, '.local') + sep)) throw new Error('Supply the ignored prepared-demo directory')
const output = resolve(root, 'docs/evidence/demo')
let browser
try {
  const context = JSON.parse(await readFile(join(directory, 'context.json'), 'utf8'))
  if (context.site !== 'http://127.0.0.1:5220' || !context.completed || !context.agentReply) throw new Error('Prepare the default demonstration first')
  const saved = JSON.parse(await readFile(join(directory, 'owner.cookies.json'), 'utf8'))
  if (saved.origin !== context.site || saved.did !== context.ownerDid) throw new Error('Cookie and demo identity differ')
  const { chromium } = await import(pathToFileURL(resolve(root, '.local/ui-proof/node_modules/playwright-core/index.mjs')).href)
  await mkdir(output, { recursive: true })
  browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true })
  const owner = await browser.newContext({ viewport: { width: 1440, height: 1100 }, locale: 'en-US', acceptDownloads: false })
  await owner.addCookies(saved.cookies.map(([name, value]) => ({ name, value, url: context.site, httpOnly: true, secure: false, sameSite: 'Lax' })))
  const page = await owner.newPage()
  await page.goto(context.links.workshop, { waitUntil: 'networkidle' })
  await page.getByText(context.agentReply.text, { exact: true }).waitFor({ state: 'visible' })
  const visibleMessages = await page.locator('#messages .message').count()
  if (visibleMessages !== 2 || await page.locator('#room-list .room-link').count() !== 2) throw new Error('Expected exactly two demo rooms and the human/model exchange')
  await page.screenshot({ path: join(output, 'workshop-desktop.png'), fullPage: true, animations: 'disabled' })
  await page.setViewportSize({ width: 390, height: 844 })
  const width = await page.evaluate(() => ({ viewport: innerWidth, document: document.documentElement.scrollWidth }))
  if (width.document > width.viewport + 1) throw new Error('Clean demo exceeds the mobile viewport')
  await page.screenshot({ path: join(output, 'workshop-mobile.png'), fullPage: true, animations: 'disabled' })
  const anonymous = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'en-US', acceptDownloads: false })
  const landing = await anonymous.newPage()
  await landing.goto(context.links.lounge, { waitUntil: 'networkidle' })
  await landing.locator('#state-anonymous').waitFor({ state: 'visible' })
  await landing.screenshot({ path: join(output, 'arrival-desktop.png'), fullPage: true, animations: 'disabled' })
  const receipt = { capturedAt: new Date().toISOString(), origin: context.site, rooms: context.rooms,
    browserVersion: browser.version(), visibleMessages, viewportWidth: width,
    screenshots: ['workshop-desktop.png', 'workshop-mobile.png', 'arrival-desktop.png'],
    actualModel: context.agentReply.model, modelSourceCid: context.agentReply.sourceCid,
    note: 'Separate clean demonstration captures; the original UI integration proof receipt is unchanged.' }
  await writeFile(join(output, 'captures.json'), JSON.stringify(receipt, null, 2) + '\n')
  console.log(JSON.stringify(receipt))
} catch {
  console.error('Clean demo screenshot capture failed; no browser headers or private state were logged.'); process.exitCode = 1
} finally { if (browser) await browser.close() }
