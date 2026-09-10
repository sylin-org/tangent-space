// Read-only live checks; no credentials or draft contents enter the receipt.
import { readFile, writeFile } from 'node:fs/promises';
const origin = 'http://127.0.0.1:5220';
const fixture = JSON.parse(await readFile('.local/spaces-network/fixtures.json', 'utf8'));
const directory = '.local/demo/' + Date.parse(fixture.startedAt);
const credential = JSON.parse(await readFile(directory + '/agent-credential.json', 'utf8'));
const outsider = JSON.parse(await readFile(directory + '/outsider.cookies.json', 'utf8'));
if (fixture.status !== 'ready' || outsider.origin !== origin) throw new Error('Expected the existing local fixture network.');
const checks = [];
async function get(path, actor = 'agent', expected) {
  const headers = actor === 'agent' ? { Authorization: 'Bearer ' + credential.token }
    : actor === 'outsider' ? { Cookie: outsider.cookies.map(([name, value]) => name + '=' + value).join('; ') } : {};
  if (expected) headers['X-Tangent-Participant'] = expected;
  const response = await fetch(origin + path, { headers, redirect: 'error', signal: AbortSignal.timeout(15000) });
  return { status: response.status, data: await response.json() };
}
function check(name, passed) { checks.push({ name, passed }); if (!passed) throw new Error('Check failed: ' + name); }
const path = '/api/rooms/tangent-lounge/messages/pending';
const own = await get(path);
check('current agent can inspect its own pending page', own.status === 200 && Array.isArray(own.data.messages) && own.data.messages.length <= 8);
check('query parameters cannot select another participant drafts', JSON.stringify((await get(path + '?did=did:plc:5rqf45qvouvadvz26a4m4al3')).data) === JSON.stringify(own.data));
check('stale displayed identity cannot read a new cookie actor drafts', (await get(path, 'agent', 'did:plc:5rqf45qvouvadvz26a4m4al3')).status === 409);
check('private pending page requires authentication', (await get(path, 'anonymous')).status === 401);
check('uninvited participant cannot recover Workshop drafts', (await get('/api/rooms/tangent-workshop/messages/pending', 'outsider')).status === 403);
check('pending recovery response exposes only necessary draft fields', own.data.messages.every(message => Object.keys(message).every(key => ['operationId', 'text', 'replyTo', 'detail'].includes(key))));
const receipt = { observedAt: new Date().toISOString(), origin, transport: 'Read-only HTTP', checks };
await writeFile('docs/evidence/pending-access.json', JSON.stringify(receipt, null, 2) + '\n');
console.log(JSON.stringify({ passed: checks.length, receipt: 'docs/evidence/pending-access.json' }));
