// Read-only HTTP checks complement (and never substitute for) actual WebMCP invocation.
import { readFile, writeFile } from 'node:fs/promises';
const origin = 'http://127.0.0.1:5220';
const fixture = JSON.parse(await readFile('.local/spaces-network/fixtures.json', 'utf8'));
if (fixture.status !== 'ready' || !Number.isFinite(Date.parse(fixture.startedAt))) throw new Error('A running disposable fixture network is required.');
const directory = '.local/demo/' + Date.parse(fixture.startedAt);
const credential = JSON.parse(await readFile(directory + '/agent-credential.json', 'utf8'));
const outsider = JSON.parse(await readFile(directory + '/outsider.cookies.json', 'utf8'));
if (outsider.origin !== origin) throw new Error('Fixture origin differs.');
const checks = [];
async function get(path, auth = 'agent') {
  const headers = auth === 'agent' ? { Authorization: 'Bearer ' + credential.token }
    : auth === 'outsider' ? { Cookie: outsider.cookies.map(([name, value]) => name + '=' + value).join('; ') } : {};
  const response = await fetch(origin + path, { headers, redirect: 'error', signal: AbortSignal.timeout(25000) });
  return { status: response.status, data: await response.json() };
}
function check(name, passed) { checks.push({ name, passed }); if (!passed) throw new Error('Check failed: ' + name); }
const before = await get('/api/rooms/tangent-lounge/messages');
const beginning = await get('/api/rooms/tangent-lounge/messages?from=start');
const after = await get('/api/rooms/tangent-lounge/messages');
check('explicit reread finds acknowledged history', beginning.status === 200 && beginning.data.messages.length > 0);
check('rereading does not change saved position', JSON.stringify(before.data.messages) === JSON.stringify(after.data.messages));
const cursor = encodeURIComponent(beginning.data.resumeCursor);
check('cursor and explicit origin cannot be combined', (await get('/api/rooms/tangent-lounge/messages?from=start&cursor=' + cursor)).status === 400);
check('unsupported history origins fail', (await get('/api/rooms/tangent-lounge/messages?from=unknown')).status === 400);
check('wait rejects cross-room cursor', (await get('/api/rooms/tangent-workshop/updates?cursor=' + cursor)).status === 400);
check('wait rejects malformed cursor', (await get('/api/rooms/tangent-lounge/updates?cursor=invalid')).status === 400);
check('wait requires authentication', (await get('/api/rooms/tangent-lounge/updates?cursor=' + cursor, 'anonymous')).status === 401);
check('outsider cannot read Workshop history', (await get('/api/rooms/tangent-workshop/messages?from=start', 'outsider')).status === 403);
check('outsider cannot subscribe to Workshop', (await get('/api/rooms/tangent-workshop/updates?cursor=' + cursor, 'outsider')).status === 403);
const receipt = { observedAt: new Date().toISOString(), origin, transport: 'HTTP; native WebMCP proof recorded separately', checks };
await writeFile('docs/evidence/webmcp-http.json', JSON.stringify(receipt, null, 2) + '\n');
console.log(JSON.stringify({ passed: checks.length, failures: checks.filter(check => !check.passed).length, receipt: 'docs/evidence/webmcp-http.json' }));
