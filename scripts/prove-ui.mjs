import { readFile, writeFile, mkdir, access } from 'node:fs/promises';
import { resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

// Real, isolated headless Chrome UI proof. No API interception, user profile,
// storage-state export, tracing, or password automation. OAuth was proved earlier.
// Run only when the shared host is stable: node scripts/prove-ui.mjs
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const args = new Map();
for (let index = 2; index < process.argv.length; index += 2) {
  if (!process.argv[index]?.startsWith('--') || !process.argv[index + 1]) throw new Error('Use --name value arguments.');
  args.set(process.argv[index].slice(2), process.argv[index + 1]);
}
const origin = args.get('origin') ?? 'http://127.0.0.1:5223';
if (!/^http:\/\/127\.0\.0\.1:\d+$/.test(origin)) throw new Error('Use the disposable loopback host origin.');
const roomKey = args.get('room-key') ?? `ui-${Date.now()}`;
const readOnlyRoom = args.get('read-room') ?? 'poc-1788987688033-workshop';
if (!/^ui-[a-z0-9-]{1,55}$/.test(roomKey) || !/^[a-z0-9-]{1,64}$/.test(readOnlyRoom)) throw new Error('Invalid proof room key.');
const cookieFile = resolve(root, args.get('cookie-file') ?? '.local/tangent/1788984280268/rooms-proof/room-proof-poc-1788987688033/owner.cookies.json');
const ignoredRoot = resolve(root, '.local');
if (!cookieFile.startsWith(ignoredRoot + '/') && !cookieFile.startsWith(ignoredRoot + '\\')) throw new Error('Read cookies only from ignored local state.');
const outputDirectory = resolve(root, 'docs/evidence/ui');
const evidenceFile = resolve(root, 'docs/evidence/ui.json');
const checks = [];
const screenshots = [];
const diagnostics = { pageErrors: [], consoleErrors: 0, consoleLocations: [], failedRequests: [], failedResponses: [], assets: {} };
let stage = 'initialize';
let browser;
let ownerPage;
let browserVersion;
let completed = false;

function check(name, passed, observed = {}) {
  checks.push({ name, passed: Boolean(passed), observed });
  if (!passed) throw new Error('ProofCheckFailed');
}
function observe(page, identity) {
  page.on('pageerror', error => diagnostics.pageErrors.push({ identity, name: /^[a-zA-Z]+$/.test(error.name) ? error.name : 'PageError' }));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    diagnostics.consoleErrors++;
    let path = null;
    try { path = new URL(message.location().url).pathname; } catch { /* No remote console text is retained. */ }
    diagnostics.consoleLocations.push({ identity, path, line: message.location().lineNumber,
      kind: message.text().startsWith('Failed to load resource:') ? 'ResourceLoadFailure' : 'ConsoleError' });
  });
  page.on('requestfailed', request => {
    const url = new URL(request.url());
    diagnostics.failedRequests.push({ identity, origin: url.origin, path: url.pathname, type: request.resourceType() });
  });
  page.on('response', response => {
    const url = new URL(response.url());
    if (url.origin !== origin) return;
    if (response.status() >= 400) diagnostics.failedResponses.push({ identity, path: url.pathname, status: response.status() });
    if (['/app.css', '/rooms.css', '/app.js', '/rooms.js'].includes(url.pathname)) diagnostics.assets[url.pathname] = response.status();
  });
  page.setDefaultTimeout(20000);
  page.setDefaultNavigationTimeout(30000);
}
async function screenshot(page, name) {
  await page.screenshot({ path: resolve(outputDirectory, name), fullPage: true, animations: 'disabled' });
  screenshots.push(`docs/evidence/ui/${name}`);
}
async function overflow(page, label) {
  const measured = await page.evaluate(() => ({ viewport: innerWidth, document: document.documentElement.scrollWidth, body: document.body.scrollWidth }));
  check(`${label} has no horizontal page overflow`, measured.document <= measured.viewport + 1 && measured.body <= measured.viewport + 1, measured);
}
async function api(context, path) {
  const response = await context.request.get(origin + path, { timeout: 30000 });
  let body;
  try { body = await response.json(); } catch { body = null; }
  return { status: response.status(), body };
}
async function mutation(page, path, method, interaction) {
  const waiting = page.waitForResponse(response => response.url() === origin + path && response.request().method() === method, { timeout: 90000 });
  await interaction();
  const response = await waiting;
  let body;
  try { body = await response.json(); } catch { body = null; }
  return { status: response.status(), body };
}
async function visible(page, selector) { await page.locator(selector).waitFor({ state: 'visible' }); }

