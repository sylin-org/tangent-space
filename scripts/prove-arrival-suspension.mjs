import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';

// Run after deploying the shared PolicyGate. Real disposable OAuth flows
// overlap audited suspension writes; raw auth responses and cookies are not logged.
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const args = new Map();
for (let index = 2; index < process.argv.length; index += 2) {
  if (!process.argv[index]?.startsWith('--') || !process.argv[index + 1]) throw new Error('Use --name value arguments.');
  args.set(process.argv[index].slice(2), process.argv[index + 1]);
}
const origin = args.get('origin') ?? 'http://127.0.0.1:5223';
if (!/^http:\/\/127\.0\.0\.1:\d+$/.test(origin)) throw new Error('Use the disposable loopback host.');
const cookieDirectory = resolve(root, args.get('cookie-directory') ?? '.local/tangent/1788984280268/rooms-proof/room-proof-poc-1788987688033');
const ignored = resolve(root, '.local');
if (!cookieDirectory.startsWith(ignored + '/') && !cookieDirectory.startsWith(ignored + '\\')) throw new Error('Cookie source must be ignored local state.');
const fixturePath = resolve(root, '.local/spaces-network/fixtures.json');
const fixture = JSON.parse(await readFile(fixturePath, 'utf8'));
const outsider = fixture.accounts.find(account => account.role === 'outsider');
const owner = fixture.accounts.find(account => account.role === 'owner');
const roomKey = args.get('room') ?? 'poc-1788987688033-workshop';
if (!/^[a-z0-9-]{1,64}$/.test(roomKey)) throw new Error('Invalid room key.');
const privateDirectory = resolve(root, `.local/tangent/arrival-suspension-${Date.now()}`);
await mkdir(privateDirectory, { recursive: true });
const checks = [];
const auditIds = [];
let stage = 'setup';
let suspensionAttempted = false;
let completed = false;
let restored = false;