try {
  await access(resolve(root, 'src/TangentSpace/wwwroot/rooms.css'));
  await mkdir(outputDirectory, { recursive: true });
  const saved = JSON.parse(await readFile(cookieFile, 'utf8'));
  if (saved.origin !== origin || typeof saved.did !== 'string' || !Array.isArray(saved.cookies)
      || saved.cookies.length < 1 || saved.cookies.some(pair => !Array.isArray(pair) || pair.length !== 2 || pair.some(value => typeof value !== 'string')))
    throw new Error('InvalidCookieFixture');
  const { chromium } = await import(pathToFileURL(resolve(root, '.local/ui-proof/node_modules/playwright-core/index.mjs')).href);
  const packageVersion = JSON.parse(await readFile(resolve(root, '.local/ui-proof/node_modules/playwright-core/package.json'), 'utf8')).version;
  browser = await chromium.launch({ executablePath: args.get('chrome') ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
  browserVersion = browser.version();
  const anonymous = await browser.newContext({ viewport: { width: 1440, height: 1100 }, locale: 'en-US', acceptDownloads: false });
  const anonPage = await anonymous.newPage();
  observe(anonPage, 'anonymous');

  stage = 'anonymous-arrival';
  const health = await api(anonymous, '/health/ready');
  check('Real host readiness endpoint is healthy', health.status === 200, { status: health.status });
  let anonymousHistoryRequests = 0;
  anonPage.on('request', request => { if (new URL(request.url()).pathname.endsWith('/messages')) anonymousHistoryRequests++; });
  await anonPage.goto(`${origin}/?room=${readOnlyRoom}`, { waitUntil: 'networkidle' });
  await visible(anonPage, '#state-anonymous');
  await visible(anonPage, '#room-content');
  check('Anonymous arrival exposes room descriptions without private conversation',
    await anonPage.locator('#room-list .room-link').count() > 0
    && await anonPage.locator('#messages .message').count() === 0
    && !await anonPage.locator('#message-form').isVisible()
    && !await anonPage.locator('#site-setup').isVisible()
    && anonymousHistoryRequests === 0,
    { roomDescriptions: await anonPage.locator('#room-list .room-link').count(), renderedMessages: 0, privateHistoryRequests: anonymousHistoryRequests });
  const anonymousHistory = await api(anonymous, `/api/rooms/${readOnlyRoom}/messages`);
  check('Real anonymous history endpoint refuses access', [401, 403].includes(anonymousHistory.status), { status: anonymousHistory.status });
  await overflow(anonPage, 'Anonymous desktop 1440px');
  await screenshot(anonPage, 'anonymous-desktop.png');
  await anonPage.setViewportSize({ width: 390, height: 844 });
  await overflow(anonPage, 'Anonymous mobile 390px');
  await screenshot(anonPage, 'anonymous-mobile.png');

  stage = 'verified-owner-arrival';
  const owner = await browser.newContext({ viewport: { width: 1440, height: 1100 }, locale: 'en-US', acceptDownloads: false });
  await owner.addCookies(saved.cookies.map(([name, value]) => ({ name, value, url: origin, httpOnly: true, secure: false, sameSite: 'Lax' })));
  ownerPage = await owner.newPage();
  observe(ownerPage, 'owner');
  await ownerPage.goto(origin, { waitUntil: 'networkidle' });
  await visible(ownerPage, '#state-signed-in');
  await visible(ownerPage, '#owner-badge');
  const welcome = await api(owner, '/api/site');
  check('Real OAuth cookie resolves the verified persisted owner in the browser', welcome.status === 200 && welcome.body?.participant?.did === saved.did && welcome.body.participant.isOwner === true,
    { status: welcome.status, verifiedOwner: welcome.body?.participant?.isOwner === true, playwrightVersion: packageVersion });
  check('Seeded authentication cookies remain unavailable to page JavaScript', await ownerPage.evaluate(() => document.cookie === ''));

  stage = 'existing-shared-conversation-read-only';
  await ownerPage.goto(`${origin}/?room=${readOnlyRoom}`, { waitUntil: 'networkidle' });
  await ownerPage.locator('#messages .message').first().waitFor({ state: 'visible' });
  const sharedHistory = await api(owner, `/api/rooms/${readOnlyRoom}/messages`);
  check('Owner can open existing human and agent conversation without changing it', sharedHistory.status === 200 && sharedHistory.body?.messages?.length > 0,
    { status: sharedHistory.status, visibleMessages: await ownerPage.locator('#messages .message').count(), distinctVisibleAuthors: new Set(sharedHistory.body?.messages?.map(message => message.authorDid)).size });
  await ownerPage.locator('#messages .source-details').first().locator('summary').click();
  await overflow(ownerPage, 'Shared conversation desktop 1440px');
  await screenshot(ownerPage, 'shared-workshop-desktop.png');
  await ownerPage.setViewportSize({ width: 390, height: 844 });
  await overflow(ownerPage, 'Shared conversation mobile 390px');
  await screenshot(ownerPage, 'shared-workshop-mobile.png');
  await ownerPage.setViewportSize({ width: 1440, height: 1100 });

  stage = 'create-own-ui-room';
  await ownerPage.locator('#site-setup > summary').click();
  await ownerPage.locator('#create-room [name=title]').fill('The Commons');
  await ownerPage.locator('#create-room [name=key]').fill(roomKey);
  await ownerPage.locator('#create-room [name=admission]').selectOption('InvitationOnly');
  const created = await mutation(ownerPage, '/api/rooms', 'POST', () => ownerPage.locator('#create-room button').click());
  check('Owner creates a fresh room through the real form', created.status === 200 && created.body?.accepted === true, { status: created.status, audited: Boolean(created.body?.auditId) });
  await visible(ownerPage, '#provision-room');
  await ownerPage.locator('#site-setup > summary').click();
  stage = 'provision-own-ui-room';
  const provisioned = await mutation(ownerPage, `/api/rooms/${roomKey}/provision`, 'POST', () => ownerPage.locator('#provision-room').click());
  check('Finish room setup provisions a real Space', provisioned.status === 200 && provisioned.body?.accepted === true && provisioned.body?.spaceUri?.startsWith('at://'),
    { status: provisioned.status, realSpaceMapped: Boolean(provisioned.body?.spaceUri), audited: Boolean(provisioned.body?.auditId) });
  await visible(ownerPage, '#message-form');

  stage = 'topic-form';
  await ownerPage.locator('#room-admin > summary').click();
  const topic = 'A place to compare notes, ask questions, and keep a conversation going.';
  await ownerPage.locator('#topic-form [name=topic]').fill(topic);
  const changedTopic = await mutation(ownerPage, `/api/rooms/${roomKey}/topic`, 'PUT', () => ownerPage.locator('#topic-form button').click());
  check('Topic administration saves through current real room policy', changedTopic.status === 200 && changedTopic.body?.accepted === true, { status: changedTopic.status, audited: Boolean(changedTopic.body?.auditId) });
  await ownerPage.waitForFunction(expected => document.querySelector('#room-topic')?.textContent === expected, topic);
  if (await ownerPage.locator('#room-admin').evaluate(element => element.open)) await ownerPage.locator('#room-admin > summary').click();
  await visible(ownerPage, '#message-form');

  stage = 'compose-real-source-message';
  const firstText = 'What helps a new participant feel welcome in a shared conversation?';
  await ownerPage.locator('#message-text').fill(firstText);
  const first = await mutation(ownerPage, `/api/rooms/${roomKey}/messages`, 'POST', () => ownerPage.locator('#send-message').click());
  check('Browser compose produces an accepted real source reference', first.status === 200 && first.body?.state === 'accepted' && first.body?.sourceUri?.startsWith('at://') && Boolean(first.body?.sourceCid),
    { status: first.status, state: first.body?.state ?? null, hasSourceReference: Boolean(first.body?.sourceUri && first.body?.sourceCid) });
  await ownerPage.getByText(firstText, { exact: true }).waitFor({ state: 'visible' });
  const firstMessage = ownerPage.locator('#messages .message').filter({ hasText: firstText });
  await firstMessage.locator('.source-details > summary').click();
  const sourceDetails = await firstMessage.locator('.source-details').textContent();
  check('Message reveals matching source URI, CID and verified author DID', sourceDetails.includes(first.body.sourceUri) && sourceDetails.includes(first.body.sourceCid) && sourceDetails.includes(saved.did));
  await firstMessage.getByRole('button', { name: 'Reply', exact: true }).click();
  await visible(ownerPage, '#reply-context');

  stage = 'reply-to-real-source';
  const replyText = 'A clear room topic, visible authorship, and a small first exchange make it easier to join.';
  await ownerPage.locator('#message-text').fill(replyText);
  const replied = await mutation(ownerPage, `/api/rooms/${roomKey}/messages`, 'POST', () => ownerPage.locator('#send-message').click());
  check('Browser reply is accepted from the owner source repository', replied.status === 200 && replied.body?.state === 'accepted' && Boolean(replied.body?.sourceUri && replied.body?.sourceCid),
    { status: replied.status, state: replied.body?.state ?? null });
  await ownerPage.getByText(replyText, { exact: true }).waitFor({ state: 'visible' });
  const history = await api(owner, `/api/rooms/${roomKey}/messages`);
  const displayedReply = history.body?.messages?.find(message => message.sourceUri === replied.body.sourceUri);
  check('Real history retains the reply dependency and both source messages', history.status === 200 && history.body?.messages?.length === 2
    && displayedReply?.content?.replyTo?.uri === first.body.sourceUri && displayedReply?.content?.replyTo?.cid === first.body.sourceCid,
    { status: history.status, messages: history.body?.messages?.length ?? 0, replySourceMatches: displayedReply?.content?.replyTo?.uri === first.body.sourceUri });
  check('Browser renders the reply relationship', await ownerPage.locator('#messages .reply-label').count() === 1);
  await ownerPage.locator('#messages .source-details').first().locator('summary').click();
  await ownerPage.locator('#room-title').scrollIntoViewIfNeeded();
  await overflow(ownerPage, 'Owner desktop 1440px');
  await screenshot(ownerPage, 'owner-desktop.png');
  await ownerPage.setViewportSize({ width: 390, height: 844 });
  await overflow(ownerPage, 'Owner mobile 390px');
  await screenshot(ownerPage, 'owner-mobile.png');

  stage = 'assets-and-runtime';
  check('Both application scripts and stylesheets load successfully', ['/app.css', '/rooms.css', '/app.js', '/rooms.js'].every(path => diagnostics.assets[path] === 200), { assets: diagnostics.assets });
  check('No browser JavaScript errors, failed requests or failed UI responses', diagnostics.pageErrors.length === 0 && diagnostics.consoleErrors === 0
    && diagnostics.failedRequests.length === 0 && diagnostics.failedResponses.length === 0, diagnostics);
  completed = true;
} catch (error) {
  // Do not emit raw browser errors, which can include request headers or state.
  checks.push({ name: `Proof completed at ${stage}`, passed: false,
    observed: { error: ['ProofCheckFailed', 'InvalidCookieFixture'].includes(error.message) ? error.message
      : error.message?.includes('strict mode violation') ? 'AmbiguousProofLocator' : /^[A-Za-z]+$/.test(error.name) ? error.name : 'ProofError' } });
  if (ownerPage && ownerPage.url().startsWith(origin + '/')) {
    try { await screenshot(ownerPage, 'failure.png'); } catch { /* Preserve the redacted failed receipt. */ }
  }
} finally {
  if (browser) await browser.close();
  await mkdir(dirname(evidenceFile), { recursive: true });
  await writeFile(evidenceFile, JSON.stringify({ observedAt: new Date().toISOString(), passed: completed, origin, roomKey, browserVersion,
    checks, screenshots, diagnostics,
    limits: ['Isolated nonpersistent headless Chrome contexts; no user browser profile or mocked API responses.',
      'The earlier real OAuth flow supplied the genuine Koan cookie. This proof exercises browser behavior, not provider password entry.',
      'Only the new UI room was created, provisioned, given a topic, or written to. Existing room governance and source content were untouched.',
      'All screenshot text is actual rendered fixture content. Cookies, provider tokens, passwords and protected state were never logged or captured.',
      '1440px and 390px viewport checks cover horizontal overflow; saved screenshots require visual inspection.'],
    references: ['https://playwright.dev/docs/api/class-browsercontext#browser-context-add-cookies', 'https://playwright.dev/docs/api/class-page#page-screenshot']
  }, null, 2) + '\n');
  console.log(JSON.stringify({ passed: completed, checks: checks.length, stage, evidence: 'docs/evidence/ui.json', screenshots }));
  if (!completed) process.exitCode = 1;
}