async function cookie(path, did) {
  const saved = JSON.parse(await readFile(path, 'utf8'));
  if (saved.origin !== origin || saved.did !== did || !Array.isArray(saved.cookies)) throw new Error('CookieFixtureMismatch');
  return saved.cookies.map(([name, value]) => `${name}=${value}`).join('; ');
}
const ownerCookie = await cookie(resolve(cookieDirectory, 'owner.cookies.json'), owner.did);
const outsiderCookie = await cookie(resolve(cookieDirectory, 'outsider.cookies.json'), outsider.did);
async function request(authCookie, path, body) {
  const response = await fetch(origin + path, { method: body ? 'PUT' : 'GET', redirect: 'error', signal: AbortSignal.timeout(30000),
    headers: { Accept: 'application/json', Cookie: authCookie, ...(body ? { Origin: origin, 'Content-Type': 'application/json' } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {}) });
  let data;
  try { data = await response.json(); } catch { data = null; }
  return { status: response.status, data };
}
function check(name, passed, observed = {}) {
  checks.push({ name, passed: Boolean(passed), observed });
  if (!passed) throw new Error('ProofCheckFailed');
}
async function suspension(value) {
  suspensionAttempted = true;
  const result = await request(ownerCookie, `/api/site/participants/${encodeURIComponent(outsider.did)}/suspension`, { suspended: value });
  if (result.status !== 200 || result.data?.accepted !== true || !result.data?.auditId) throw new Error('SuspensionRejected');
  auditIds.push(result.data.auditId);
  return result;
}
async function arrival(index) {
  const path = resolve(privateDirectory, `outsider-${index}.cookies.json`);
  await new Promise((complete, reject) => {
    const child = spawn(process.execPath, [resolve(root, 'probes/scripts/oauth-flow.mjs'), '--web', '--probe', origin,
      '--accounts', fixturePath, '--account', 'outsider', '--cookie-file', path], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    // The existing driver emits a redacted receipt, but keep even that output private to this wrapper.
    let bytes = 0;
    const consume = data => { bytes += data.length; if (bytes > 65536) child.kill(); };
    child.stdout.on('data', consume); child.stderr.on('data', consume);
    const timeout = setTimeout(() => child.kill(), 60000);
    child.once('error', () => { clearTimeout(timeout); reject(new Error('OAuthProcessFailed')); });
    child.once('exit', code => { clearTimeout(timeout); code === 0 ? complete() : reject(new Error('OAuthArrivalFailed')); });
  });
  return cookie(path, outsider.did);
}

try {
  const before = await request(outsiderCookie, `/api/rooms/${roomKey}`);
  check('Existing outsider begins unsuspended without changing its room role', before.status === 200 && before.data?.accessState !== 'suspended', { status: before.status, accessState: before.data?.accessState ?? null });
  stage = 'overlapping-real-arrivals-and-suspension';
  let finishedArrivals = 0;
  const arrivals = Array.from({ length: 4 }, (_, index) => arrival(index).finally(() => { finishedArrivals++; }));
  const adminWrites = (async () => {
    let writesWhileArrivalActive = 0;
    for (let index = 0; index < 12; index++) {
      if (finishedArrivals < arrivals.length) writesWhileArrivalActive++;
      await suspension(true);
      await new Promise(resolve => setTimeout(resolve, 20));
    }
    return writesWhileArrivalActive;
  })();
  const outcomes = await Promise.allSettled([...arrivals, adminWrites]);
  check('Four real provider OAuth arrivals and twelve overlapping suspension commits complete', outcomes.every(result => result.status === 'fulfilled')
    && outcomes[4].value > 0 && auditIds.length === 12,
    { successfulArrivals: outcomes.slice(0, 4).filter(result => result.status === 'fulfilled').length,
      suspensionCommits: auditIds.length, writesWhileArrivalActive: outcomes[4].status === 'fulfilled' ? outcomes[4].value : 0 });
  const after = await request(outsiderCookie, `/api/rooms/${roomKey}`);
  check('Existing cookie sees durable suspension after every concurrent arrival completes', after.status === 200 && after.data?.accessState === 'suspended' && !after.data?.canRead && !after.data?.canWrite,
    { status: after.status, accessState: after.data?.accessState ?? null, canRead: after.data?.canRead, canWrite: after.data?.canWrite });
  stage = 'arrival-after-suspension';
  const freshCookie = await arrival('after-suspension');
  const fresh = await request(freshCookie, `/api/rooms/${roomKey}`);
  check('A subsequent genuine OAuth sign-in preserves suspension with its new cookie', fresh.status === 200 && fresh.data?.accessState === 'suspended' && !fresh.data?.canRead && !fresh.data?.canWrite,
    { status: fresh.status, accessState: fresh.data?.accessState ?? null });
  const history = await request(freshCookie, `/api/rooms/${roomKey}/messages`);
  check('A newly issued verified cookie cannot read while suspended', history.status === 403, { status: history.status });
  stage = 'restore-outsider';
  await suspension(false);
  restored = true;
  const final = await request(freshCookie, `/api/rooms/${roomKey}`);
  check('Restoration retains the pre-existing room decision', final.status === 200 && final.data?.accessState === before.data.accessState
    && final.data?.canRead === before.data.canRead && final.data?.canWrite === before.data.canWrite,
    { status: final.status, accessState: final.data?.accessState ?? null });
  completed = true;
} catch (error) {
  checks.push({ name: `Proof completed at ${stage}`, passed: false, observed: { error: ['ProofCheckFailed', 'SuspensionRejected', 'OAuthArrivalFailed', 'CookieFixtureMismatch'].includes(error.message) ? error.message : 'ProofError' } });
} finally {
  if (suspensionAttempted && !restored) {
    try { await suspension(false); restored = true; } catch { /* Receipt makes incomplete cleanup explicit. */ }
  }
  const receipt = { observedAt: new Date().toISOString(), passed: completed && restored, origin, checks, auditIds, outsiderRestored: restored,
    limits: ['Real disposable provider OAuth flows and real audited HTTP suspension writes overlap; no fake identity, PDS, or persistence backend.',
      'This bounded concurrency run observes the durable result but does not force every possible database scheduling interleaving.',
      'The shared DI PolicyGate and NoCache reload close the stale Participant overwrite path structurally; focused domain tests verify returning identity preserves suspension.',
      'No room membership, topic, admission, or source records changed. Only the disposable outsider suspension was changed and restored.',
      'Cookies, passwords, authorization tokens and OAuth callback URLs are absent from this receipt.'] };
  await writeFile(resolve(root, 'docs/evidence/arrival-suspension.json'), JSON.stringify(receipt, null, 2) + '\n');
  console.log(JSON.stringify({ passed: receipt.passed, checks: checks.length, outsiderRestored: restored, evidence: 'docs/evidence/arrival-suspension.json' }));
  if (!receipt.passed) process.exitCode = 1;
}
